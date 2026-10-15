using Campfire.Application.Storage;

namespace Campfire.IntegrationTests.Support;

/// <summary>Blobs already in storage before a request names them, as an earlier upload leaves them.</summary>
internal static class StoredBlobs
{
    /// <summary>A blob made by <c>ActiveStorage::Blob.create_and_upload!</c>, and its signed id.</summary>
    public static async Task<(long Id, string SignedId)> CreateAsync( SliceAppFactory factory, string filename, byte[] bytes, string contentType )
    {
        ArgumentNullException.ThrowIfNull( factory );

        using var content = new MemoryStream( bytes );
        var blob = await factory.App.Storage.CreateAndUploadAsync( content, new BlobFilename( filename ), contentType, SliceClient.Cancellation );
        return (blob.Id, factory.App.StorageVerifier.SignedBlobId( blob.Id ));
    }
}
