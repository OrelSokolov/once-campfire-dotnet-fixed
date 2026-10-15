using System.Collections.Frozen;
using AngleSharp.Html.Parser;

namespace Campfire.Application.RichText.Html.Parsing;

/// <summary><c>handle_in_body</c>: its start and end tag branches, by tag name.</summary>
internal sealed partial class GumboTreeBuilder
{
    private delegate void TagHandler( GumboTreeBuilder builder, ParserToken token );

    private static readonly FrozenDictionary<string, TagHandler> InBodyStartTags = BuildInBodyStartTags( );

    private static readonly FrozenDictionary<string, TagHandler> InBodyEndTags = BuildInBodyEndTags( );

    private void HandleInBody( ParserToken token )
    {
        switch ( token.Kind )
        {
            case ParserTokenKind.Whitespace:
                ReconstructActiveFormattingElements( );
                InsertText( token );
                break;
            case ParserTokenKind.Character or ParserTokenKind.CData:
                ReconstructActiveFormattingElements( );
                InsertText( token );
                _framesetOk = false;
                break;
            case ParserTokenKind.Comment:
                AppendCommentToCurrentNode( token );
                break;
            case ParserTokenKind.EndOfFile:
                if ( CurrentTemplateInsertionMode != InsertionMode.Initial )
                {
                    HandleInTemplate( token );
                }

                break;
            case ParserTokenKind.StartTag:
                InBodyStartTag( token );
                break;
            case ParserTokenKind.EndTag:
                InBodyEndTag( token );
                break;
        }

        // NULL and DOCTYPE tokens are ignored
    }

    private void InBodyStartTag( ParserToken token )
    {
        if ( InBodyStartTags.TryGetValue( token.Name, out var handler ) )
        {
            handler( this, token );
            return;
        }

        ReconstructActiveFormattingElements( );
        InsertElementFromToken( token );
    }

    private void InBodyEndTag( ParserToken token )
    {
        if ( InBodyEndTags.TryGetValue( token.Name, out var handler ) )
        {
            handler( this, token );
            return;
        }

        InBodyAnyOtherEndTag( token );
    }

    private static FrozenDictionary<string, TagHandler> BuildInBodyStartTags( )
    {
        var handlers = new Dictionary<string, TagHandler>( StringComparer.Ordinal )
        {
            ["html"] = static ( builder, token ) => builder.InBodyHtml( token ),
            ["body"] = static ( builder, token ) => builder.InBodyBody( token ),
            ["frameset"] = static ( builder, token ) => builder.InBodyFrameset( token ),
            ["pre"] = static ( builder, token ) => builder.InBodyPre( token ),
            ["listing"] = static ( builder, token ) => builder.InBodyPre( token ),
            ["form"] = static ( builder, token ) => builder.InBodyForm( token ),
            ["li"] = static ( builder, token ) => builder.InBodyListItem( token, isLi: true ),
            ["dd"] = static ( builder, token ) => builder.InBodyListItem( token, isLi: false ),
            ["dt"] = static ( builder, token ) => builder.InBodyListItem( token, isLi: false ),
            ["plaintext"] = static ( builder, token ) => builder.InBodyPlaintext( token ),
            ["button"] = static ( builder, token ) => builder.InBodyButton( token ),
            ["a"] = static ( builder, token ) => builder.InBodyAnchor( token ),
            ["nobr"] = static ( builder, token ) => builder.InBodyNobr( token ),
            ["table"] = static ( builder, token ) => builder.InBodyTable( token ),
            ["input"] = static ( builder, token ) => builder.InBodyInput( token ),
            ["hr"] = static ( builder, token ) => builder.InBodyHr( token ),
            ["textarea"] = static ( builder, token ) => builder.InBodyTextarea( token ),
            ["xmp"] = static ( builder, token ) => builder.InBodyXmp( token ),
            ["iframe"] = static ( builder, token ) => builder.InBodyIframe( token ),
            ["noembed"] = static ( builder, token ) => builder.RunGenericParsingAlgorithm( token, HtmlParseMode.Rawtext ),
            ["select"] = static ( builder, token ) => builder.InBodySelect( token ),
            ["math"] = static ( builder, token ) => builder.InBodyForeign( token, HtmlNamespace.MathMl ),
            ["svg"] = static ( builder, token ) => builder.InBodyForeign( token, HtmlNamespace.Svg ),
        };

        Add(
            handlers,
            static ( builder, token ) => builder.HandleInHead( token ),
            "base", "basefont", "bgsound", "link", "meta", "noframes", "script", "style", "template", "title" );
        Add( handlers, static ( builder, token ) => builder.InBodyBlock( token ),
            "address", "article", "aside", "blockquote", "center", "details", "dialog", "dir", "div", "dl", "fieldset", "figcaption",
            "figure", "footer", "header", "hgroup", "main", "menu", "nav", "ol", "p", "section", "summary", "ul", "search" );
        Add( handlers, static ( builder, token ) => builder.InBodyHeading( token ), "h1", "h2", "h3", "h4", "h5", "h6" );
        Add(
            handlers,
            static ( builder, token ) => builder.InBodyFormatting( token ),
            "b", "big", "code", "em", "font", "i", "s", "small", "strike", "strong", "tt", "u" );
        Add( handlers, static ( builder, token ) => builder.InBodyApplet( token ), "applet", "marquee", "object" );
        Add( handlers, static ( builder, token ) => builder.InBodyVoid( token ), "area", "br", "embed", "img", "image", "keygen", "wbr" );
        Add( handlers, static ( builder, token ) => builder.InBodyParam( token ), "param", "source", "track" );
        Add( handlers, static ( builder, token ) => builder.InBodyOption( token ), "optgroup", "option" );
        Add( handlers, static ( builder, token ) => builder.InBodyRb( token ), "rb", "rtc" );
        Add( handlers, static ( builder, token ) => builder.InBodyRp( token ), "rp", "rt" );
        Add( handlers, static ( _, _ ) => { }, "caption", "col", "colgroup", "frame", "head", "tbody", "td", "tfoot", "th", "thead", "tr" );

        return handlers.ToFrozenDictionary( StringComparer.Ordinal );
    }

    private static FrozenDictionary<string, TagHandler> BuildInBodyEndTags( )
    {
        var handlers = new Dictionary<string, TagHandler>( StringComparer.Ordinal )
        {
            ["template"] = static ( builder, token ) => builder.HandleInHead( token ),
            ["body"] = static ( builder, _ ) => builder.InBodyEndBody( reprocess: false ),
            ["html"] = static ( builder, _ ) => builder.InBodyEndBody( reprocess: true ),
            ["form"] = static ( builder, _ ) => builder.InBodyEndForm( ),
            ["p"] = static ( builder, _ ) => builder.InBodyEndP( ),
            ["li"] = static ( builder, _ ) => builder.InBodyEndLi( ),
            ["dd"] = static ( builder, token ) => builder.InBodyEndInScope( token ),
            ["dt"] = static ( builder, token ) => builder.InBodyEndInScope( token ),
            ["br"] = static ( builder, _ ) => builder.InBodyEndBr( ),
        };

        Add( handlers, static ( builder, token ) => builder.InBodyEndInScope( token ),
            "address", "article", "aside", "blockquote", "button", "center", "details", "dialog", "dir", "div", "dl", "fieldset",
            "figcaption", "figure", "footer", "header", "hgroup", "listing", "main", "menu", "nav", "ol", "pre", "section", "summary",
            "ul", "search" );
        Add( handlers, static ( builder, _ ) => builder.InBodyEndHeading( ), "h1", "h2", "h3", "h4", "h5", "h6" );
        Add( handlers, static ( builder, token ) => builder.AdoptionAgencyAlgorithm( token ),
            "a", "b", "big", "code", "em", "font", "i", "nobr", "s", "small", "strike", "strong", "tt", "u" );
        Add( handlers, static ( builder, token ) => builder.InBodyEndApplet( token ), "applet", "marquee", "object" );

        return handlers.ToFrozenDictionary( StringComparer.Ordinal );
    }

    private static void Add( Dictionary<string, TagHandler> handlers, TagHandler handler, params string[] names )
    {
        foreach ( var name in names )
        {
            handlers.Add( name, handler );
        }
    }

    // --- Start tags -----------------------------------------------------------------------------

    private void InBodyHtml( ParserToken token )
    {
        if ( !HasOpenElement( "template" ) )
        {
            MergeAttributes( token, _root! );
        }
    }

    private void InBodyBody( ParserToken token )
    {
        if ( _openElements.Count < 2 || !IsHtml( _openElements[1], "body" ) || HasOpenElement( "template" ) )
        {
            return;
        }

        _framesetOk = false;
        MergeAttributes( token, _openElements[1] );
    }

    private void InBodyFrameset( ParserToken token )
    {
        if ( _openElements.Count < 2 || !IsHtml( _openElements[1], "body" ) || !_framesetOk )
        {
            return;
        }

        // The body is on the stack, so popping through it stops there
        var body = _openElements[1];
        PopThrough( body );
        ClearActiveFormattingElements( );
        body.Detach( );
        InsertElementFromToken( token );
        _insertionMode = InsertionMode.InFrameset;
    }

    private void InBodyBlock( ParserToken token )
    {
        MaybeImplicitlyClosePTag( );
        InsertElementFromToken( token );
    }

    private void InBodyHeading( ParserToken token )
    {
        MaybeImplicitlyClosePTag( );
        if ( IsHtmlIn( CurrentNode, GumboTagSets.Headings ) )
        {
            PopCurrentNode( );
        }

        InsertElementFromToken( token );
    }

    private void InBodyPre( ParserToken token )
    {
        MaybeImplicitlyClosePTag( );
        InsertElementFromToken( token );
        _ignoreNextLinefeed = true;
        _framesetOk = false;
    }

    private void InBodyForm( ParserToken token )
    {
        if ( _formElement is not null && !HasOpenElement( "template" ) )
        {
            return;
        }

        MaybeImplicitlyClosePTag( );
        var form = InsertElementFromToken( token );
        if ( !HasOpenElement( "template" ) )
        {
            _formElement = form;
        }
    }

    private void InBodyListItem( ParserToken token, bool isLi )
    {
        MaybeImplicitlyCloseListTag( isLi );
        MaybeImplicitlyClosePTag( );
        InsertElementFromToken( token );
    }

    private void InBodyPlaintext( ParserToken token )
    {
        MaybeImplicitlyClosePTag( );
        InsertElementFromToken( token );
        _tokens.State = HtmlParseMode.Plaintext;
    }

    private void InBodyButton( ParserToken token )
    {
        if ( HasElementInScope( "button" ) )
        {
            GenerateImpliedEndTags( );
            PopUntilHtml( "button" );
        }

        ReconstructActiveFormattingElements( );
        InsertElementFromToken( token );
        _framesetOk = false;
    }

    private void InBodyAnchor( ParserToken token )
    {
        if ( FindLastAnchorIndex( ) != -1 )
        {
            AdoptionAgencyAlgorithm( token );
            var lastAnchor = FindLastAnchorIndex( );
            if ( lastAnchor != -1 )
            {
                var element = _activeFormattingElements[lastAnchor]!;
                _activeFormattingElements.RemoveAt( lastAnchor );
                _openElements.Remove( element );
            }
        }

        ReconstructActiveFormattingElements( );
        AddFormattingElement( InsertElementFromToken( token ) );
    }

    private void InBodyFormatting( ParserToken token )
    {
        ReconstructActiveFormattingElements( );
        AddFormattingElement( InsertElementFromToken( token ) );
    }

    private void InBodyNobr( ParserToken token )
    {
        ReconstructActiveFormattingElements( );
        if ( HasElementInScope( "nobr" ) )
        {
            AdoptionAgencyAlgorithm( token );
            ReconstructActiveFormattingElements( );
        }

        InsertElementFromToken( token );
        AddFormattingElement( CurrentNode );
    }

    private void InBodyApplet( ParserToken token )
    {
        ReconstructActiveFormattingElements( );
        InsertElementFromToken( token );
        AddFormattingMarker( );
        _framesetOk = false;
    }

    private void InBodyTable( ParserToken token )
    {
        // Rich text fragments are never in quirks mode, where a table would not close a paragraph
        MaybeImplicitlyClosePTag( );
        InsertElementFromToken( token );
        _framesetOk = false;
        _insertionMode = InsertionMode.InTable;
    }

    private void InBodyVoid( ParserToken token )
    {
        if ( token.Name == "image" )
        {
            token.Name = "img";
        }

        ReconstructActiveFormattingElements( );
        InsertElementFromToken( token );
        PopCurrentNode( );
        _framesetOk = false;
    }

    private void InBodyInput( ParserToken token )
    {
        ReconstructActiveFormattingElements( );
        var input = InsertElementFromToken( token );
        PopCurrentNode( );
        if ( !AttributeMatches( input, "type", "hidden" ) )
        {
            _framesetOk = false;
        }
    }

    private void InBodyParam( ParserToken token )
    {
        InsertElementFromToken( token );
        PopCurrentNode( );
    }

    private void InBodyHr( ParserToken token )
    {
        MaybeImplicitlyClosePTag( );
        InsertElementFromToken( token );
        PopCurrentNode( );
        _framesetOk = false;
    }

    private void InBodyTextarea( ParserToken token )
    {
        RunGenericParsingAlgorithm( token, HtmlParseMode.RCData );
        _ignoreNextLinefeed = true;
        _framesetOk = false;
    }

    private void InBodyXmp( ParserToken token )
    {
        MaybeImplicitlyClosePTag( );
        ReconstructActiveFormattingElements( );
        _framesetOk = false;
        RunGenericParsingAlgorithm( token, HtmlParseMode.Rawtext );
    }

    private void InBodyIframe( ParserToken token )
    {
        _framesetOk = false;
        RunGenericParsingAlgorithm( token, HtmlParseMode.Rawtext );
    }

    private void InBodySelect( ParserToken token )
    {
        ReconstructActiveFormattingElements( );
        InsertElementFromToken( token );
        _framesetOk = false;
        _insertionMode = _insertionMode is InsertionMode.InTable or InsertionMode.InCaption or InsertionMode.InTableBody
            or InsertionMode.InRow or InsertionMode.InCell
            ? InsertionMode.InSelectInTable
            : InsertionMode.InSelect;
    }

    private void InBodyOption( ParserToken token )
    {
        if ( IsHtml( CurrentNode, "option" ) )
        {
            PopCurrentNode( );
        }

        ReconstructActiveFormattingElements( );
        InsertElementFromToken( token );
    }

    private void InBodyRb( ParserToken token )
    {
        if ( HasElementInScope( "ruby" ) )
        {
            GenerateImpliedEndTags( );
        }

        InsertElementFromToken( token );
    }

    private void InBodyRp( ParserToken token )
    {
        if ( HasElementInScope( "ruby" ) )
        {
            GenerateImpliedEndTags( "rtc" );
        }

        InsertElementFromToken( token );
    }

    private void InBodyForeign( ParserToken token, HtmlNamespace elementNamespace )
    {
        ReconstructActiveFormattingElements( );
        if ( elementNamespace == HtmlNamespace.MathMl )
        {
            AdjustMathMlAttributes( token );
        }
        else
        {
            AdjustSvgAttributes( token );
        }

        AdjustForeignAttributes( token );
        InsertForeignElement( token, elementNamespace );
        if ( token.IsSelfClosing )
        {
            PopCurrentNode( );
        }
    }

    // --- End tags -------------------------------------------------------------------------------

    private void InBodyEndBody( bool reprocess )
    {
        if ( !HasElementInScope( "body" ) )
        {
            return;
        }

        _insertionMode = InsertionMode.AfterBody;
        _reprocessCurrentToken = reprocess;
    }

    /// <summary>End tags that close a matching element in scope, with implied end tags.</summary>
    private void InBodyEndInScope( ParserToken token )
    {
        if ( HasElementInScope( token.Name ) )
        {
            ImplicitlyCloseTags( token.Name );
        }
    }

    private void InBodyEndForm( )
    {
        if ( HasOpenElement( "template" ) )
        {
            if ( HasElementInScope( "form" ) )
            {
                GenerateImpliedEndTags( );
                PopUntilHtml( "form" );
            }

            return;
        }

        var node = _formElement;
        _formElement = null;
        if ( node is null || !HasNodeInScope( node ) )
        {
            return;
        }

        // Only the form is removed from the stack; the pending text belongs inside it
        FlushTextBuffer( );
        GenerateImpliedEndTags( );
        _openElements.Remove( node );
    }

    private void InBodyEndP( )
    {
        if ( !HasElementInButtonScope( "p" ) )
        {
            InsertElementOfTagType( "p" );
        }

        ImplicitlyCloseTags( "p" );
    }

    private void InBodyEndLi( )
    {
        if ( HasElementInListScope( "li" ) )
        {
            ImplicitlyCloseTags( "li" );
        }
    }

    private void InBodyEndHeading( )
    {
        if ( !HasElementInScope( GumboTagSets.Headings ) )
        {
            return;
        }

        GenerateImpliedEndTags( );
        PopUntil( node => IsHtmlIn( node, GumboTagSets.Headings ) );
    }

    private void InBodyEndApplet( ParserToken token )
    {
        if ( HasElementInScope( token.Name ) )
        {
            ImplicitlyCloseTags( token.Name );
            ClearActiveFormattingElements( );
        }
    }

    private void InBodyEndBr( )
    {
        ReconstructActiveFormattingElements( );
        InsertElementOfTagType( "br" );
        PopCurrentNode( );
        _framesetOk = false;
    }
}
