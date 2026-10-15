using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Http;

/// <summary>Immutable payloads survive eviction for active responses. Compression never runs under these locks.</summary>
internal sealed class GzipPieceStore(
    long fragmentBudget = GzipPieceStore.FragmentBudget,
    long textBudget = GzipPieceStore.TextBudget,
    long memberBudget = GzipPieceStore.MemberBudget )
{
    // Dictionary capacity survives removals; reserve space for both capped generations separately.
    internal const long IndexReserveBytes = 2L * 1024 * 1024;
    internal const long FragmentBudget = 32L * 1024 * 1024;
    internal const long TextBudget = 16L * 1024 * 1024;
    internal const long MemberBudget = 16L * 1024 * 1024;
    internal const int MaximumPieceBytes = 256 * 1024;
    private const int PiecesPerFragment = 4;
    private readonly Lock _fragmentsGate = new( );
    private readonly Lock _textsGate = new( );
    private readonly Lock _membersGate = new( );
    private readonly GzipGenerations<ContentDigest, GzipStoredPiece[]> _fragments = new( Math.Max( 0, fragmentBudget - IndexReserveBytes ) );
    private readonly GzipGenerations<(ContentDigest Text, GzipTextHistory Before), TextEntry> _texts = new( Math.Max( 0, textBudget - IndexReserveBytes ) );
    private readonly GzipGenerations<ContentDigest, MemberEntry> _members = new( Math.Max( 0, memberBudget - IndexReserveBytes ) );
    private long _hits;
    private long _misses;

    internal GzipPiece? Find( CachedFragment? fragment, ContentDigest identity, GzipHistory history, ReadOnlySpan<byte> plain, ReadOnlySpan<byte> glue )
    {
        GzipPiece? found = null;
        if ( fragment is not null )
        {
            lock ( _fragmentsGate )
            {
                if ( _fragments.TryGet( identity, out var pieces ) )
                {
                    foreach ( var piece in pieces )
                    {
                        if ( piece.Fits( history, glue ) )
                        {
                            found = piece.Piece;
                            break;
                        }
                    }
                }
            }
        }
        else
        {
            lock ( _textsGate )
            {
                if ( _texts.TryGet( (identity, history.TextKey), out var entry )
                    && plain.SequenceEqual( entry.Plain )
                    && entry.Stored.Fits( history, [] ) )
                {
                    found = entry.Stored.Piece;
                }
            }
        }

        if ( found is null )
        {
            Interlocked.Increment( ref _misses );
        }
        else
        {
            Interlocked.Increment( ref _hits );
        }

        return found;
    }

    internal void Store(
        CachedFragment? fragment,
        ContentDigest identity,
        GzipHistory history,
        ReadOnlySpan<byte> plain,
        ReadOnlySpan<byte> glue,
        GzipPiece piece )
    {
        if ( piece.Bytes.Length > MaximumPieceBytes || (fragment is null && plain.Length > MaximumPieceBytes) )
        {
            return;
        }

        var stored = new GzipStoredPiece( history.Copy( ), glue.ToArray( ), piece );
        if ( fragment is not null )
        {
            lock ( _fragmentsGate )
            {
                var known = _fragments.TryGet( identity, out var entries ) ? entries : [];
                foreach ( var entry in known )
                {
                    if ( entry.Fits( history, glue ) )
                    {
                        return;
                    }
                }

                var retained = known.TakeLast( PiecesPerFragment - 1 ).Append( stored ).ToArray( );
                _fragments.Set( identity, retained, 512L + retained.Sum( entry => entry.Cost ) );
            }
        }
        else
        {
            var entry = new TextEntry( plain.ToArray( ), stored );
            lock ( _textsGate )
            {
                _texts.Set( (identity, history.TextKey), entry, entry.Plain.Length + stored.Cost );
            }
        }
    }

    internal byte[]? FindMember( ContentDigest digest, ReadOnlySpan<byte> plain )
    {
        lock ( _membersGate )
        {
            if ( _members.TryGet( digest, out var member ) && plain.SequenceEqual( member.Plain ) )
            {
                return member.Compressed;
            }
        }

        return null;
    }

    internal void StoreMember( ContentDigest digest, ReadOnlySpan<byte> plain, byte[] compressed )
    {
        if ( compressed.Length > MaximumPieceBytes || plain.Length > MaximumPieceBytes )
        {
            return;
        }

        var entry = new MemberEntry( plain.ToArray( ), compressed );
        lock ( _membersGate )
        {
            _members.Set( digest, entry, 512L + entry.Plain.Length + compressed.Length );
        }
    }

    internal long TextSuffixBytes
    {
        get
        {
            long fragments, texts;
            lock ( _fragmentsGate )
            {
                fragments = _fragments.SumCharges( static pieces => pieces.Sum( piece => piece.History.TextDictionaryCharge ) );
            }

            lock ( _textsGate )
            {
                texts = _texts.SumCharges( static entry => entry.Stored.History.TextDictionaryCharge );
            }

            return fragments + texts;
        }
    }

    internal GzipCacheSnapshot Snapshot( )
    {
        long fragments, texts, members;
        int fragmentCount, textCount, memberCount;
        lock ( _fragmentsGate )
        {
            fragments = _fragments.Bytes;
            fragmentCount = _fragments.Count;
        }

        lock ( _textsGate )
        {
            texts = _texts.Bytes;
            textCount = _texts.Count;
        }

        lock ( _membersGate )
        {
            members = _members.Bytes;
            memberCount = _members.Count;
        }

        return new GzipCacheSnapshot(
            fragments + IndexReserveBytes,
            texts + IndexReserveBytes,
            members + IndexReserveBytes,
            fragmentCount,
            textCount,
            memberCount,
            Interlocked.Read( ref _hits ),
            Interlocked.Read( ref _misses ) );
    }

    private sealed record TextEntry( byte[] Plain, GzipStoredPiece Stored );
    private sealed record MemberEntry( byte[] Plain, byte[] Compressed );
}
