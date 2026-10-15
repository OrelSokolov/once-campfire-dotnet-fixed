using AngleSharp.Html.Parser;

namespace Campfire.Application.RichText.Html.Parsing;

internal sealed partial class GumboTreeBuilder
{
    /// <summary>Where a node goes: at the end of <see cref="Target"/>, or before its child at <see cref="Index"/>.</summary>
    private readonly record struct InsertionLocation( HtmlNode Target, int Index );

    /// <summary><c>handle_token</c>: HTML content or foreign content, by the adjusted current node.</summary>
    private void HandleToken( ParserToken token )
    {
        if ( _ignoreNextLinefeed && token.Kind == ParserTokenKind.Whitespace && token.Text[0] == '\n' )
        {
            _ignoreNextLinefeed = false;
            if ( token.Text.Length == 1 )
            {
                return;
            }

            token = new ParserToken( ParserTokenKind.Whitespace, text: token.Text[1..] );
        }

        _ignoreNextLinefeed = false;
        if ( IsForHtmlContent( token ) )
        {
            HandleHtmlContent( token );
        }
        else
        {
            HandleInForeignContent( token );
        }
    }

    private bool IsForHtmlContent( ParserToken token )
    {
        if ( AdjustedCurrentNode is not { } node || node.IsHtmlElement || token.Kind == ParserTokenKind.EndOfFile )
        {
            return true;
        }

        var isText = token.Kind is ParserTokenKind.Character or ParserTokenKind.Whitespace or ParserTokenKind.Null;
        if ( IsMathMlIntegrationPoint( node ) && (isText || token is { Kind: ParserTokenKind.StartTag, Name: not ("mglyph" or "malignmark") }) )
        {
            return true;
        }

        if ( node is { Namespace: HtmlNamespace.MathMl, LocalName: "annotation-xml" } && token.IsStartTag( "svg" ) )
        {
            return true;
        }

        return IsHtmlIntegrationPoint( node ) && (token.Kind == ParserTokenKind.StartTag || isText);
    }

    /// <summary><c>handle_html_content</c>: the handler for the current insertion mode.</summary>
    private void HandleHtmlContent( ParserToken token )
    {
        switch ( _insertionMode )
        {
            case InsertionMode.InBody:
                HandleInBody( token );
                break;
            case InsertionMode.Text:
                HandleText( token );
                break;
            case InsertionMode.InTable or InsertionMode.InTableText or InsertionMode.InCaption or InsertionMode.InColumnGroup
                or InsertionMode.InTableBody or InsertionMode.InRow or InsertionMode.InCell:
                HandleInTableModes( token );
                break;
            case InsertionMode.InSelect:
                HandleInSelect( token );
                break;
            case InsertionMode.InSelectInTable:
                HandleInSelectInTable( token );
                break;
            case InsertionMode.InTemplate:
                HandleInTemplate( token );
                break;
            default:
                HandleOutsideBody( token );
                break;
        }
    }

    // --- Inserting nodes ------------------------------------------------------------------------

    /// <summary><c>get_appropriate_insertion_location</c>, with foster parenting.</summary>
    private InsertionLocation GetAppropriateInsertionLocation( HtmlNode? overrideTarget = null )
    {
        var target = overrideTarget ?? (_root is null ? _document : CurrentNode);
        if ( !_fosterParentInsertions || !IsHtmlIn( target, GumboTagSets.TableFosterTargets ) )
        {
            return new InsertionLocation( target, -1 );
        }

        var lastTemplate = _openElements.FindLastIndex( node => IsHtml( node, "template" ) );
        var lastTable = _openElements.FindLastIndex( node => IsHtml( node, "table" ) );
        if ( lastTemplate != -1 && (lastTable == -1 || lastTemplate > lastTable) )
        {
            return new InsertionLocation( _openElements[lastTemplate], -1 );
        }

        if ( lastTable == -1 )
        {
            return new InsertionLocation( _openElements[0], -1 );
        }

        var table = _openElements[lastTable];
        if ( table.Parent is { } parent )
        {
            return new InsertionLocation( parent, parent.IndexOfChild( table ) );
        }

        return new InsertionLocation( _openElements[lastTable - 1], -1 );
    }

    /// <summary><c>insert_node</c>.</summary>
    private static void InsertNode( HtmlNode node, InsertionLocation location )
    {
        if ( location.Index == -1 )
        {
            location.Target.AppendParsed( node );
        }
        else
        {
            location.Target.InsertParsed( location.Index, node );
        }
    }

    /// <summary><c>insert_text_token</c>: characters wait in a buffer until something else is inserted.</summary>
    private void InsertText( ParserToken token, string? text = null )
    {
        _textBuffer.Append( text ?? token.Text );
        _textBufferType = token.Kind switch
        {
            ParserTokenKind.Character => TextBufferType.Text,
            ParserTokenKind.CData => TextBufferType.CData,
            _ => _textBufferType,
        };
    }

    /// <summary><c>maybe_flush_text_node_buffer</c>: the text goes where text would be inserted now.</summary>
    private void FlushTextBuffer( )
    {
        if ( _textBuffer.Length == 0 )
        {
            return;
        }

        var text = _textBuffer.ToString( );
        var node = _textBufferType == TextBufferType.CData ? HtmlNode.CreateCData( text ) : HtmlNode.CreateText( text );
        var location = GetAppropriateInsertionLocation( );
        _textBuffer.Clear( );
        _textBufferType = TextBufferType.Whitespace;

        // The DOM doesn't allow text in a document, so the spec drops it
        if ( location.Target != _document )
        {
            InsertNode( node, location );
        }
    }

    /// <summary><c>append_comment_node</c>.</summary>
    private void AppendComment( HtmlNode parent, ParserToken token )
    {
        FlushTextBuffer( );
        parent.AppendParsed( HtmlNode.CreateComment( token.Text ) );
    }

    private void AppendCommentToCurrentNode( ParserToken token ) => AppendComment( CurrentNode, token );

    /// <summary><c>create_element_from_token</c>.</summary>
    private static HtmlNode CreateElement( ParserToken token, HtmlNamespace elementNamespace ) =>
        HtmlNode.CreateElement( elementNamespace, token.Name, [.. token.Attributes.Select( static attribute => attribute.Clone( ) )] );

    /// <summary><c>insert_element</c>: pending text first (unless reconstructing formatting elements).</summary>
    private void InsertElement( HtmlNode element, bool isReconstructingFormattingElements = false )
    {
        if ( !isReconstructingFormattingElements )
        {
            FlushTextBuffer( );
        }

        InsertNode( element, GetAppropriateInsertionLocation( ) );
        _openElements.Add( element );
    }

    private HtmlNode InsertElementFromToken( ParserToken token )
    {
        var element = CreateElement( token, HtmlNamespace.Html );
        InsertElement( element );

        return element;
    }

    private HtmlNode InsertElementOfTagType( string name )
    {
        var element = HtmlNode.CreateElement( HtmlNamespace.Html, name );
        InsertElement( element );

        return element;
    }

    private HtmlNode InsertForeignElement( ParserToken token, HtmlNamespace elementNamespace )
    {
        var element = CreateElement( token, elementNamespace );
        InsertElement( element );

        return element;
    }

    /// <summary><c>run_generic_parsing_algorithm</c>: RCDATA, RAWTEXT or script data until the end tag.</summary>
    private void RunGenericParsingAlgorithm( ParserToken token, HtmlParseMode state )
    {
        InsertElementFromToken( token );
        _tokens.State = state;
        _originalInsertionMode = _insertionMode;
        _insertionMode = InsertionMode.Text;
    }

    /// <summary><c>merge_attributes</c>: the token's attributes the node lacks.</summary>
    private static void MergeAttributes( ParserToken token, HtmlNode node )
    {
        foreach ( var attribute in token.Attributes )
        {
            if ( !node.HasAttribute( attribute.QualifiedName ) )
            {
                node.Attributes.Add( attribute );
            }
        }
    }

    // --- Foreign elements -----------------------------------------------------------------------

    /// <summary><c>adjust_svg_tag</c>.</summary>
    private static void AdjustSvgTag( ParserToken token )
    {
        if ( GumboTagSets.SvgTagNames.TryGetValue( token.Name, out var adjusted ) )
        {
            token.Name = adjusted;
        }
    }

    /// <summary><c>adjust_svg_attributes</c>.</summary>
    private static void AdjustSvgAttributes( ParserToken token ) => AdjustAttributes( token, name =>
        GumboTagSets.SvgAttributeNames.TryGetValue( name, out var adjusted ) ? (null, adjusted) : null );

    /// <summary><c>adjust_mathml_attributes</c>.</summary>
    private static void AdjustMathMlAttributes( ParserToken token ) => AdjustAttributes( token, name =>
        name == "definitionurl" ? (null, "definitionURL") : null );

    /// <summary><c>adjust_foreign_attributes</c>: <c>xlink:</c>, <c>xml:</c> and <c>xmlns</c> attributes get their namespace.</summary>
    private static void AdjustForeignAttributes( ParserToken token ) => AdjustAttributes( token, name =>
        GumboTagSets.ForeignAttributes.TryGetValue( name, out var adjusted ) ? adjusted : null );

    private static void AdjustAttributes( ParserToken token, Func<string, (string? Prefix, string LocalName)?> adjust )
    {
        for ( var index = 0; index < token.Attributes.Count; index++ )
        {
            var attribute = token.Attributes[index];
            if ( attribute.Prefix is null && adjust( attribute.LocalName ) is var (prefix, localName) )
            {
                token.Attributes[index] = new HtmlAttribute( prefix, localName, attribute.Value );
            }
        }
    }
}
