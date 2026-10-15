using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Http;

/// <summary>One fragment of the current run, borrowed from the page: its identity, the glue before it, and where it ends.</summary>
internal readonly record struct GzipHistoryPart( ContentDigest Identity, ReadOnlyMemory<byte> Glue, int End );
