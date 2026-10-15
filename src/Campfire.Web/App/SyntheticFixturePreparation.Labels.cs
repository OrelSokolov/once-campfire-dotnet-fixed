using System.Text.Json.Nodes;
using Campfire.Application.Data;
using Campfire.Application.Security;

namespace Campfire.Web.App;

public static partial class SyntheticFixturePreparation
{
    /// <summary>Only the candidate's capture labels change; shared Rails/Rust seed labels stay untouched.</summary>
    internal static JsonObject NativeCaptureLabels( JsonObject original, SignedIds signedIds, DateTimeOffset now )
    {
        var result = new JsonObject( );
        foreach ( var (key, _) in original )
        {
            string? name = null;
            string? purpose = null;
            DateTimeOffset? expiry = null;
            if ( key.StartsWith( "avatar_tokens.", StringComparison.Ordinal ) )
            {
                name = key["avatar_tokens.".Length..];
                purpose = SignedIds.AvatarPurpose;
            }
            else if ( key.StartsWith( "transfers.", StringComparison.Ordinal ) )
            {
                name = key["transfers.".Length..];
                var expired = name.EndsWith( "_expired", StringComparison.Ordinal );
                if ( expired )
                {
                    name = name[..^"_expired".Length];
                }

                purpose = SignedIds.TransferPurpose;
                expiry = expired ? now.AddHours( -1 ) : now.AddHours( 4 );
            }

            if ( name is not null && original["users." + name] is { } id )
            {
                result[key] = signedIds.Generate( ModelNames.User, id.GetValue<long>( ), purpose, expiry );
            }
        }

        return result;
    }
}
