using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Http;

/// <summary>A <see cref="GzipHistory"/> copied for the store, compared byte for byte on lookup.</summary>
internal sealed record GzipStoredHistory( GzipHistoryKind Kind, ContentDigest Identity, GzipStoredHistoryPart[] Parts, byte[] TextDictionary )
{
    internal long TextDictionaryCharge => TextDictionary.Length == 0 ? 0 : 32L + TextDictionary.Length;
    internal long Cost => Parts.Sum( part => 96L + part.Glue.Length ) + TextDictionaryCharge;

    internal bool Fits( GzipHistory history )
    {
        if ( Kind != history.Kind
            || Identity != history.Identity
            || Parts.Length != history.Parts.Length
            || (Kind == GzipHistoryKind.Text && !history.TextDictionary.SequenceEqual( TextDictionary )) )
        {
            return false;
        }

        for ( var index = 0; index < Parts.Length; index++ )
        {
            if ( Parts[index].Identity != history.Parts[index].Identity || !history.Parts[index].Glue.Span.SequenceEqual( Parts[index].Glue ) )
            {
                return false;
            }
        }

        return true;
    }
}
