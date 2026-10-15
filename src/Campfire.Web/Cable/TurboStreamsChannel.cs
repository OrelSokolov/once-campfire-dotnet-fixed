using System.Text.Json.Nodes;

namespace Campfire.Web.Cable;

/// <summary>
/// turbo-rails' <c>Turbo::StreamsChannel</c> (<c>app/channels/turbo/streams_channel.rb</c> at
/// <c>30cd8fc</c>): streams from the verified <c>signed_stream_name</c>, or rejects. An optional
/// guard runs first, the way Campfire prepends <c>RoomStreamsAreAuthorized</c>
/// (<c>app/channels/concerns/room_streams_are_authorized.rb</c>). Ported from Rust
/// <c>crates/cable/src/turbo.rs</c>.
/// <para>
/// Its public methods are actions (<c>Channel::Base.action_methods</c>): <c>subscribed</c>, which verifies, guards and
/// streams again (or rejects), and <c>verified_stream_name_from_params</c>, which verifies and sends nothing.
/// </para>
/// </summary>
public sealed class TurboStreamsChannel( Func<string, string?> verifyStreamName, Func<string, bool>? guard = null ) : CableChannel
{
    public const string ClassName = "Turbo::StreamsChannel";

    /// <summary><c>Turbo::Streams::StreamName::ClassMethods#verified_stream_name_from_params</c>, a public method.</summary>
    public const string VerifiedStreamNameFromParamsAction = "verified_stream_name_from_params";

    /// <summary>The identifier's signed stream name (<c>params[:signed_stream_name]</c>).</summary>
    public const string SignedStreamNameParam = "signed_stream_name";

    public override ValueTask<bool> PerformAsync( string action, JsonObject data, CancellationToken cancellationToken )
    {
        if ( PerformVerifiedStreamNameFromParams( action, Params, verifyStreamName ) )
        {
            return ValueTask.FromResult( true );
        }

        return PerformSubscribedAsync( action, cancellationToken );
    }

    public override ValueTask SubscribedAsync( CancellationToken cancellationToken )
    {
        var streamName = VerifiedStreamNameFromParams( Params, verifyStreamName );

        // The guard sees a name that failed verification as "" (Ruby's nil.to_s).
        if ( guard is not null && guard( streamName ?? string.Empty ) )
        {
            Reject( );
        }
        else if ( streamName is not null )
        {
            StreamFrom( streamName );
        }
        else
        {
            Reject( );
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// <c>verified_stream_name_from_params</c>: <c>params[:signed_stream_name]</c> through the
    /// verifier. A missing or <c>null</c> name is simply unverified; any other non-string makes
    /// <c>MessageVerifier#verified</c> raise, so the subscription gets no reply.
    /// </summary>
    public static string? VerifiedStreamNameFromParams( IReadOnlyDictionary<string, object?> parameters, Func<string, string?> verifyStreamName )
    {
        ArgumentNullException.ThrowIfNull( parameters );
        ArgumentNullException.ThrowIfNull( verifyStreamName );

        var signed = parameters.GetValueOrDefault( SignedStreamNameParam );
        if ( signed is null )
        {
            return null;
        }

        if ( signed is not string value )
        {
            throw new InvalidOperationException( "The signed stream name must be a string." );
        }

        return verifyStreamName( value );
    }

    /// <summary>
    /// The <see cref="VerifiedStreamNameFromParamsAction"/> action of a channel that includes
    /// <c>Turbo::Streams::StreamName::ClassMethods</c>: when <paramref name="action"/> is it, the name is verified (a
    /// non-string raises) and nothing is sent. Returns whether it was that action.
    /// </summary>
    public static bool PerformVerifiedStreamNameFromParams(
        string action,
        IReadOnlyDictionary<string, object?> parameters,
        Func<string, string?> verifyStreamName )
    {
        if ( action != VerifiedStreamNameFromParamsAction )
        {
            return false;
        }

        _ = VerifiedStreamNameFromParams( parameters, verifyStreamName );
        return true;
    }
}
