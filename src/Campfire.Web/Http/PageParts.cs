using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Http;

/// <summary>Contiguous byte ranges, preserving cached-fragment identity and the small gap before it.</summary>
internal static class PageParts
{
    internal const int MaximumGlueBytes = 256;

    internal static IReadOnlyList<PagePart> Split( PooledBytesBody body ) => Split( body.Bytes.Length, body.Fragments );

    internal static IReadOnlyList<PagePart> Split( int length, IReadOnlyList<RecordedFragment> fragments )
    {
        var parts = new List<PagePart>( fragments.Count + 2 );
        var position = 0;
        foreach ( var recorded in fragments )
        {
            if ( recorded.Offset < position || recorded.End > length )
            {
                throw new InvalidOperationException( "A fragment is outside its recorded page." );
            }

            var gap = recorded.Offset - position;
            var glue = parts.Count > 0 && parts[^1].Fragment is not null && gap <= MaximumGlueBytes ? gap : 0;
            if ( gap > glue )
            {
                parts.Add( new PagePart( position, gap, null, 0 ) );
            }

            parts.Add( new PagePart( recorded.Offset - glue, recorded.End - recorded.Offset + glue, recorded.Fragment, glue ) );
            position = recorded.End;
        }

        if ( position < length )
        {
            parts.Add( new PagePart( position, length - position, null, 0 ) );
        }

        return parts;
    }
}
