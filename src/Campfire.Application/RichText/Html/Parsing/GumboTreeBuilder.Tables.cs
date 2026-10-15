using System.Collections.Frozen;

namespace Campfire.Application.RichText.Html.Parsing;

/// <summary>The table insertion modes: <c>handle_in_table</c> through <c>handle_in_cell</c>.</summary>
internal sealed partial class GumboTreeBuilder
{
    private static readonly FrozenSet<string> TableTextParents = GumboTagSets.Set( "table", "tbody", "template", "tfoot", "thead", "tr" );

    private static readonly FrozenSet<string> TableSections = GumboTagSets.Set( "tbody", "tfoot", "thead" );

    private static readonly FrozenSet<string> TableIgnoredEndTags = GumboTagSets.Set(
        "body", "caption", "col", "colgroup", "html", "tbody", "td", "tfoot", "th", "thead", "tr" );

    private static readonly FrozenSet<string> CaptionClosingStartTags = GumboTagSets.Set(
        "caption", "col", "colgroup", "tbody", "td", "tfoot", "th", "thead", "tr" );

    private static readonly FrozenSet<string> CaptionIgnoredEndTags = GumboTagSets.Set(
        "body", "col", "colgroup", "html", "tbody", "td", "tfoot", "th", "thead", "tr" );

    private static readonly FrozenSet<string> TableBodyClosingStartTags = GumboTagSets.Set( "caption", "col", "colgroup", "tbody", "tfoot", "thead" );

    private static readonly FrozenSet<string> TableBodyIgnoredEndTags = GumboTagSets.Set( "body", "caption", "col", "colgroup", "html", "td", "th", "tr" );

    private static readonly FrozenSet<string> RowClosingStartTags = GumboTagSets.Set( "caption", "col", "colgroup", "tbody", "tfoot", "thead", "tr" );

    private static readonly FrozenSet<string> RowIgnoredEndTags = GumboTagSets.Set( "body", "caption", "col", "colgroup", "html", "td", "th" );

    private static readonly FrozenSet<string> CellIgnoredEndTags = GumboTagSets.Set( "body", "caption", "col", "colgroup", "html" );

    private static readonly FrozenSet<string> CellClosingEndTags = GumboTagSets.Set( "table", "tbody", "tfoot", "thead", "tr" );

    private void HandleInTableModes( ParserToken token )
    {
        switch ( _insertionMode )
        {
            case InsertionMode.InTable:
                HandleInTable( token );
                break;
            case InsertionMode.InTableText:
                HandleInTableText( token );
                break;
            case InsertionMode.InCaption:
                HandleInCaption( token );
                break;
            case InsertionMode.InColumnGroup:
                HandleInColumnGroup( token );
                break;
            case InsertionMode.InTableBody:
                HandleInTableBody( token );
                break;
            case InsertionMode.InRow:
                HandleInRow( token );
                break;
            default:
                HandleInCell( token );
                break;
        }
    }

    /// <summary><c>handle_in_table</c>.</summary>
    private void HandleInTable( ParserToken token )
    {
        if ( token.Kind is ParserTokenKind.Character or ParserTokenKind.Whitespace or ParserTokenKind.Null && IsHtmlIn( CurrentNode, TableTextParents ) )
        {
            _originalInsertionMode = _insertionMode;
            _reprocessCurrentToken = true;
            _insertionMode = InsertionMode.InTableText;
            return;
        }

        switch ( token.Kind )
        {
            case ParserTokenKind.Comment:
                AppendCommentToCurrentNode( token );
                return;
            case ParserTokenKind.Doctype:
                return;
            case ParserTokenKind.StartTag when InTableStartTag( token ):
            case ParserTokenKind.EndTag when InTableEndTag( token ):
                return;
            case ParserTokenKind.EndOfFile:
                HandleInBody( token );
                return;
        }

        // Foster parenting
        _fosterParentInsertions = true;
        HandleInBody( token );
        _fosterParentInsertions = false;
    }

    /// <summary>The start tags <c>handle_in_table</c> handles itself; <c>false</c> for the others, which are foster parented.</summary>
    private bool InTableStartTag( ParserToken token )
    {
        switch ( token.Name )
        {
            case "caption":
                ClearStackToTableContext( );
                AddFormattingMarker( );
                InsertElementFromToken( token );
                _insertionMode = InsertionMode.InCaption;
                return true;
            case "colgroup":
                ClearStackToTableContext( );
                InsertElementFromToken( token );
                _insertionMode = InsertionMode.InColumnGroup;
                return true;
            case "col":
                ClearStackToTableContext( );
                InsertElementOfTagType( "colgroup" );
                ReprocessIn( InsertionMode.InColumnGroup );
                return true;
            case "tbody" or "tfoot" or "thead":
                ClearStackToTableContext( );
                InsertElementFromToken( token );
                _insertionMode = InsertionMode.InTableBody;
                return true;
            case "td" or "th" or "tr":
                ClearStackToTableContext( );
                InsertElementOfTagType( "tbody" );
                ReprocessIn( InsertionMode.InTableBody );
                return true;
            default:
                return InTableOtherStartTag( token );
        }
    }

    private bool InTableOtherStartTag( ParserToken token )
    {
        switch ( token.Name )
        {
            case "table":
                _reprocessCurrentToken = CloseTable( );
                return true;
            case "style" or "script" or "template":
                HandleInHead( token );
                return true;
            case "input" when token.AttributeMatches( "type", "hidden" ):
                InsertElementFromToken( token );
                PopCurrentNode( );
                return true;
            case "form":
                if ( _formElement is null && !HasOpenElement( "template" ) )
                {
                    _formElement = InsertElementFromToken( token );
                    PopCurrentNode( );
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>The end tags <c>handle_in_table</c> handles itself.</summary>
    private bool InTableEndTag( ParserToken token )
    {
        if ( token.Name == "table" )
        {
            CloseTable( );
            return true;
        }

        if ( token.Name == "template" )
        {
            HandleInHead( token );
            return true;
        }

        return TableIgnoredEndTags.Contains( token.Name );
    }

    private void ReprocessIn( InsertionMode mode )
    {
        _insertionMode = mode;
        _reprocessCurrentToken = true;
    }

    /// <summary><c>close_table</c>: <c>false</c> when there's no table in table scope.</summary>
    private bool CloseTable( )
    {
        if ( !HasElementInTableScope( "table" ) )
        {
            return false;
        }

        PopUntilHtml( "table" );
        ResetInsertionModeAppropriately( );

        return true;
    }

    private void ClearStackToTableContext( ) => ClearStackBackTo( "html", "table", "template" );

    private void ClearStackToTableBodyContext( ) => ClearStackBackTo( "html", "tbody", "tfoot", "thead", "template" );

    private void ClearStackToTableRowContext( ) => ClearStackBackTo( "html", "tr", "template" );

    private void ClearStackBackTo( params string[] names )
    {
        while ( !(CurrentNode.IsHtmlElement && names.Contains( CurrentNode.LocalName )) )
        {
            PopCurrentNode( );
        }
    }

    /// <summary>
    /// <c>handle_in_table_text</c>: the pending characters are the text buffer. If any isn't whitespace
    /// they are foster parented, after reconstructing the active formatting elements.
    /// </summary>
    private void HandleInTableText( ParserToken token )
    {
        switch ( token.Kind )
        {
            case ParserTokenKind.Null:
                return;
            case ParserTokenKind.Whitespace or ParserTokenKind.Character:
                InsertText( token );
                return;
        }

        if ( _textBufferType != TextBufferType.Whitespace )
        {
            _fosterParentInsertions = true;
            _framesetOk = false;
            ReconstructActiveFormattingElements( );
        }

        FlushTextBuffer( );
        _fosterParentInsertions = false;
        _reprocessCurrentToken = true;
        _insertionMode = _originalInsertionMode;
    }

    /// <summary><c>handle_in_caption</c>.</summary>
    private void HandleInCaption( ParserToken token )
    {
        var closesCaption = token.IsEndTag( "caption" ) || token.IsStartTagIn( CaptionClosingStartTags ) || token.IsEndTag( "table" );
        if ( closesCaption )
        {
            if ( !HasElementInTableScope( "caption" ) )
            {
                return;
            }

            GenerateImpliedEndTags( );
            PopUntilHtml( "caption" );
            ClearActiveFormattingElements( );
            _insertionMode = InsertionMode.InTable;
            _reprocessCurrentToken = !token.IsEndTag( "caption" );
            return;
        }

        if ( !token.IsEndTagIn( CaptionIgnoredEndTags ) )
        {
            HandleInBody( token );
        }
    }

    /// <summary><c>handle_in_column_group</c>.</summary>
    private void HandleInColumnGroup( ParserToken token )
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
            case ParserTokenKind.EndOfFile:
                HandleInBody( token );
                return;
        }

        if ( InColumnGroupTag( token ) )
        {
            return;
        }

        if ( IsHtml( CurrentNode, "colgroup" ) )
        {
            PopCurrentNode( );
            ReprocessIn( InsertionMode.InTable );
        }
    }

    /// <summary>The tags <c>handle_in_column_group</c> handles itself.</summary>
    private bool InColumnGroupTag( ParserToken token )
    {
        if ( token.IsStartTag( "html" ) )
        {
            HandleInBody( token );
        }
        else if ( token.IsStartTag( "col" ) )
        {
            InsertElementFromToken( token );
            PopCurrentNode( );
        }
        else if ( token.IsEndTag( "colgroup" ) )
        {
            if ( IsHtml( CurrentNode, "colgroup" ) )
            {
                PopCurrentNode( );
                _insertionMode = InsertionMode.InTable;
            }
        }
        else if ( token.IsStartTag( "template" ) || token.IsEndTag( "template" ) )
        {
            HandleInHead( token );
        }
        else
        {
            return token.IsEndTag( "col" );
        }

        return true;
    }

    /// <summary><c>handle_in_table_body</c>.</summary>
    private void HandleInTableBody( ParserToken token )
    {
        if ( token.IsStartTag( "tr" ) )
        {
            ClearStackToTableBodyContext( );
            InsertElementFromToken( token );
            _insertionMode = InsertionMode.InRow;
        }
        else if ( token.IsStartTagIn( GumboTagSets.TableCells ) )
        {
            ClearStackToTableBodyContext( );
            InsertElementOfTagType( "tr" );
            ReprocessIn( InsertionMode.InRow );
        }
        else if ( token.IsEndTagIn( TableSections ) )
        {
            if ( HasElementInTableScope( token.Name ) )
            {
                ClearStackToTableBodyContext( );
                PopCurrentNode( );
                _insertionMode = InsertionMode.InTable;
            }
        }
        else if ( token.IsStartTagIn( TableBodyClosingStartTags ) || token.IsEndTag( "table" ) )
        {
            if ( HasElementInTableScope( "tbody" ) || HasElementInTableScope( "thead" ) || HasElementInTableScope( "tfoot" ) )
            {
                ClearStackToTableBodyContext( );
                PopCurrentNode( );
                ReprocessIn( InsertionMode.InTable );
            }
        }
        else if ( !token.IsEndTagIn( TableBodyIgnoredEndTags ) )
        {
            HandleInTable( token );
        }
    }

    /// <summary><c>handle_in_row</c>.</summary>
    private void HandleInRow( ParserToken token )
    {
        if ( token.IsStartTagIn( GumboTagSets.TableCells ) )
        {
            ClearStackToTableRowContext( );
            InsertElementFromToken( token );
            _insertionMode = InsertionMode.InCell;
            AddFormattingMarker( );
        }
        else if ( token.IsEndTag( "tr" ) )
        {
            CloseRow( reprocess: false );
        }
        else if ( token.IsStartTagIn( RowClosingStartTags ) || token.IsEndTag( "table" ) )
        {
            CloseRow( reprocess: true );
        }
        else if ( token.IsEndTagIn( TableSections ) )
        {
            if ( HasElementInTableScope( token.Name ) && HasElementInTableScope( "tr" ) )
            {
                CloseRow( reprocess: true );
            }
        }
        else if ( !token.IsEndTagIn( RowIgnoredEndTags ) )
        {
            HandleInTable( token );
        }
    }

    private void CloseRow( bool reprocess )
    {
        if ( !HasElementInTableScope( "tr" ) )
        {
            return;
        }

        ClearStackToTableRowContext( );
        PopCurrentNode( );
        _insertionMode = InsertionMode.InTableBody;
        _reprocessCurrentToken = reprocess;
    }

    /// <summary><c>handle_in_cell</c>.</summary>
    private void HandleInCell( ParserToken token )
    {
        if ( token.IsEndTagIn( GumboTagSets.TableCells ) )
        {
            if ( HasElementInTableScope( token.Name ) )
            {
                CloseTableCell( token.Name );
            }
        }
        else if ( token.IsStartTagIn( CaptionClosingStartTags ) )
        {
            if ( HasElementInTableScope( "th" ) || HasElementInTableScope( "td" ) )
            {
                _reprocessCurrentToken = true;
                CloseCurrentCell( );
            }
        }
        else if ( token.IsEndTagIn( CellClosingEndTags ) )
        {
            if ( HasElementInTableScope( token.Name ) )
            {
                _reprocessCurrentToken = true;
                CloseCurrentCell( );
            }
        }
        else if ( !token.IsEndTagIn( CellIgnoredEndTags ) )
        {
            HandleInBody( token );
        }
    }

    /// <summary><c>close_current_cell</c>.</summary>
    private void CloseCurrentCell( ) => CloseTableCell( HasElementInTableScope( "td" ) ? "td" : "th" );

    /// <summary><c>close_table_cell</c>.</summary>
    private void CloseTableCell( string cellTag )
    {
        GenerateImpliedEndTags( );
        PopUntilHtml( cellTag );
        ClearActiveFormattingElements( );
        _insertionMode = InsertionMode.InRow;
    }
}
