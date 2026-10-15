namespace Campfire.Application.RichText.Attachments;

/// <summary>An attachment's plain-text representation, which replaces the attachment node.</summary>
internal abstract record PlainTextRepresentation;

/// <summary>A string, parsed as markup in the node's parent (<c>node.replace(string)</c>).</summary>
internal sealed record PlainTextMarkup( string Html ) : PlainTextRepresentation;

/// <summary>A content attachment's fragment, moved in as is.</summary>
internal sealed record PlainTextContent( string Content ) : PlainTextRepresentation;
