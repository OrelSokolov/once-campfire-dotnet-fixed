namespace Campfire.Application.RichText.Html;

/// <summary>The namespaces an element can have after HTML5 tree construction.</summary>
internal enum HtmlNamespace
{
    Html,
    Svg,
    MathMl,
}

internal enum HtmlNodeKind
{
    /// <summary>A document fragment: the root of a parsed body.</summary>
    Fragment,
    Element,
    Text,
    Comment,

    /// <summary>A CDATA section, which Gumbo creates for <c>&lt;![CDATA[</c> in SVG and MathML.</summary>
    CData,
}

/// <summary>
/// A node of the small DOM the pipeline works on, shaped like the libxml2 trees Nokogiri's HTML5
/// parser builds: template contents are ordinary children, and adjacent text nodes are merged when
/// a tree is parsed (libxml2's <c>xmlAddChild</c> merges them).
/// </summary>
internal sealed class HtmlNode
{
    private readonly List<HtmlNode> _children = [];

    private HtmlNode( HtmlNodeKind kind, HtmlNamespace nodeNamespace, string name, List<HtmlAttribute>? attributes, string text )
    {
        Kind = kind;
        Namespace = nodeNamespace;
        LocalName = name;
        Attributes = attributes ?? [];
        Text = text;
    }

    public HtmlNodeKind Kind { get; }

    /// <summary>The element's namespace (HTML for anything that isn't an element).</summary>
    public HtmlNamespace Namespace { get; }

    /// <summary>The element's local name, or empty for other nodes.</summary>
    public string LocalName { get; }

    public List<HtmlAttribute> Attributes { get; }

    /// <summary>The character data of a text or comment node.</summary>
    public string Text { get; set; }

    public HtmlNode? Parent { get; private set; }

    public IReadOnlyList<HtmlNode> Children => _children;

    public bool IsElement => Kind == HtmlNodeKind.Element;

    public bool IsText => Kind == HtmlNodeKind.Text;

    public bool IsHtmlElement => Kind == HtmlNodeKind.Element && Namespace == HtmlNamespace.Html;

    /// <summary>
    /// Nokogiri's <c>Node#name</c>: the local name for elements, <c>text</c>, <c>comment</c> and
    /// <c>#document-fragment</c> otherwise.
    /// </summary>
    public string Name => Kind switch
    {
        HtmlNodeKind.Element => LocalName,
        HtmlNodeKind.Text => "text",
        HtmlNodeKind.Comment => "comment",
        HtmlNodeKind.CData => "#cdata-section",
        _ => "#document-fragment",
    };

    public static HtmlNode CreateFragment( ) => new( HtmlNodeKind.Fragment, HtmlNamespace.Html, "", null, "" );

    public static HtmlNode CreateElement( HtmlNamespace elementNamespace, string localName, List<HtmlAttribute>? attributes = null ) =>
        new( HtmlNodeKind.Element, elementNamespace, localName, attributes, "" );

    /// <summary>An HTML element with plain (unprefixed) attributes, in order.</summary>
    public static HtmlNode CreateHtmlElement( string localName, IEnumerable<KeyValuePair<string, string>> attributes ) =>
        CreateElement( HtmlNamespace.Html, localName, [.. attributes.Select( pair => new HtmlAttribute( null, pair.Key, pair.Value ) )] );

    public static HtmlNode CreateText( string text ) => new( HtmlNodeKind.Text, HtmlNamespace.Html, "", null, text );

    public static HtmlNode CreateComment( string text ) => new( HtmlNodeKind.Comment, HtmlNamespace.Html, "", null, text );

    public static HtmlNode CreateCData( string text ) => new( HtmlNodeKind.CData, HtmlNamespace.Html, "", null, text );

    /// <summary>An HTML <c>form</c> element, which changes how a fragment parsed inside it treats <c>&lt;form&gt;</c>.</summary>
    public bool IsNamedHtmlForm( ) => IsHtmlElement && LocalName == "form";

    /// <summary>Whether this is an element with the given local name, in any namespace.</summary>
    public bool IsNamed( string localName ) => Kind == HtmlNodeKind.Element && LocalName == localName;

    public string? GetAttribute( string qualifiedName )
    {
        var index = IndexOfAttribute( qualifiedName );
        return index < 0 ? null : Attributes[index].Value;
    }

    public bool HasAttribute( string qualifiedName ) => IndexOfAttribute( qualifiedName ) >= 0;

    /// <summary>Nokogiri's <c>node[name] = value</c>: updates in place, or appends a new attribute.</summary>
    public void SetAttribute( string qualifiedName, string value )
    {
        var index = IndexOfAttribute( qualifiedName );
        if ( index < 0 )
        {
            Attributes.Add( new HtmlAttribute( null, qualifiedName, value ) );
        }
        else
        {
            Attributes[index].Value = value;
        }
    }

    public string? RemoveAttribute( string qualifiedName )
    {
        var index = IndexOfAttribute( qualifiedName );
        if ( index < 0 )
        {
            return null;
        }

        var value = Attributes[index].Value;
        Attributes.RemoveAt( index );

        return value;
    }

    /// <summary>Element children only (Nokogiri's <c>Node#elements</c>).</summary>
    public List<HtmlNode> ElementChildren( ) => [.. _children.Where( child => child.IsElement )];

    /// <summary>All descendants in document order, excluding the node itself.</summary>
    public List<HtmlNode> Descendants( )
    {
        var result = new List<HtmlNode>( );
        var stack = new Stack<HtmlNode>( );
        PushChildrenReversed( stack, this );
        while ( stack.TryPop( out var node ) )
        {
            result.Add( node );
            PushChildrenReversed( stack, node );
        }

        return result;
    }

    /// <summary>Descendant elements with the given local name, in document order.</summary>
    public List<HtmlNode> DescendantsNamed( string localName ) => [.. Descendants( ).Where( node => node.IsNamed( localName ) )];

    public IEnumerable<HtmlNode> Ancestors( )
    {
        for ( var node = Parent; node is not null; node = node.Parent )
        {
            yield return node;
        }
    }

    public void Detach( )
    {
        if ( Parent is { } parent )
        {
            parent._children.RemoveAt( parent._children.LastIndexOf( this ) );
            Parent = null;
        }
    }

    public void Append( HtmlNode child )
    {
        ArgumentNullException.ThrowIfNull( child );

        child.Detach( );
        child.Parent = this;
        _children.Add( child );
    }

    /// <summary>Removes every child, leaving them detached.</summary>
    public void ClearChildren( )
    {
        foreach ( var child in _children )
        {
            child.Parent = null;
        }

        _children.Clear( );
    }

    /// <summary>Removes every child and returns them, detached and in order.</summary>
    public List<HtmlNode> DetachChildren( )
    {
        var children = new List<HtmlNode>( _children );
        ClearChildren( );

        return children;
    }

    /// <summary>
    /// Replaces this node with other nodes (detached from wherever they are first), in one splice so
    /// that replacing among many siblings stays linear. A node without a parent stays as it is.
    /// </summary>
    public void ReplaceWith( IEnumerable<HtmlNode> replacements )
    {
        ArgumentNullException.ThrowIfNull( replacements );

        if ( Parent is not { } parent )
        {
            return;
        }

        var nodes = replacements.ToList( );
        foreach ( var node in nodes )
        {
            node.Detach( );
            node.Parent = parent;
        }

        var index = parent._children.IndexOf( this );
        parent._children.RemoveAt( index );
        parent._children.InsertRange( index, nodes );
        Parent = null;
    }

    /// <summary>
    /// Nokogiri's <c>node.replace(html)</c>: <paramref name="html"/>, parsed in the context of the node's parent,
    /// replaces the node.
    /// </summary>
    public void ReplaceWithParsed( string html ) => ReplaceWith( HtmlFragmentParser.ParseNodes( html, ParseContext.For( Parent ) ) );

    /// <summary>Nokogiri's <c>node.inner_html = html</c>: <paramref name="html"/>, parsed in the node's context, replaces its children.</summary>
    public void SetInnerHtml( string html ) => ReplaceChildren( HtmlFragmentParser.ParseNodes( html, ParseContext.For( this ) ) );

    /// <summary>Detaches every node in <paramref name="nodes"/>, one pass per affected parent.</summary>
    public static void RemoveAll( IEnumerable<HtmlNode> nodes )
    {
        ArgumentNullException.ThrowIfNull( nodes );

        var removed = new HashSet<HtmlNode>( ReferenceEqualityComparer.Instance );
        var parents = new List<HtmlNode>( );
        foreach ( var node in nodes )
        {
            if ( node.Parent is { } parent && removed.Add( node ) )
            {
                parents.Add( parent );
            }
        }

        foreach ( var parent in parents.Distinct( ) )
        {
            parent._children.RemoveAll( child => removed.Contains( child ) );
        }

        foreach ( var node in removed )
        {
            node.Parent = null;
        }
    }

    /// <summary>The text content of a node, as libxml2's <c>xmlNodeGetContent</c> computes it (text and CDATA).</summary>
    public string TextContent( )
    {
        if ( Kind is HtmlNodeKind.Text or HtmlNodeKind.Comment or HtmlNodeKind.CData )
        {
            return Text;
        }

        return string.Concat( Descendants( ).Where( node => node.Kind is HtmlNodeKind.Text or HtmlNodeKind.CData ).Select( node => node.Text ) );
    }

    /// <summary>A detached copy of the node and its subtree.</summary>
    public HtmlNode DeepClone( )
    {
        var copy = ShallowClone( this );
        var pending = new Stack<(HtmlNode Source, HtmlNode Copy)>( );
        pending.Push( (this, copy) );
        while ( pending.TryPop( out var pair ) )
        {
            foreach ( var child in pair.Source._children )
            {
                var childCopy = ShallowClone( child );
                childCopy.Parent = pair.Copy;
                pair.Copy._children.Add( childCopy );
                pending.Push( (child, childCopy) );
            }
        }

        return copy;
    }

    /// <summary>Makes <paramref name="nodes"/> the node's children, in one pass (they must be detached or children already).</summary>
    public void ReplaceChildren( List<HtmlNode> nodes )
    {
        ArgumentNullException.ThrowIfNull( nodes );

        foreach ( var child in _children )
        {
            child.Parent = null;
        }

        _children.Clear( );
        foreach ( var node in nodes )
        {
            if ( node.Parent is { } parent && parent != this )
            {
                node.Detach( );
            }

            node.Parent = this;
            _children.Add( node );
        }
    }

    /// <summary>
    /// Appends a detached child during parsing, merging text into a preceding text node as libxml2
    /// does (the tree builder's text is joined anyway once the tree is built).
    /// </summary>
    internal void AppendParsed( HtmlNode child ) => InsertParsed( _children.Count, child );

    /// <summary>Inserts a detached child before the child at <paramref name="index"/>, merging text like <see cref="AppendParsed"/>.</summary>
    internal void InsertParsed( int index, HtmlNode child )
    {
        if ( child.IsText && index > 0 && _children[index - 1].IsText )
        {
            _children[index - 1].Text += child.Text;
            return;
        }

        child.Parent = this;
        _children.Insert( index, child );
    }

    /// <summary>The position of a child, searching from the end, where the parser's lookups usually succeed.</summary>
    internal int IndexOfChild( HtmlNode child ) => _children.LastIndexOf( child );

    /// <summary>Joins runs of adjacent text children into one node (libxml2's <c>xmlAddChild</c> merging).</summary>
    internal void MergeAdjacentTextChildren( )
    {
        for ( var index = _children.Count - 1; index > 0; index-- )
        {
            if ( _children[index].IsText && _children[index - 1].IsText )
            {
                _children[index - 1].Text += _children[index].Text;
                _children[index].Parent = null;
                _children.RemoveAt( index );
            }
        }
    }

    private static HtmlNode ShallowClone( HtmlNode node ) =>
        new( node.Kind, node.Namespace, node.LocalName, [.. node.Attributes.Select( static attribute => attribute.Clone( ) )], node.Text );

    private int IndexOfAttribute( string qualifiedName ) => HtmlAttribute.IndexOf( Attributes, qualifiedName );

    private static void PushChildrenReversed( Stack<HtmlNode> stack, HtmlNode node )
    {
        for ( var index = node._children.Count - 1; index >= 0; index-- )
        {
            stack.Push( node._children[index] );
        }
    }
}
