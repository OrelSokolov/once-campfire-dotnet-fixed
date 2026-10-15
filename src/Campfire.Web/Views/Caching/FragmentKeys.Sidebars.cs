using System.Buffers;
using Campfire.Application.Data;

namespace Campfire.Web.Views.Caching;

public static partial class FragmentKeys
{
    internal const int SidebarDirectKeyCapacity = 192;

    private const string SidebarDirectPrefix = "views/users/sidebars/rooms/_direct/";

    /// <summary>Writes the sidebar direct room fragment's key into <paramref name="key"/> and returns its length.</summary>
    internal static int WriteSidebarDirectKey( RecordVersion membership, RecordVersion room, IReadOnlyList<RecordVersion> members, Span<char> key )
    {
        // Each record has fixed vocabulary, an Int64 ID and the Rails UTC timestamp. Keep enough room for
        // their largest formats; only unusually large groups need a rental for the digest input.
        const int recordCapacity = 140;
        var capacity = checked((members.Count + 1) * recordCapacity);
        char[]? rental = capacity > StackCapacity ? ArrayPool<char>.Shared.Rent( capacity ) : null;
        Span<char> parts = rental is null ? stackalloc char[StackCapacity] : rental;
        var position = 0;
        try
        {
            position += WriteRecord( parts, room, TableNames.Rooms );
            parts[position++] = '\n';
            for ( var index = 0; index < members.Count; index++ )
            {
                if ( index > 0 )
                {
                    parts[position++] = '\n';
                }

                position += WriteRecord( parts[position..], members[index], TableNames.Users );
            }

            SidebarDirectPrefix.CopyTo( key );
            var written = SidebarDirectPrefix.Length + WriteRecord( key[SidebarDirectPrefix.Length..], membership, TableNames.Memberships );
            key[written++] = '/';
            WriteDigest( parts[..position], key.Slice( written, DigestChars ) );
            return written + DigestChars;
        }
        finally
        {
            parts[..position].Clear( );
            if ( rental is not null )
            {
                ArrayPool<char>.Shared.Return( rental );
            }
        }
    }
}
