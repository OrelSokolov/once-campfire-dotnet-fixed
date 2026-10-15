using Campfire.Application.Storage;

namespace Campfire.Web.Endpoints.ActiveStorage;

/// <summary><c>@blob</c> or <c>@representation</c>: the blob the action serves.</summary>
public sealed record CurrentBlob( Blob Blob );
