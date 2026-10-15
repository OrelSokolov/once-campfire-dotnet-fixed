using System.Collections.Frozen;
using AngleSharp.Html.Parser;

namespace Campfire.Application.RichText.Html.Parsing;

/// <summary>
/// The remaining insertion modes: head, text, select, template, and the modes around the body and
/// frameset, which a fragment reaches only from an <c>html</c> or <c>frameset</c> context.
/// </summary>
internal sealed partial class GumboTreeBuilder
{
    private static readonly FrozenSet<string> HeadVoidElements = GumboTagSets.Set( "base", "basefont", "bgsound", "link", "meta" );

    private static readonly FrozenSet<string> HeadRawTextElements = GumboTagSets.Set( "noframes", "style" );

    private static readonly FrozenSet<string> AfterHeadHeadElements = GumboTagSets.Set(
        "base", "basefont", "bgsound", "link", "meta", "noframes", "script", "style", "template", "title" );

    private static readonly FrozenSet<string> SelectClosingStartTags = GumboTagSets.Set( "input", "keygen", "textarea" );

    private static readonly FrozenSet<string> SelectInTableTags = GumboTagSets.Set( "caption", "table", "tbody", "tfoot", "thead", "tr", "td", "th" );

    private static readonly FrozenSet<string> TemplateTableSections = GumboTagSets.Set( "caption", "colgroup", "tbody", "tfoot", "thead" );

    private void HandleOutsideBody( ParserToken token )
    {
        switch ( _insertionMode )
        {
            case InsertionMode.BeforeHead:
                HandleBeforeHead( token );
                break;
            case InsertionMode.InHead:
                HandleInHead( token );
                break;
            case InsertionMode.InHeadNoscript:
                HandleInHeadNoscript( token );
                break;
            case InsertionMode.AfterHead:
                HandleAfterHead( token );
                break;
            case InsertionMode.AfterBody or InsertionMode.AfterAfterBody:
                HandleAfterBody( token );
                break;
            case InsertionMode.InFrameset or InsertionMode.AfterFrameset or InsertionMode.AfterAfterFrameset:
                HandleFrameset( token );
                break;
            default:
                // Initial and before html: the fragment parser starts with the html element open
                throw new InvalidOperationException( $"The fragment parser can't be in the {_insertionMode} insertion mode." );
        }
    }

    /// <summary><c>handle_before_head</c>.</summary>
    private void HandleBeforeHead( ParserToken token )
    {
        if ( token.Kind is ParserTokenKind.Whitespace or ParserTokenKind.Doctype )
        {
            return;
        }

        if ( token.Kind == ParserTokenKind.Comment )
        {
            AppendCommentToCurrentNode( token );
        }
        else if ( token.IsStartTag( "html" ) )
        {
            HandleInBody( token );
        }
        else if ( token.IsStartTag( "head" ) )
        {
            _headElement = InsertElementFromToken( token );
            _insertionMode = InsertionMode.InHead;
        }
        else if ( token.Kind != ParserTokenKind.EndTag || token.Name is "head" or "body" or "html" or "br" )
        {
            _headElement = InsertElementOfTagType( "head" );
            ReprocessIn( InsertionMode.InHead );
        }
    }

    /// <summary><c>handle_in_head</c>, also used by other modes for the elements that belong in a head.</summary>
    private void HandleInHead( ParserToken token )
    {
        switch ( token.Kind )
        {
            case ParserTokenKind.Whitespace:
                InsertText( token );
                return;
            case ParserTokenKind.Comment:
                AppendCommentToCurrentNode( token );
                return;
            case ParserTokenKind.Doctype:
                return;
            case ParserTokenKind.StartTag when InHeadStartTag( token ):
            case ParserTokenKind.EndTag when InHeadEndTag( token ):
                return;
        }

        PopCurrentNode( );
        ReprocessIn( InsertionMode.AfterHead );
    }

    private bool InHeadStartTag( ParserToken token )
    {
        if ( token.Name == "html" )
        {
            HandleInBody( token );
        }
        else if ( HeadVoidElements.Contains( token.Name ) )
        {
            InsertElementFromToken( token );
            PopCurrentNode( );
        }
        else if ( token.Name == "title" )
        {
            RunGenericParsingAlgorithm( token, HtmlParseMode.RCData );
        }
        else if ( HeadRawTextElements.Contains( token.Name ) )
        {
            RunGenericParsingAlgorithm( token, HtmlParseMode.Rawtext );
        }
        else if ( token.Name == "noscript" )
        {
            InsertElementFromToken( token );
            _insertionMode = InsertionMode.InHeadNoscript;
        }
        else if ( token.Name == "script" )
        {
            RunGenericParsingAlgorithm( token, HtmlParseMode.Script );
        }
        else if ( token.Name == "template" )
        {
            InsertElementFromToken( token );
            AddFormattingMarker( );
            _framesetOk = false;
            _insertionMode = InsertionMode.InTemplate;
            PushTemplateInsertionMode( InsertionMode.InTemplate );
        }
        else
        {
            // <head> is ignored; anything else closes the head
            return token.Name == "head";
        }

        return true;
    }

    private bool InHeadEndTag( ParserToken token )
    {
        switch ( token.Name )
        {
            case "head":
                PopCurrentNode( );
                _insertionMode = InsertionMode.AfterHead;
                return true;
            case "body" or "html" or "br":
                return false;
            case "template":
                InHeadEndTemplate( );
                return true;
            default:
                return true;
        }
    }

    private void InHeadEndTemplate( )
    {
        if ( !HasOpenElement( "template" ) )
        {
            return;
        }

        GenerateAllImpliedEndTagsThoroughly( );
        PopUntilHtml( "template" );
        ClearActiveFormattingElements( );
        PopTemplateInsertionMode( );
        ResetInsertionModeAppropriately( );
    }

    /// <summary><c>handle_in_head_noscript</c>.</summary>
    private void HandleInHeadNoscript( ParserToken token )
    {
        if ( token.Kind == ParserTokenKind.Doctype )
        {
            return;
        }

        if ( token.IsStartTag( "html" ) )
        {
            HandleInBody( token );
        }
        else if ( token.IsEndTag( "noscript" ) )
        {
            PopCurrentNode( );
            _insertionMode = InsertionMode.InHead;
        }
        else if ( token.Kind is ParserTokenKind.Whitespace or ParserTokenKind.Comment
            || token is { Kind: ParserTokenKind.StartTag, Name: "basefont" or "bgsound" or "link" or "meta" or "noframes" or "style" } )
        {
            HandleInHead( token );
        }
        else if ( !(token is { Kind: ParserTokenKind.StartTag, Name: "head" or "noscript" })
            && !(token.Kind == ParserTokenKind.EndTag && token.Name != "br") )
        {
            PopCurrentNode( );
            ReprocessIn( InsertionMode.InHead );
        }
    }

    /// <summary><c>handle_after_head</c>.</summary>
    private void HandleAfterHead( ParserToken token )
    {
        switch ( token.Kind )
        {
            case ParserTokenKind.Whitespace:
                InsertText( token );
                return;
            case ParserTokenKind.Comment:
                AppendCommentToCurrentNode( token );
                return;
            case ParserTokenKind.Doctype:
                return;
            case ParserTokenKind.StartTag when AfterHeadStartTag( token ):
                return;
            case ParserTokenKind.EndTag when token.Name == "template":
                HandleInHead( token );
                return;
            case ParserTokenKind.EndTag when token.Name is not ("body" or "html" or "br"):
                return;
        }

        InsertElementOfTagType( "body" );
        ReprocessIn( InsertionMode.InBody );
    }

    private bool AfterHeadStartTag( ParserToken token )
    {
        if ( token.Name == "html" )
        {
            HandleInBody( token );
        }
        else if ( token.Name == "body" )
        {
            InsertElementFromToken( token );
            _framesetOk = false;
            _insertionMode = InsertionMode.InBody;
        }
        else if ( token.Name == "frameset" )
        {
            InsertElementFromToken( token );
            _insertionMode = InsertionMode.InFrameset;
        }
        else if ( AfterHeadHeadElements.Contains( token.Name ) )
        {
            FlushTextBuffer( );
            _openElements.Add( _headElement! );
            HandleInHead( token );
            _openElements.Remove( _headElement! );
        }
        else
        {
            return token.Name == "head";
        }

        return true;
    }

    /// <summary><c>handle_text</c>: the raw text of <c>title</c>, <c>textarea</c>, <c>style</c>, <c>script</c> and the like.</summary>
    private void HandleText( ParserToken token )
    {
        if ( token.Kind is ParserTokenKind.Character or ParserTokenKind.Whitespace )
        {
            InsertText( token );
            return;
        }

        _reprocessCurrentToken = token.Kind == ParserTokenKind.EndOfFile;
        PopCurrentNode( );
        _insertionMode = _originalInsertionMode;
    }

    /// <summary><c>handle_in_select</c>: the select parsing rules before customizable selects.</summary>
    private void HandleInSelect( ParserToken token )
    {
        switch ( token.Kind )
        {
            case ParserTokenKind.Character or ParserTokenKind.Whitespace:
                InsertText( token );
                return;
            case ParserTokenKind.Comment:
                AppendCommentToCurrentNode( token );
                return;
            case ParserTokenKind.EndOfFile:
                HandleInBody( token );
                return;
            case ParserTokenKind.StartTag:
                InSelectStartTag( token );
                return;
            case ParserTokenKind.EndTag:
                InSelectEndTag( token );
                return;
        }

        // NULL and DOCTYPE tokens are ignored
    }

    private void InSelectStartTag( ParserToken token )
    {
        switch ( token.Name )
        {
            case "html":
                HandleInBody( token );
                break;
            case "option" or "optgroup" or "hr":
                PopIfCurrent( "option" );
                if ( token.Name != "option" )
                {
                    PopIfCurrent( "optgroup" );
                }

                InsertElementFromToken( token );
                if ( token.Name == "hr" )
                {
                    PopCurrentNode( );
                }

                break;
            case "select":
                if ( HasElementInSelectScope( "select" ) )
                {
                    CloseCurrentSelect( );
                }

                break;
            case "script" or "template":
                HandleInHead( token );
                break;
            default:
                if ( SelectClosingStartTags.Contains( token.Name ) && HasElementInSelectScope( "select" ) )
                {
                    CloseCurrentSelect( );
                    _reprocessCurrentToken = true;
                }

                break;
        }
    }

    private void InSelectEndTag( ParserToken token )
    {
        switch ( token.Name )
        {
            case "optgroup":
                if ( IsHtml( CurrentNode, "option" ) && _openElements.Count >= 2 && IsHtml( _openElements[^2], "optgroup" ) )
                {
                    PopCurrentNode( );
                }

                PopIfCurrent( "optgroup" );
                break;
            case "option":
                PopIfCurrent( "option" );
                break;
            case "select":
                if ( HasElementInSelectScope( "select" ) )
                {
                    CloseCurrentSelect( );
                }

                break;
            case "template":
                HandleInHead( token );
                break;
        }
    }

    private void PopIfCurrent( string name )
    {
        if ( IsHtml( CurrentNode, name ) )
        {
            PopCurrentNode( );
        }
    }

    /// <summary><c>close_current_select</c>.</summary>
    private void CloseCurrentSelect( )
    {
        PopUntilHtml( "select" );
        ResetInsertionModeAppropriately( );
    }

    /// <summary><c>handle_in_select_in_table</c>.</summary>
    private void HandleInSelectInTable( ParserToken token )
    {
        if ( token.IsStartTagIn( SelectInTableTags ) )
        {
            CloseCurrentSelect( );
            _reprocessCurrentToken = true;
        }
        else if ( token.IsEndTagIn( SelectInTableTags ) )
        {
            if ( HasElementInTableScope( token.Name ) )
            {
                CloseCurrentSelect( );
                _reprocessCurrentToken = true;
            }
        }
        else
        {
            HandleInSelect( token );
        }
    }

    /// <summary><c>handle_in_template</c>.</summary>
    private void HandleInTemplate( ParserToken token )
    {
        if ( token.IsCharacterData || token.Kind is ParserTokenKind.Comment or ParserTokenKind.Doctype )
        {
            HandleInBody( token );
        }
        else if ( token.IsStartTagIn( AfterHeadHeadElements ) || token.IsEndTag( "template" ) )
        {
            HandleInHead( token );
        }
        else if ( token.Kind == ParserTokenKind.StartTag )
        {
            ReplaceTemplateInsertionMode( TemplateModeFor( token.Name ) );
        }
        else if ( token.Kind == ParserTokenKind.EndOfFile && HasOpenElement( "template" ) )
        {
            PopUntilHtml( "template" );
            ClearActiveFormattingElements( );
            PopTemplateInsertionMode( );
            ResetInsertionModeAppropriately( );
            _reprocessCurrentToken = true;
        }

        // Other end tags are ignored; end of file with no template open stops parsing
    }

    private static InsertionMode TemplateModeFor( string startTag ) => startTag switch
    {
        "caption" or "colgroup" or "tbody" or "tfoot" or "thead" => InsertionMode.InTable,
        "col" => InsertionMode.InColumnGroup,
        "tr" => InsertionMode.InTableBody,
        "td" or "th" => InsertionMode.InRow,
        _ => InsertionMode.InBody,
    };

    /// <summary><c>handle_after_body</c> and <c>handle_after_after_body</c>.</summary>
    private void HandleAfterBody( ParserToken token )
    {
        var isAfterAfterBody = _insertionMode == InsertionMode.AfterAfterBody;
        if ( token.Kind == ParserTokenKind.Whitespace || token.IsStartTag( "html" ) || (isAfterAfterBody && token.Kind == ParserTokenKind.Doctype) )
        {
            HandleInBody( token );
        }
        else if ( token.Kind == ParserTokenKind.Comment )
        {
            AppendComment( isAfterAfterBody ? _document : _root!, token );
        }
        else if ( token.IsEndTag( "html" ) && !isAfterAfterBody )
        {
            // The fragment case ignores </html>
        }
        else if ( token.Kind is not (ParserTokenKind.Doctype or ParserTokenKind.EndOfFile) )
        {
            ReprocessIn( InsertionMode.InBody );
        }
    }

    /// <summary><c>handle_in_frameset</c>, <c>handle_after_frameset</c> and <c>handle_after_after_frameset</c>.</summary>
    private void HandleFrameset( ParserToken token )
    {
        var isAfterAfterFrameset = _insertionMode == InsertionMode.AfterAfterFrameset;
        if ( token.Kind == ParserTokenKind.Whitespace && !isAfterAfterFrameset )
        {
            InsertText( token );
        }
        else if ( token.Kind == ParserTokenKind.Comment )
        {
            AppendComment( isAfterAfterFrameset ? _document : CurrentNode, token );
        }
        else if ( token.IsStartTag( "html" ) || (isAfterAfterFrameset && token.Kind is ParserTokenKind.Whitespace or ParserTokenKind.Doctype) )
        {
            HandleInBody( token );
        }
        else if ( token.IsStartTag( "noframes" ) )
        {
            HandleInHead( token );
        }
        else if ( _insertionMode == InsertionMode.InFrameset )
        {
            InFramesetTag( token );
        }
        else if ( token.IsEndTag( "html" ) && _insertionMode == InsertionMode.AfterFrameset )
        {
            _insertionMode = InsertionMode.AfterAfterFrameset;
        }
    }

    private void InFramesetTag( ParserToken token )
    {
        if ( token.IsStartTag( "frameset" ) )
        {
            InsertElementFromToken( token );
        }
        else if ( token.IsEndTag( "frameset" ) && !IsHtml( CurrentNode, "html" ) )
        {
            // The fragment case stays in frameset mode
            PopCurrentNode( );
        }
        else if ( token.IsStartTag( "frame" ) )
        {
            InsertElementFromToken( token );
            PopCurrentNode( );
        }
    }
}
