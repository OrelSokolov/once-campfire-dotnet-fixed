using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Security;

/// <summary>The source-generated JSON contracts of the request bodies endpoints read and the problem details they write.</summary>
[JsonSerializable( typeof( UnfurlRequest ) )]
[JsonSerializable( typeof( PushSubscriptionRequest ) )]
[JsonSerializable( typeof( ProblemDetails ) )]
public partial class FrontendJsonContext : JsonSerializerContext;
