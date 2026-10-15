using System.Text.Json.Serialization;

namespace Campfire.Web.Security;

/// <summary>The JSON body of an unfurl request: <c>{ "url": … }</c>.</summary>
public sealed record UnfurlRequest( [property: JsonPropertyName( "url" )] string? Url );
