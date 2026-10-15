using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Http;

/// <summary>The part of a history a text piece is keyed by.</summary>
internal readonly record struct GzipTextHistory( GzipHistoryKind Kind, ContentDigest Identity );
