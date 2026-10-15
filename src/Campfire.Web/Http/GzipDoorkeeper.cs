namespace Campfire.Web.Http;

/// <summary>Only hashes are retained. Four slots per bucket give a fixed memory ceiling under unique-key churn.</summary>
internal sealed class GzipDoorkeeper
{
    private const int Buckets = 16384;
    private const int Ways = 4;
    internal const int Capacity = Buckets * Ways;
    internal const long ChargedBytes = Capacity * 16L + Buckets + 128;
    private readonly UInt128[] _keys = new UInt128[Capacity];
    // Low four bits indicate occupied slots; high bits select the next replacement slot.
    private readonly byte[] _states = new byte[Buckets];
    private readonly Lock _gate = new( );
    private int _count;
    private long _turnover;

    internal bool Seen( UInt128 key )
    {
        var bucket = ( int ) (( ulong ) key & (Buckets - 1));
        var start = bucket * Ways;
        lock ( _gate )
        {
            var state = _states[bucket];
            for ( var way = 0; way < Ways; way++ )
            {
                if ( (state & (1 << way)) != 0 && _keys[start + way] == key )
                {
                    return true;
                }
            }

            var slot = state >> Ways;
            if ( (state & (1 << slot)) == 0 )
            {
                _count++;
            }
            else
            {
                _turnover++;
            }

            _keys[start + slot] = key;
            _states[bucket] = ( byte ) ((state & 15) | (1 << slot) | (((slot + 1) % Ways) << Ways));
            return false;
        }
    }

    internal GzipDoorkeeperSnapshot Snapshot( )
    {
        lock ( _gate )
        {
            return new( ChargedBytes, _count, _turnover );
        }
    }
}
