using Campfire.Web.Views.Models;

namespace Campfire.Web.Views.Rendering;

internal interface IRecordedSlice
{
    ViewContext RecordingContext { get; }
}

/// <summary>Record the writer position at the actual literal write, without searching rendered HTML.</summary>
internal sealed class PageRecorder( Utf8PageBuffer writer )
{
    internal const int MinimumFragmentBytes = 1024;
    private readonly List<RecordedFragment> _fragments = [];
    internal DateTimeOffset ValidUntil { get; private set; } = DateTimeOffset.MaxValue;

    internal void Mark( CachedFragment fragment )
    {
        if ( fragment.ValidUntil < ValidUntil )
        {
            ValidUntil = fragment.ValidUntil;
        }

        if ( fragment.Bytes.Length < MinimumFragmentBytes )
        {
            return;
        }

        var position = writer.Length;
        if ( _fragments.Count > 0 && _fragments[^1].End > position )
        {
            throw new InvalidOperationException( "Recorded fragments must not overlap." );
        }

        _fragments.Add( new RecordedFragment( position, fragment ) );
    }

    internal RecordedFragment[] Take( int length )
    {
        if ( _fragments.Count > 0 && _fragments[^1].End > length )
        {
            throw new InvalidOperationException( "A recorded literal was not fully written." );
        }

        return _fragments.ToArray( );
    }
}

internal readonly record struct RecordedFragment( int Offset, CachedFragment Fragment )
{
    internal int End => checked(Offset + Fragment.Bytes.Length);
}
