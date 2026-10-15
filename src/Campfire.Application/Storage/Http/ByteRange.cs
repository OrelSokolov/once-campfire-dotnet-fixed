namespace Campfire.Application.Storage.Http;

/// <summary>An inclusive byte range, <c>start..end</c>.</summary>
public readonly record struct ByteRange( long Start, long End )
{
    public long Length => End - Start + 1;
}
