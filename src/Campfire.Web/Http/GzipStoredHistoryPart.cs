using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Http;

/// <summary>A <see cref="GzipHistoryPart"/> copied for the store.</summary>
internal readonly record struct GzipStoredHistoryPart( ContentDigest Identity, byte[] Glue );
