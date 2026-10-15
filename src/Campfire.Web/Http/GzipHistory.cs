using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Http;

/// <summary>A request borrows history from its page; stored text dictionaries require an exact byte check.</summary>
internal readonly ref struct GzipHistory(
    GzipHistoryKind kind,
    ContentDigest identity,
    ReadOnlySpan<GzipHistoryPart> parts,
    ReadOnlySpan<byte> textDictionary = default )
{
    internal GzipHistoryKind Kind { get; } = kind;
    internal ContentDigest Identity { get; } = identity;
    internal ReadOnlySpan<GzipHistoryPart> Parts { get; } = parts;
    internal ReadOnlySpan<byte> TextDictionary { get; } = textDictionary;
    internal GzipTextHistory TextKey => new( Kind, Identity );

    internal GzipStoredHistory Copy( )
    {
        var copied = new GzipStoredHistoryPart[Parts.Length];
        for ( var index = 0; index < copied.Length; index++ )
        {
            copied[index] = new( Parts[index].Identity, Parts[index].Glue.ToArray( ) );
        }

        return new GzipStoredHistory( Kind, Identity, copied, Kind == GzipHistoryKind.Text ? TextDictionary.ToArray( ) : [] );
    }
}
