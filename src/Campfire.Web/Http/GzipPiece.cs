namespace Campfire.Web.Http;

/// <summary>A sync-flushed deflate block and the CRC-32 of the bytes it encodes.</summary>
internal sealed record GzipPiece( byte[] Bytes, uint Crc );
