using Campfire.Application.Storage;

namespace Campfire.Web.App;

/// <summary>Named variants of attached images: an avatar's <c>:square</c>, the logo's <c>:large</c> and <c>:small</c>.</summary>
public static class AttachedImages
{
    /// <summary>
    /// <c>record.attachment.variant(transformations).processed if attachment.variable?</c>: the variant's image blob,
    /// made (outside the writer lease) and recorded on first use; <c>null</c> when nothing is attached or the
    /// attachment can't be transformed.
    /// </summary>
    public static async Task<Blob?> ProcessedVariantAsync(
        CampfireApp app,
        AttachmentOwner owner,
        string name,
        Variation transformations,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( app );

        var blob = await app.Database.ReadAsync( db => BlobRecords.Attached( db, owner, name ), cancellationToken );
        if ( blob is null || !blob.IsVariable )
        {
            return null;
        }

        return await app.Storage.ProcessVariantAsync( blob, BlobStorage.VariationFor( blob, transformations ), cancellationToken );
    }
}
