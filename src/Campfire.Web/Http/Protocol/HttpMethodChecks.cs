using System.Runtime.CompilerServices;

namespace Campfire.Web.Http;

/// <summary>
/// Request-method tests as Rack spells them: <c>get?</c>, <c>head?</c> and the like compare the method exactly
/// (<c>request_method == GET</c>), so they are case-sensitive, unlike <see cref="HttpMethods.IsGet"/>. Kestrel's
/// interned method strings make the common case a reference comparison.
/// </summary>
public static class HttpMethodChecks
{
    /// <summary><c>request.get?</c>.</summary>
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static bool IsGet( string method ) => method == HttpMethods.Get;

    /// <summary><c>request.head?</c>.</summary>
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static bool IsHead( string method ) => method == HttpMethods.Head;

    /// <summary><c>request.post?</c>.</summary>
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static bool IsPost( string method ) => method == HttpMethods.Post;

    /// <summary><c>request.options?</c>.</summary>
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static bool IsOptions( string method ) => method == HttpMethods.Options;

    /// <summary><c>request.request_method == "CONNECT"</c>: HTTP/2 extended CONNECT (RFC 8441) WebSockets.</summary>
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static bool IsConnect( string method ) => method == HttpMethods.Connect;

    /// <summary><c>request.get? || request.head?</c>.</summary>
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static bool IsGetOrHead( string method ) => IsGet( method ) || IsHead( method );
}
