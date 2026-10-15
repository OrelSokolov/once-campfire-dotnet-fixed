namespace Campfire.Web.Http;

/// <summary>A cached piece with the exact history and glue it was compressed after.</summary>
internal sealed record GzipStoredPiece( GzipStoredHistory History, byte[] Glue, GzipPiece Piece )
{
    // Text-predecessor dictionaries are retained for exact comparison; charge every copied array and bookkeeping.
    internal long Cost => 512L + Glue.Length + Piece.Bytes.Length + History.Cost;

    internal bool Fits( GzipHistory history, ReadOnlySpan<byte> glue ) => glue.SequenceEqual( Glue ) && History.Fits( history );
}
