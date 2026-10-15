namespace Campfire.Web.Cable;

/// <summary>Whether the socket negotiated permessage-deflate, kept on the request for the connection.</summary>
internal sealed record CableNegotiatedCompression( bool Enabled );
