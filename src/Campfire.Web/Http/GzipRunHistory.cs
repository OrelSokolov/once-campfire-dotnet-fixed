using System.Runtime.InteropServices;
using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Http;

/// <summary>Ordered identities for only the current fragment run's effective deflate window.</summary>
internal sealed class GzipRunHistory
{
    internal const int WindowBytes = 32768;
    private readonly List<GzipHistoryPart> _run = [];
    private PagePart? _previous;
    private ContentDigest _previousIdentity;
    private ReadOnlyMemory<byte> _previousTextDictionary;
    private int _runStart;

    internal int DictionaryStart { get; private set; }

    internal GzipHistory Before( PagePart part )
    {
        DictionaryStart = part.Offset;
        if ( _previous is not { } previous )
        {
            return new( GzipHistoryKind.Empty, default, [] );
        }

        if ( part.Fragment is not null && previous.Fragment is not null )
        {
            DictionaryStart = Math.Max( _runStart, part.Offset - WindowBytes );
            var drop = 0;
            while ( drop < _run.Count && _run[drop].End <= DictionaryStart )
            {
                drop++;
            }

            if ( drop > 0 )
            {
                _run.RemoveRange( 0, drop );
            }

            return new( DictionaryStart == _runStart ? GzipHistoryKind.RunStart : GzipHistoryKind.RunCut, default, CollectionsMarshal.AsSpan( _run ) );
        }

        var ownLength = previous.Length - previous.Glue;
        DictionaryStart = previous.Offset + previous.Glue + Math.Max( 0, ownLength - WindowBytes );
        return new( previous.Fragment is null ? GzipHistoryKind.Text : GzipHistoryKind.Fragment, _previousIdentity, [], _previousTextDictionary.Span );
    }

    internal void Advance( PagePart part, ContentDigest identity, ReadOnlyMemory<byte> page )
    {
        if ( part.Fragment is null )
        {
            _run.Clear( );
            _runStart = part.Offset + part.Length;
        }
        else
        {
            if ( _previous?.Fragment is null )
            {
                _runStart = part.Offset;
            }

            _run.Add( new( identity, page.Slice( part.Offset, part.Glue ), part.Offset + part.Length ) );
        }

        _previous = part;
        _previousIdentity = identity;
        var ownLength = part.Length - part.Glue;
        _previousTextDictionary = part.Fragment is null
            ? page.Slice( part.Offset + part.Glue + Math.Max( 0, ownLength - WindowBytes ), Math.Min( ownLength, WindowBytes ) )
            : default;
    }
}
