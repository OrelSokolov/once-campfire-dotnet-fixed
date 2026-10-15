using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Http;

/// <summary>
/// A contiguous range of a rendered page: a cached fragment (with <paramref name="Glue"/> bytes of the gap before it)
/// or the text between fragments.
/// </summary>
internal readonly record struct PagePart( int Offset, int Length, CachedFragment? Fragment, int Glue )
{
    internal ReadOnlySpan<byte> Bytes( ReadOnlySpan<byte> body ) => body.Slice( Offset, Length );

    internal ReadOnlySpan<byte> Dictionary( ReadOnlySpan<byte> body ) => Fragment is { } fragment ? fragment.Bytes : Bytes( body );
}
