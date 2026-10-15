using System.Text.Json.Serialization;
using Campfire.Application.Data;

namespace Campfire.Web.Security;

/// <summary>A push subscription's fields, remembering which ones the JSON named.</summary>
public sealed class PushSubscriptionBody
{
    [JsonPropertyName( "endpoint" )]
    public string? Endpoint
    {
        get => EndpointAssignment.Value;
        init => EndpointAssignment = new( value );
    }

    [JsonPropertyName( "p256dh_key" )]
    public string? P256dhKey
    {
        get => P256dhKeyAssignment.Value;
        init => P256dhKeyAssignment = new( value );
    }

    [JsonPropertyName( "auth_key" )]
    public string? AuthKey
    {
        get => AuthKeyAssignment.Value;
        init => AuthKeyAssignment = new( value );
    }

    // JSON omission and explicit null are distinct registration inputs.
    internal Assigned<string?> EndpointAssignment { get; private init; }
    internal Assigned<string?> P256dhKeyAssignment { get; private init; }
    internal Assigned<string?> AuthKeyAssignment { get; private init; }
}
