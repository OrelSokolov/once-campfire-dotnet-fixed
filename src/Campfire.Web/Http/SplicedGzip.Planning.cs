using System.IO.Hashing;
using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Http;

internal sealed partial class SplicedGzip
{
    private const int OneOffWindowBytes = 8 * 1024;

    private readonly record struct PlannedPiece(
        ContentDigest Identity,
        GzipPiece? Piece,
        GzipAdmission Admission,
        int DictionaryStart,
        bool Continuous );

    /// <summary>Look up and record every key before deciding fallback. No payload can enter a store during planning.</summary>
    private long Plan(
        PooledBytesBody body,
        IReadOnlyList<PagePart> parts,
        PlannedPiece[] planned,
        GzipRouteMetrics.Counts counts,
        CancellationToken cancellationToken )
    {
        XxHash128? hash = null;
        var run = new GzipRunHistory( );
        var encoderStart = -1;
        var encoderEnd = -1;
        long oneOffWork = 0;
        for ( var partIndex = 0; partIndex < parts.Count; partIndex++ )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            var part = parts[partIndex];
            var bytes = part.Bytes( body.Bytes.Span );
            var identity = part.Fragment?.Digest ?? GzipKeyHash.TextIdentity( bytes );
            var history = run.Before( part );
            var glue = bytes[..part.Glue];
            var piece = _store.Find( part.Fragment, identity, history, bytes, glue );
            GzipAdmission admission;
            if ( piece is not null )
            {
                admission = GzipAdmission.Hit;
            }
            else
            {
                hash ??= new XxHash128( );
                var key = GzipKeyHash.Piece( hash, part.Fragment is not null, identity, history, glue );
                admission = _doorkeeper.Seen( key ) ? GzipAdmission.Admit : GzipAdmission.OneOff;
            }

            counts.Add( admission );
            var dictionaryStart = run.DictionaryStart;
            var continuous = false;
            if ( admission != GzipAdmission.Hit )
            {
                if ( admission == GzipAdmission.OneOff )
                {
                    // Unstored output only needs to decode in this response, so real preceding bytes are safe.
                    continuous = encoderEnd == part.Offset;
                    dictionaryStart = Math.Max( 0, part.Offset - OneOffWindowBytes );
                    oneOffWork += part.Length + (continuous ? 0 : part.Offset - dictionaryStart);
                }
                else
                {
                    // An admitted piece must never see bytes outside its exact keyed history.
                    continuous = history.Kind is GzipHistoryKind.RunStart or GzipHistoryKind.RunCut
                        && encoderEnd == part.Offset
                        && Math.Max( encoderStart, part.Offset - WindowBytes ) == dictionaryStart;
                }

                if ( !continuous )
                {
                    encoderStart = dictionaryStart;
                }

                encoderEnd = part.Offset + part.Length;
            }

            planned[partIndex] = new( identity, piece, admission, dictionaryStart, continuous );
            run.Advance( part, identity, body.Bytes );
        }

        return oneOffWork;
    }
}
