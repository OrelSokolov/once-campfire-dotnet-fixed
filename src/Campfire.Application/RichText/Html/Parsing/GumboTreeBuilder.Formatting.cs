namespace Campfire.Application.RichText.Html.Parsing;

internal sealed partial class GumboTreeBuilder
{
    private void AddFormattingMarker( ) => _activeFormattingElements.Add( null );

    /// <summary><c>add_formatting_element</c>, with the Noah's Ark clause: at most three identical entries after the last marker.</summary>
    private void AddFormattingElement( HtmlNode element )
    {
        var identical = 0;
        var earliest = -1;
        for ( var index = _activeFormattingElements.Count - 1; index >= 0; index-- )
        {
            if ( _activeFormattingElements[index] is not { } entry )
            {
                break;
            }

            if ( entry.Namespace == element.Namespace && TagNameIs( entry, element.LocalName ) && AllAttributesMatch( entry, element ) )
            {
                identical++;
                earliest = index;
            }
        }

        if ( identical >= 3 )
        {
            _activeFormattingElements.RemoveAt( earliest );
        }

        _activeFormattingElements.Add( element );
    }

    /// <summary><c>all_attributes_match</c>: the same attributes with the same values, in any order.</summary>
    private static bool AllAttributesMatch( HtmlNode left, HtmlNode right ) =>
        left.Attributes.Count == right.Attributes.Count
        && left.Attributes.TrueForAll( attribute => right.GetAttribute( attribute.QualifiedName ) == attribute.Value );

    /// <summary><c>clear_active_formatting_elements</c>: up to and including the last marker.</summary>
    private void ClearActiveFormattingElements( )
    {
        while ( _activeFormattingElements.Count > 0 )
        {
            var entry = _activeFormattingElements[^1];
            _activeFormattingElements.RemoveAt( _activeFormattingElements.Count - 1 );
            if ( entry is null )
            {
                return;
            }
        }
    }

    /// <summary><c>clone_node</c>: the element and its attributes, without children.</summary>
    private static HtmlNode CloneElement( HtmlNode element ) =>
        HtmlNode.CreateElement( element.Namespace, element.LocalName, [.. element.Attributes.Select( static attribute => attribute.Clone( ) )] );

    /// <summary><c>reconstruct_active_formatting_elements</c>.</summary>
    private void ReconstructActiveFormattingElements( )
    {
        if ( _activeFormattingElements.Count == 0 )
        {
            return;
        }

        var index = _activeFormattingElements.Count - 1;
        if ( _activeFormattingElements[index] is not { } last || IsOpenElement( last ) )
        {
            return;
        }

        // Rewind to the entry after the last marker or open element
        while ( index > 0 && _activeFormattingElements[index - 1] is { } previous && !IsOpenElement( previous ) )
        {
            index--;
        }

        for ( ; index < _activeFormattingElements.Count; index++ )
        {
            var clone = CloneElement( _activeFormattingElements[index]! );
            InsertNode( clone, GetAppropriateInsertionLocation( ) );
            _openElements.Add( clone );
            _activeFormattingElements[index] = clone;
        }
    }

    /// <summary>The last <c>a</c> in the list of active formatting elements after the last marker.</summary>
    private int FindLastAnchorIndex( )
    {
        for ( var index = _activeFormattingElements.Count - 1; index >= 0; index-- )
        {
            if ( _activeFormattingElements[index] is not { } entry )
            {
                return -1;
            }

            if ( IsHtml( entry, "a" ) )
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// <c>in_body_any_other_end_tag</c>: closes the nearest open element with the token's name,
    /// unless a special element comes first.
    /// </summary>
    private void InBodyAnyOtherEndTag( ParserToken token )
    {
        for ( var index = _openElements.Count - 1; index >= 0; index-- )
        {
            var node = _openElements[index];
            if ( IsHtml( node, token.Name ) )
            {
                GenerateImpliedEndTags( token.Name );
                PopThrough( node );
                return;
            }

            if ( IsSpecial( node ) )
            {
                return;
            }
        }
    }

    /// <summary>
    /// <c>adoption_agency_algorithm</c>, as Gumbo implements it. Where the standard removes a
    /// formatting element that the inner loop passes more than three times from both the list and the
    /// stack of open elements, Gumbo removes it from the list only.
    /// </summary>
    private void AdoptionAgencyAlgorithm( ParserToken token )
    {
        var subject = token.Name;
        var current = CurrentNode;
        if ( IsHtml( current, subject ) && !_activeFormattingElements.Contains( current ) )
        {
            PopCurrentNode( );
            return;
        }

        for ( var outer = 0; outer < 8; outer++ )
        {
            if ( !RunAdoptionAgencyStep( token, subject ) )
            {
                return;
            }
        }
    }

    /// <summary>One pass of the outer loop; <c>false</c> when the algorithm is done.</summary>
    private bool RunAdoptionAgencyStep( ParserToken token, string subject )
    {
        var formattingNode = FindFormattingElement( subject );
        if ( formattingNode is null )
        {
            InBodyAnyOtherEndTag( token );
            return false;
        }

        var formattingIndex = _openElements.IndexOf( formattingNode );
        if ( formattingIndex == -1 )
        {
            _activeFormattingElements.Remove( formattingNode );
            return false;
        }

        if ( !HasElementInScope( formattingNode.LocalName ) )
        {
            return false;
        }

        var furthestBlock = _openElements.Skip( formattingIndex ).FirstOrDefault( IsSpecial );
        if ( furthestBlock is null )
        {
            PopThrough( formattingNode );
            _activeFormattingElements.Remove( formattingNode );
            return false;
        }

        var commonAncestor = _openElements[formattingIndex - 1];
        var bookmark = 1 + _activeFormattingElements.IndexOf( formattingNode );
        var lastNode = RunAdoptionAgencyInnerLoop( formattingNode, furthestBlock, ref bookmark );

        lastNode.Detach( );
        InsertNode( lastNode, GetAppropriateInsertionLocation( commonAncestor ) );

        var newFormattingNode = CloneElement( formattingNode );
        foreach ( var child in furthestBlock.DetachChildren( ) )
        {
            newFormattingNode.AppendParsed( child );
        }

        furthestBlock.AppendParsed( newFormattingNode );

        var oldIndex = _activeFormattingElements.IndexOf( formattingNode );
        if ( oldIndex < bookmark )
        {
            bookmark--;
        }

        _activeFormattingElements.RemoveAt( oldIndex );
        _activeFormattingElements.Insert( bookmark, newFormattingNode );

        _openElements.Remove( formattingNode );
        _openElements.Insert( 1 + _openElements.IndexOf( furthestBlock ), newFormattingNode );

        return true;
    }

    /// <summary>The last entry named <paramref name="subject"/> after the last marker.</summary>
    private HtmlNode? FindFormattingElement( string subject )
    {
        for ( var index = _activeFormattingElements.Count - 1; index >= 0; index-- )
        {
            if ( _activeFormattingElements[index] is not { } entry )
            {
                return null;
            }

            if ( IsHtml( entry, subject ) )
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>Steps 14.1 to 14.11: the nodes between the furthest block and the formatting element.</summary>
    private HtmlNode RunAdoptionAgencyInnerLoop( HtmlNode formattingNode, HtmlNode furthestBlock, ref int bookmark )
    {
        var node = furthestBlock;
        var lastNode = furthestBlock;
        var savedNodeIndex = _openElements.IndexOf( node );
        for ( var iteration = 1; ; iteration++ )
        {
            var nodeIndex = _openElements.IndexOf( node );
            if ( nodeIndex == -1 )
            {
                nodeIndex = savedNodeIndex;
            }

            savedNodeIndex = --nodeIndex;
            node = _openElements[nodeIndex];
            if ( node == formattingNode )
            {
                return lastNode;
            }

            var formattingIndex = _activeFormattingElements.IndexOf( node );
            if ( iteration > 3 && formattingIndex != -1 )
            {
                _activeFormattingElements.RemoveAt( formattingIndex );
                if ( formattingIndex < bookmark )
                {
                    bookmark--;
                }

                continue;
            }

            if ( formattingIndex == -1 )
            {
                _openElements.RemoveAt( nodeIndex );
                continue;
            }

            node = CloneElement( node );
            _activeFormattingElements[formattingIndex] = node;
            _openElements[nodeIndex] = node;
            if ( lastNode == furthestBlock )
            {
                bookmark = formattingIndex + 1;
            }

            lastNode.Detach( );
            node.AppendParsed( lastNode );
            lastNode = node;
        }
    }
}
