using System.Text;
using AngleSharp.Html.Parser;

namespace Campfire.Application.RichText.Html.Parsing;

/// <summary>What the pending characters will become: Gumbo's <c>TextNodeBufferState._type</c>.</summary>
internal enum TextBufferType
{
    Whitespace,
    Text,
    CData,
}

/// <summary>
/// A port of the tree construction stage of Gumbo as Nokogiri 1.19.4 ships it
/// (gumbo-parser/src/parser.c), fed by AngleSharp's tokenizer. Rails parses every Action Text
/// fragment with it, so this builds the trees Rails builds, including where Gumbo departs from the
/// HTML standard (see <see cref="GumboTagSets"/> and the adoption agency). Only the fragment case is
/// supported; the functions and branches keep Gumbo's names and order so the two read side by side.
/// Parse errors, source positions and parse flags, which don't shape the tree, are not tracked.
/// </summary>
internal sealed partial class GumboTreeBuilder : IDisposable
{
    private readonly HtmlTokenSource _tokens;
    private readonly HtmlNode _document = HtmlNode.CreateFragment( );
    private readonly HtmlNode _contextElement;
    private readonly List<HtmlNode> _openElements = [];

    // null entries are scope markers
    private readonly List<HtmlNode?> _activeFormattingElements = [];
    private readonly List<InsertionMode> _templateInsertionModes = [];
    private readonly StringBuilder _textBuffer = new( );
    private readonly int _maxOpenElements;

    private HtmlNode? _root;
    private HtmlNode? _headElement;
    private HtmlNode? _formElement;
    private InsertionMode _insertionMode = InsertionMode.Initial;
    private InsertionMode _originalInsertionMode = InsertionMode.Initial;
    private TextBufferType _textBufferType = TextBufferType.Whitespace;
    private bool _framesetOk = true;
    private bool _fosterParentInsertions;
    private bool _ignoreNextLinefeed;
    private bool _reprocessCurrentToken;

    private GumboTreeBuilder( string html, ParseContext context, int maxTreeDepth, int maxAttributes )
    {
        _tokens = new HtmlTokenSource( html, maxAttributes );
        _contextElement = context.CreateElement( );

        // Nokogiri adds one to the depth limit to account for the fragment's html element
        _maxOpenElements = maxTreeDepth + 1;
    }

    /// <summary>
    /// <c>Nokogiri::Gumbo.fragment</c>: parses <paramref name="html"/> in <paramref name="context"/> and
    /// returns the fragment's top-level nodes, detached.
    /// </summary>
    public static List<HtmlNode> ParseFragment( string html, ParseContext context, int maxTreeDepth, int maxAttributes )
    {
        using var builder = new GumboTreeBuilder( html, context, maxTreeDepth, maxAttributes );
        builder.InitializeFragment( context.HasFormAncestor );
        builder.Run( );

        var root = builder._root!;
        MergeAdjacentText( root );

        return root.DetachChildren( );
    }

    public void Dispose( ) => _tokens.Dispose( );

    /// <summary><c>fragment_parser_init</c>.</summary>
    private void InitializeFragment( bool hasFormAncestor )
    {
        if ( _contextElement.Namespace == HtmlNamespace.Html )
        {
            _tokens.State = _contextElement.LocalName switch
            {
                "title" or "textarea" => HtmlParseMode.RCData,
                "style" or "xmp" or "iframe" or "noembed" or "noframes" => HtmlParseMode.Rawtext,
                "script" => HtmlParseMode.Script,
                "plaintext" => HtmlParseMode.Plaintext,
                _ => HtmlParseMode.PCData,
            };
        }

        _root = InsertElementOfTagType( "html" );
        if ( IsHtml( _contextElement, "template" ) )
        {
            _templateInsertionModes.Add( InsertionMode.InTemplate );
        }

        ResetInsertionModeAppropriately( );
        if ( hasFormAncestor || IsHtml( _contextElement, "form" ) )
        {
            // Gumbo points at a form that is never on the stack of open elements
            _formElement = HtmlNode.CreateElement( HtmlNamespace.Html, "form" );
        }
    }

    /// <summary>
    /// <c>gumbo_parse_with_options</c>' loop. Before lexing each token Gumbo checks the depth of the
    /// stack of open elements, and its tokenizer counts attributes; Nokogiri raises for either.
    /// </summary>
    private void Run( )
    {
        var token = ParserToken.EndOfFile;
        do
        {
            if ( _reprocessCurrentToken )
            {
                _reprocessCurrentToken = false;
            }
            else
            {
                if ( _openElements.Count > _maxOpenElements )
                {
                    throw new RichTextParseException( RichTextParseError.TreeDepthExceeded );
                }

                token = _tokens.Next( IsAdjustedCurrentNodeForeign( ) );
                if ( token.Kind is ParserTokenKind.StartTag or ParserTokenKind.EndTag && _tokens.LastTagExceededAttributeLimit )
                {
                    throw new RichTextParseException( RichTextParseError.TooManyAttributes );
                }
            }

            HandleToken( token );
        }
        while ( token.Kind != ParserTokenKind.EndOfFile || _reprocessCurrentToken );

        FinishParsing( );
    }

    /// <summary><c>finish_parsing</c>.</summary>
    private void FinishParsing( )
    {
        FlushTextBuffer( );
        while ( PopCurrentNode( ) is not null )
        {
        }
    }

    // --- Nodes and the stack of open elements ---------------------------------------------------

    private HtmlNode CurrentNode => _openElements[^1];

    /// <summary><c>get_adjusted_current_node</c>: the context element while only the fragment's root is open.</summary>
    private HtmlNode? AdjustedCurrentNode
    {
        get
        {
            if ( _openElements.Count == 0 )
            {
                return null;
            }

            return _openElements.Count == 1 ? _contextElement : CurrentNode;
        }
    }

    private bool IsAdjustedCurrentNodeForeign( ) => AdjustedCurrentNode is { } node && node.Namespace != HtmlNamespace.Html;

    private static bool IsHtml( HtmlNode node, string name ) => node.IsHtmlElement && node.LocalName == name;

    private static bool IsHtmlIn( HtmlNode node, IReadOnlySet<string> names ) => node.IsHtmlElement && names.Contains( node.LocalName );

    /// <summary><c>node_tagname_is</c>: the node's name in its own namespace, ignoring ASCII case.</summary>
    private static bool TagNameIs( HtmlNode node, string name ) => Ascii.EqualsIgnoreCase( node.LocalName, name );

    /// <summary><c>is_mathml_integration_point</c>.</summary>
    private static bool IsMathMlIntegrationPoint( HtmlNode node ) =>
        node.Namespace == HtmlNamespace.MathMl && GumboTagSets.MathMlTextIntegrationPoints.Contains( node.LocalName );

    /// <summary><c>is_html_integration_point</c>.</summary>
    private static bool IsHtmlIntegrationPoint( HtmlNode node )
    {
        if ( node.Namespace == HtmlNamespace.Svg && GumboTagSets.SvgHtmlIntegrationPoints.Contains( node.LocalName ) )
        {
            return true;
        }

        return node is { Namespace: HtmlNamespace.MathMl, LocalName: "annotation-xml" }
               && (AttributeMatches( node, "encoding", "text/html" ) || AttributeMatches( node, "encoding", "application/xhtml+xml" ));
    }

    /// <summary><c>is_special_node</c>.</summary>
    private static bool IsSpecial( HtmlNode node ) => node.Namespace switch
    {
        HtmlNamespace.Html => GumboTagSets.SpecialHtml.Contains( node.LocalName ),
        HtmlNamespace.MathMl => GumboTagSets.MathMlTextIntegrationPoints.Contains( node.LocalName ) || node.LocalName == "annotation-xml",
        _ => GumboTagSets.SvgHtmlIntegrationPoints.Contains( node.LocalName ),
    };

    private static bool AttributeMatches( HtmlNode node, string name, string value ) =>
        node.GetAttribute( name ) is { } found && Ascii.EqualsIgnoreCase( found, value );

    /// <summary><c>pop_current_node</c>: pending text is flushed first.</summary>
    private HtmlNode? PopCurrentNode( )
    {
        FlushTextBuffer( );
        if ( _openElements.Count == 0 )
        {
            return null;
        }

        var node = _openElements[^1];
        _openElements.RemoveAt( _openElements.Count - 1 );

        return node;
    }

    /// <summary>Pops elements until one satisfying <paramref name="isTarget"/> has been popped.</summary>
    private void PopUntil( Func<HtmlNode, bool> isTarget )
    {
        while ( PopCurrentNode( ) is { } node && !isTarget( node ) )
        {
        }
    }

    /// <summary>Pops elements until <paramref name="target"/> itself has been popped (or the stack is empty).</summary>
    private void PopThrough( HtmlNode target )
    {
        while ( PopCurrentNode( ) is { } node && node != target )
        {
        }
    }

    private void PopUntilHtml( string name ) => PopUntil( node => IsHtml( node, name ) );

    private bool HasOpenElement( string name ) => _openElements.Exists( node => IsHtml( node, name ) );

    private bool IsOpenElement( HtmlNode node ) => _openElements.Contains( node );

    private void RemoveFromOpenElements( HtmlNode node )
    {
        var index = _openElements.LastIndexOf( node );
        if ( index >= 0 )
        {
            _openElements.RemoveAt( index );
        }
    }

    // --- Scopes ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>has_an_element_in_specific_scope</c>: whether an open element satisfying <paramref name="isExpected"/> comes
    /// before (from the current node down) any element satisfying <paramref name="isScopeBoundary"/>.
    /// </summary>
    private bool HasElementInSpecificScope( Func<HtmlNode, bool> isExpected, Func<HtmlNode, bool> isScopeBoundary )
    {
        for ( var index = _openElements.Count - 1; index >= 0; index-- )
        {
            var node = _openElements[index];
            if ( isExpected( node ) )
            {
                return true;
            }

            if ( isScopeBoundary( node ) )
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsDefaultScopeBoundary( HtmlNode node ) => node.Namespace switch
    {
        HtmlNamespace.Html => GumboTagSets.DefaultScopeHtml.Contains( node.LocalName ),
        HtmlNamespace.MathMl => GumboTagSets.MathMlTextIntegrationPoints.Contains( node.LocalName ) || node.LocalName == "annotation-xml",
        _ => GumboTagSets.SvgHtmlIntegrationPoints.Contains( node.LocalName ),
    };

    private bool HasElementInScope( string name ) => HasElementInSpecificScope( node => IsHtml( node, name ), IsDefaultScopeBoundary );

    private bool HasElementInScope( IReadOnlySet<string> names ) => HasElementInSpecificScope( node => IsHtmlIn( node, names ), IsDefaultScopeBoundary );

    /// <summary><c>has_node_in_scope</c>: this exact node, within the default scope.</summary>
    private bool HasNodeInScope( HtmlNode target ) => HasElementInSpecificScope( node => node == target, IsDefaultScopeBoundary );

    private bool HasElementInListScope( string name ) =>
        HasElementInSpecificScope(
            node => IsHtml( node, name ),
            static node => IsDefaultScopeBoundary( node ) || IsHtml( node, "ol" ) || IsHtml( node, "ul" ) );

    private bool HasElementInButtonScope( string name ) =>
        HasElementInSpecificScope( node => IsHtml( node, name ), static node => IsDefaultScopeBoundary( node ) || IsHtml( node, "button" ) );

    private bool HasElementInTableScope( string name ) =>
        HasElementInSpecificScope(
            node => IsHtml( node, name ),
            static node => IsHtml( node, "html" ) || IsHtml( node, "table" ) || IsHtml( node, "template" ) );

    /// <summary>Select scope is the inverse of the others: everything but <c>optgroup</c> and <c>option</c> bounds it.</summary>
    private bool HasElementInSelectScope( string name ) =>
        HasElementInSpecificScope( node => IsHtml( node, name ), static node => !IsHtml( node, "optgroup" ) && !IsHtml( node, "option" ) );

    // --- Implied end tags -----------------------------------------------------------------------

    /// <summary><c>generate_implied_end_tags</c>, except for elements named <paramref name="exception"/>.</summary>
    private void GenerateImpliedEndTags( string? exception = null )
    {
        while ( _openElements.Count > 0 && IsHtmlIn( CurrentNode, GumboTagSets.ImpliedEndTags ) && !IsHtml( CurrentNode, exception ?? "" ) )
        {
            PopCurrentNode( );
        }
    }

    private void GenerateAllImpliedEndTagsThoroughly( )
    {
        while ( _openElements.Count > 0 && IsHtmlIn( CurrentNode, GumboTagSets.ImpliedEndTagsThoroughly ) )
        {
            PopCurrentNode( );
        }
    }

    /// <summary><c>implicitly_close_tags</c>: closes up to and including the named element.</summary>
    private void ImplicitlyCloseTags( HtmlNamespace elementNamespace, string name )
    {
        GenerateImpliedEndTags( elementNamespace == HtmlNamespace.Html ? name : null );
        while ( !(CurrentNode.Namespace == elementNamespace && CurrentNode.LocalName == name) )
        {
            PopCurrentNode( );
        }

        PopCurrentNode( );
    }

    private void ImplicitlyCloseTags( string htmlName ) => ImplicitlyCloseTags( HtmlNamespace.Html, htmlName );

    /// <summary><c>maybe_implicitly_close_p_tag</c>.</summary>
    private void MaybeImplicitlyClosePTag( )
    {
        if ( HasElementInButtonScope( "p" ) )
        {
            ImplicitlyCloseTags( "p" );
        }
    }

    /// <summary><c>maybe_implicitly_close_list_tag</c>: an open <c>li</c> (or <c>dd</c>/<c>dt</c>) before a special element.</summary>
    private void MaybeImplicitlyCloseListTag( bool isLi )
    {
        _framesetOk = false;
        for ( var index = _openElements.Count - 1; index >= 0; index-- )
        {
            var node = _openElements[index];
            var isListTag = isLi ? IsHtml( node, "li" ) : IsHtmlIn( node, GumboTagSets.DefinitionListItems );
            if ( isListTag )
            {
                ImplicitlyCloseTags( node.Namespace, node.LocalName );
                return;
            }

            if ( IsSpecial( node ) && !IsHtml( node, "address" ) && !IsHtml( node, "div" ) && !IsHtml( node, "p" ) )
            {
                return;
            }
        }
    }

    /// <summary><c>reset_insertion_mode_appropriately</c>.</summary>
    private void ResetInsertionModeAppropriately( )
    {
        for ( var index = _openElements.Count - 1; index >= 0; index-- )
        {
            if ( AppropriateInsertionMode( index ) is { } mode )
            {
                _insertionMode = mode;
                return;
            }
        }

        _insertionMode = InsertionMode.InBody;
    }

    /// <summary><c>get_appropriate_insertion_mode</c>, with <c>null</c> for "keep looking".</summary>
    private InsertionMode? AppropriateInsertionMode( int index )
    {
        var isLast = index == 0;
        var node = isLast ? _contextElement : _openElements[index];
        if ( !node.IsHtmlElement )
        {
            return isLast ? InsertionMode.InBody : null;
        }

        InsertionMode? mode = node.LocalName switch
        {
            "select" => isLast ? InsertionMode.InSelect : SelectInsertionMode( index ),
            "td" or "th" when !isLast => InsertionMode.InCell,
            "tr" => InsertionMode.InRow,
            "tbody" or "thead" or "tfoot" => InsertionMode.InTableBody,
            "caption" => InsertionMode.InCaption,
            "colgroup" => InsertionMode.InColumnGroup,
            "table" => InsertionMode.InTable,
            "template" => CurrentTemplateInsertionMode,
            "head" when !isLast => InsertionMode.InHead,
            "body" => InsertionMode.InBody,
            "frameset" => InsertionMode.InFrameset,
            "html" => _headElement is null ? InsertionMode.BeforeHead : InsertionMode.AfterHead,
            _ => null,
        };

        return mode ?? (isLast ? InsertionMode.InBody : null);
    }

    private InsertionMode SelectInsertionMode( int index )
    {
        for ( var ancestorIndex = index; ancestorIndex > 0; ancestorIndex-- )
        {
            if ( IsHtml( _openElements[ancestorIndex], "template" ) )
            {
                return InsertionMode.InSelect;
            }

            if ( IsHtml( _openElements[ancestorIndex], "table" ) )
            {
                return InsertionMode.InSelectInTable;
            }
        }

        return InsertionMode.InSelect;
    }

    /// <summary><c>get_current_template_insertion_mode</c>: <see cref="InsertionMode.Initial"/> when there is none.</summary>
    private InsertionMode CurrentTemplateInsertionMode => _templateInsertionModes.Count == 0 ? InsertionMode.Initial : _templateInsertionModes[^1];

    private void PushTemplateInsertionMode( InsertionMode mode ) => _templateInsertionModes.Add( mode );

    private void PopTemplateInsertionMode( ) => _templateInsertionModes.RemoveAt( _templateInsertionModes.Count - 1 );

    private void ReplaceTemplateInsertionMode( InsertionMode mode )
    {
        PopTemplateInsertionMode( );
        PushTemplateInsertionMode( mode );
        _insertionMode = mode;
        _reprocessCurrentToken = true;
    }

    /// <summary>Joins the text nodes the tree builder left adjacent, as libxml2's <c>xmlAddChild</c> does when Nokogiri builds its tree.</summary>
    private static void MergeAdjacentText( HtmlNode root )
    {
        var pending = new Stack<HtmlNode>( );
        pending.Push( root );
        while ( pending.TryPop( out var node ) )
        {
            node.MergeAdjacentTextChildren( );
            foreach ( var child in node.Children )
            {
                if ( child.IsElement )
                {
                    pending.Push( child );
                }
            }
        }
    }
}
