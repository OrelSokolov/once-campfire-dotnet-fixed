
namespace Campfire.Application.Storage.Http;

/// <summary>
/// The Active Storage route helpers (<c>activestorage/config/routes.rb</c>). <c>rails_blob_path</c> and
/// <c>url_for(representation)</c> resolve through <c>resolve_model_to_route = :rails_storage_redirect</c>;
/// <c>rails_storage_proxy_path</c> gives the proxy forms. Campfire leaves <c>urls_expire_in</c> unset,
/// so the signed blob ids in these paths never expire.
/// </summary>
public static class StorageRoutes
{
    public const string Prefix = "/rails/active_storage";

    /// <summary><c>rails_blob_path(blob, disposition:)</c>: <c>/rails/active_storage/blobs/redirect/:signed_id/*filename</c>.</summary>
    public static string BlobRedirectPath( ActiveStorageVerifier verifier, Blob blob, string? disposition = null ) =>
        BlobPath( "redirect", verifier, blob, disposition );

    /// <summary><c>rails_storage_proxy_path(blob, disposition:)</c>: <c>/rails/active_storage/blobs/proxy/:signed_id/*filename</c>.</summary>
    public static string BlobProxyPath( ActiveStorageVerifier verifier, Blob blob, string? disposition = null ) =>
        BlobPath( "proxy", verifier, blob, disposition );

    /// <summary>
    /// <c>url_for(variant)</c> or <c>url_for(preview)</c>:
    /// <c>/rails/active_storage/representations/redirect/:signed_blob_id/:variation_key/*filename</c>.
    /// <paramref name="blob"/> is the represented blob (the video, for a preview) and
    /// <paramref name="variation"/> exactly the one the representation holds (defaulted for a
    /// variant, as given for a preview).
    /// </summary>
    public static string RepresentationRedirectPath( ActiveStorageVerifier verifier, Blob blob, Variation variation ) =>
        RepresentationPath( "redirect", verifier, blob, variation );

    /// <summary><c>rails_storage_proxy_path(representation)</c>.</summary>
    public static string RepresentationProxyPath( ActiveStorageVerifier verifier, Blob blob, Variation variation ) =>
        RepresentationPath( "proxy", verifier, blob, variation );

    private static string BlobPath( string kind, ActiveStorageVerifier verifier, Blob blob, string? disposition )
    {
        ArgumentNullException.ThrowIfNull( verifier );
        ArgumentNullException.ThrowIfNull( blob );

        var signedId = ContentDisposition.EscapeSegment( verifier.SignedBlobId( blob.Id ) );
        var path = $"{Prefix}/blobs/{kind}/{signedId}/{ContentDisposition.EscapePath( blob.Filename.Sanitized )}";

        // Extra route options become the query string through Hash#to_query (CGI.escape).
        return disposition is null ? path : $"{path}?disposition={Uri.EscapeDataString( disposition )}";
    }

    private static string RepresentationPath( string kind, ActiveStorageVerifier verifier, Blob blob, Variation variation )
    {
        ArgumentNullException.ThrowIfNull( verifier );
        ArgumentNullException.ThrowIfNull( blob );
        ArgumentNullException.ThrowIfNull( variation );

        var signedId = ContentDisposition.EscapeSegment( verifier.SignedBlobId( blob.Id ) );
        var key = ContentDisposition.EscapeSegment( variation.Key( verifier ) );

        return $"{Prefix}/representations/{kind}/{signedId}/{key}/{ContentDisposition.EscapePath( blob.Filename.Sanitized )}";
    }
}
