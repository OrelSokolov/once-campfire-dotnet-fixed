using System.Text;

namespace Campfire.Web.Views.Rendering;

/// <summary>Immutable shared UTF-8 markup. Cached fragments contain no viewer credentials or tokens.</summary>
public sealed class CachedFragment
{
    private readonly byte[] _bytes;
    internal ReadOnlySpan<byte> Bytes => _bytes;
    internal long RetainedBytes => _bytes.LongLength + 32;
    internal ContentDigest Digest { get; }
    internal DateTimeOffset ValidUntil { get; set; } = DateTimeOffset.MaxValue;
    public string Html => Encoding.UTF8.GetString( _bytes );
    internal CachedFragment( SafeHtml html )
        : this( Encoding.UTF8.GetBytes( html.Html ) )
    {
    }

    /// <summary>Takes the renderer's owned array; pooled response buffers must be copied before transfer.</summary>
    internal CachedFragment( byte[] bytes )
    {
        _bytes = bytes;
        Digest = ContentDigest.Of( _bytes );
    }
}
