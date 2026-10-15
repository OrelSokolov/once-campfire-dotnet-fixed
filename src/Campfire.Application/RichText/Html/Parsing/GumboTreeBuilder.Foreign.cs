namespace Campfire.Application.RichText.Html.Parsing;

/// <summary><c>handle_in_foreign_content</c>: SVG and MathML.</summary>
internal sealed partial class GumboTreeBuilder
{
    private void HandleInForeignContent( ParserToken token )
    {
        switch ( token.Kind )
        {
            case ParserTokenKind.Null:
                InsertText( token, new string( '\uFFFD', token.Text.Length ) );
                return;
            case ParserTokenKind.Whitespace:
                InsertText( token );
                return;
            case ParserTokenKind.Character or ParserTokenKind.CData:
                InsertText( token );
                _framesetOk = false;
                return;
            case ParserTokenKind.Comment:
                AppendCommentToCurrentNode( token );
                return;
            case ParserTokenKind.Doctype:
                return;
        }

        if ( IsForeignBreakout( token ) )
        {
            // Close the foreign content up to an integration point or an HTML element
            while ( !IsMathMlIntegrationPoint( CurrentNode ) && !IsHtmlIntegrationPoint( CurrentNode ) && !CurrentNode.IsHtmlElement )
            {
                PopCurrentNode( );
            }

            HandleHtmlContent( token );
        }
        else if ( token.Kind == ParserTokenKind.StartTag )
        {
            ForeignStartTag( token );
        }
        else
        {
            ForeignEndTag( token );
        }
    }

    private static bool IsForeignBreakout( ParserToken token ) =>
        token.IsStartTagIn( GumboTagSets.ForeignBreakout )
        || (token.IsStartTag( "font" ) && (token.HasAttribute( "color" ) || token.HasAttribute( "face" ) || token.HasAttribute( "size" )))
        || token.IsEndTag( "br" ) || token.IsEndTag( "p" );

    /// <summary>A start tag in foreign content: an element in the adjusted current node's namespace.</summary>
    private void ForeignStartTag( ParserToken token )
    {
        var elementNamespace = AdjustedCurrentNode!.Namespace;
        if ( elementNamespace == HtmlNamespace.MathMl )
        {
            AdjustMathMlAttributes( token );
        }

        if ( elementNamespace == HtmlNamespace.Svg )
        {
            AdjustSvgTag( token );
            AdjustSvgAttributes( token );
        }

        AdjustForeignAttributes( token );
        InsertForeignElement( token, elementNamespace );
        if ( token.IsSelfClosing )
        {
            PopCurrentNode( );
        }
    }

    /// <summary>
    /// An end tag in foreign content closes the nearest foreign element with its name, looking no
    /// further than the first HTML element, which gets the token instead.
    /// </summary>
    private void ForeignEndTag( ParserToken token )
    {
        var node = CurrentNode;
        var index = _openElements.Count - 1;
        while ( index > 0 )
        {
            if ( TagNameIs( node, token.Name ) )
            {
                PopThrough( node );
                return;
            }

            node = _openElements[--index];
            if ( node.IsHtmlElement )
            {
                break;
            }
        }

        if ( index != 0 )
        {
            HandleHtmlContent( token );
        }
    }
}
