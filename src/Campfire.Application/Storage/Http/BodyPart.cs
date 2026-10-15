namespace Campfire.Application.Storage.Http;

/// <summary>One piece of a response body: literal bytes, or a byte range of a file the caller streams.</summary>
public abstract record BodyPart
{
    private protected BodyPart( )
    {
    }

    public abstract long Length { get; }
}

/// <summary>Literal bytes (a multipart heading, an error message).</summary>
public sealed record BytesPart( byte[] Bytes ) : BodyPart
{
    public override long Length => Bytes.Length;
}

/// <summary><paramref name="Length"/> bytes of the file at <paramref name="Path"/>, from <paramref name="Offset"/>.</summary>
public sealed record FilePart( string Path, long Offset, long Length ) : BodyPart
{
    public override long Length { get; } = Length;
}
