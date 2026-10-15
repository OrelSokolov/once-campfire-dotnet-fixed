using System.Net.Sockets;
using System.Security.Authentication;
using Campfire.Application.Ruby;

namespace Campfire.Application.Integrations.Network;

/// <summary>What went wrong with an outbound request, as the Ruby exception <c>Net::HTTP</c> raises would say.</summary>
public enum NetHttpFailure
{
    /// <summary><c>Net::OpenTimeout</c>: connecting (TCP and TLS) took longer than the open timeout.</summary>
    OpenTimeout,

    /// <summary><c>Net::ReadTimeout</c>: one read waited longer than the read timeout.</summary>
    ReadTimeout,

    /// <summary><c>Errno::ECONNREFUSED</c>.</summary>
    ConnectionRefused,

    /// <summary><c>SocketError</c>: the host name did not resolve.</summary>
    Unresolvable,

    /// <summary><c>OpenSSL::SSL::SSLError</c>: the TLS handshake failed or the certificate did not verify.</summary>
    Tls,

    /// <summary>Another <c>SystemCallError</c> (reset, unreachable, ...).</summary>
    Socket,

    /// <summary><c>Net::HTTPBadResponse</c>, <c>EOFError</c>: the response did not parse or ended early.</summary>
    Protocol,

    /// <summary><c>Zlib::Error</c>: a compressed body did not inflate.</summary>
    Inflate,

    /// <summary><c>ArgumentError</c> / <c>URI::InvalidURIError</c>: the request could not be built.</summary>
    InvalidRequest,
}

/// <summary>An outbound request failed the way <c>Net::HTTP</c> would have raised.</summary>
public sealed class NetHttpException : Exception
{
    public NetHttpException( ) : this( NetHttpFailure.Protocol, "HTTP request failed" )
    {
    }

    public NetHttpException( string message ) : this( NetHttpFailure.Protocol, message )
    {
    }

    public NetHttpException( string message, Exception innerException ) : this( NetHttpFailure.Protocol, message, innerException )
    {
    }

    public NetHttpException( NetHttpFailure failure, string message, Exception? innerException = null ) : base( message, innerException )
    {
        Failure = failure;
    }

    public NetHttpFailure Failure { get; }

    /// <summary>Whether Ruby would have raised <c>Net::OpenTimeout</c> or <c>Net::ReadTimeout</c>.</summary>
    public bool IsTimeout => Failure is NetHttpFailure.OpenTimeout or NetHttpFailure.ReadTimeout;

    /// <summary>The Ruby exception class, for logs and job failures.</summary>
    public string RubyClass => Failure switch
    {
        NetHttpFailure.OpenTimeout => RubyErrorClasses.OpenTimeout,
        NetHttpFailure.ReadTimeout => RubyErrorClasses.ReadTimeout,
        NetHttpFailure.ConnectionRefused => RubyErrorClasses.ConnectionRefused,
        NetHttpFailure.Unresolvable => RubyErrorClasses.SocketError,
        NetHttpFailure.Tls => RubyErrorClasses.SslError,
        NetHttpFailure.Socket => RubyErrorClasses.SystemCallError,
        NetHttpFailure.Inflate => RubyErrorClasses.ZlibDataError,
        NetHttpFailure.InvalidRequest => RubyErrorClasses.ArgumentError,
        _ => RubyErrorClasses.HttpBadResponse,
    };

    /// <summary>
    /// Classifies what <see cref="HttpMessageInvoker"/> or a body read threw. Timeouts are recognized by the markers
    /// the clients plant (<see cref="ReadTimeoutException"/>, and the <see cref="TimeoutException"/> behind a connect
    /// timeout), before the TLS and socket errors they may be wrapped in.
    /// </summary>
    internal static NetHttpException From( Exception error )
    {
        if ( error is NetHttpException known )
        {
            return known;
        }

        var chain = Chain( error ).ToList( );
        if ( chain.Exists( exception => exception is ReadTimeoutException ) )
        {
            return new NetHttpException( NetHttpFailure.ReadTimeout, RubyErrorClasses.ReadTimeout, error );
        }

        if ( error is OperationCanceledException && chain.Exists( exception => exception is TimeoutException ) )
        {
            return new NetHttpException( NetHttpFailure.OpenTimeout, "execution expired", error );
        }

        if ( chain.Exists( exception => exception is HostUnresolvableException ) )
        {
            return new NetHttpException( NetHttpFailure.Unresolvable, "getaddrinfo: Name or service not known", error );
        }

        var isTlsFailure = chain.Exists( exception => exception is AuthenticationException )
            || error is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError };
        if ( isTlsFailure )
        {
            return new NetHttpException( NetHttpFailure.Tls, $"SSL_connect returned=1 errno=0 state=error: {error.Message}", error );
        }

        if ( chain.OfType<SocketException>( ).FirstOrDefault( ) is { } socket )
        {
            return socket.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => new NetHttpException( NetHttpFailure.ConnectionRefused, "Connection refused", error ),
                SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain =>
                    new NetHttpException( NetHttpFailure.Unresolvable, socket.Message, error ),
                _ => new NetHttpException( NetHttpFailure.Socket, socket.Message, error ),
            };
        }

        if ( chain.Exists( exception => exception is InvalidDataException ) )
        {
            return new NetHttpException( NetHttpFailure.Inflate, "incorrect header check", error );
        }

        return new NetHttpException( NetHttpFailure.Protocol, error.Message, error );
    }

    private static IEnumerable<Exception> Chain( Exception error )
    {
        for ( Exception? current = error; current is not null; current = current.InnerException )
        {
            yield return current;
        }
    }
}
