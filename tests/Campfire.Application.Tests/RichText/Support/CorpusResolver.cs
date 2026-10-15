using System.Text.Json.Nodes;
using Campfire.Application.RichText;

namespace Campfire.Application.Tests.RichText.Support;

/// <summary>
/// Stands in for the app over the corpus's <c>users</c> and <c>signed</c> sections:
/// signed attachables Rails minted verify by exact match.
/// </summary>
internal sealed class CorpusResolver : IAttachableResolver
{
    private readonly List<(string Sgid, string Model, long Id, bool Exists)> _signed;

    public CorpusResolver( JsonNode corpus )
    {
        Users = [.. corpus["users"]!.AsArray( ).Select( user => new MentionUser(
            user!["id"]!.GetValue<long>( ),
            user["name"]!.GetValue<string>( ),
            user["title"]!.GetValue<string>( ),
            user["attachable_sgid"]!.GetValue<string>( ),
            user["user_path"]!.GetValue<string>( ),
            user["avatar_path"]!.GetValue<string>( ) ) )];
        _signed = [.. corpus["signed"]!.AsArray( ).Select( entry => (
            entry!["sgid"]!.GetValue<string>( ),
            entry["model"]!.GetValue<string>( ),
            entry["id"]!.GetValue<long>( ),
            entry["exists"]!.GetValue<bool>( )) )];
    }

    public List<MentionUser> Users { get; }

    public SignedAttachableLookup LocateSigned( string signedGlobalId )
    {
        foreach ( var entry in _signed )
        {
            if ( entry.Sgid != signedGlobalId )
            {
                continue;
            }

            return entry is { Model: "User", Exists: true }
                ? SignedAttachableLookup.ForUser( Users.Single( user => user.Id == entry.Id ) )
                : SignedAttachableLookup.ForMissingRecord( entry.Model );
        }

        return SignedAttachableLookup.Invalid;
    }
}
