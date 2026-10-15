using Campfire.Application.Storage;

namespace Campfire.Web.Endpoints.ActiveStorage;

/// <summary><c>@representation</c>: the processed variant's or preview's image.</summary>
public sealed record CurrentRepresentation( Blob Image );
