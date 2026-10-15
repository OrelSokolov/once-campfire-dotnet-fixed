using Campfire.Application.Data;
using Campfire.Application.RichText;
using Campfire.Application.Security;
using Campfire.Application.Storage;
using Campfire.Web.Assets;
using Campfire.Web.Cable;
using Campfire.Web.Channels;
using Campfire.Web.Hosting;
using Campfire.Web.Http;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Caching;
using Campfire.Web.Workers;

namespace Campfire.Web.App;

/// <summary>
/// What outlives a request and every controller, channel and job shares: the database, storage, the cable server
/// and the app's broadcasts, the signers, the fragment store, the view contexts and the job queues. Actions reach it
/// with <see cref="CampfireAppAccess.App(RequestContext)"/> and build view contexts with
/// <see cref="CampfireAppAccess.Views(RequestContext)"/>; channels and job handlers take it
/// by constructor injection.
/// </summary>
public sealed class CampfireApp(
    CampfireOptions options,
    StoragePaths paths,
    TimeProvider clock,
    CampfireDatabase database,
    DatabaseGeneration databaseGeneration,
    BlobStorage storage,
    CableServer cable,
    SignedIds signedIds,
    SignedGlobalIds signedGlobalIds,
    TurboStreamNames streamNames,
    FragmentCache fragments,
    ActiveStorageVerifier storageVerifier,
    RoomBroadcasts broadcasts,
    JobChannels jobs,
    AssetPaths assetPaths )
{
    public CampfireOptions Options { get; } = options;

    public StoragePaths Paths { get; } = paths;

    public TimeProvider Clock { get; } = clock;

    public CampfireDatabase Database { get; } = database;

    public BlobStorage Storage { get; } = storage;

    public CableServer Cable { get; } = cable;

    /// <summary><c>signed_id</c>: avatar tokens and transfer ids.</summary>
    public SignedIds SignedIds { get; } = signedIds;

    /// <summary>SGIDs: <c>attachable_sgid</c> for mentions.</summary>
    public SignedGlobalIds SignedGlobalIds { get; } = signedGlobalIds;

    /// <summary><c>Turbo::StreamsChannel.signed_stream_name</c>.</summary>
    public TurboStreamNames StreamNames { get; } = streamNames;

    /// <summary>The in-process fragment store (<c>Rails.cache</c> for views).</summary>
    public FragmentCache Fragments { get; } = fragments;

    public DatabaseGeneration DatabaseGeneration { get; } = databaseGeneration;

    /// <summary><c>Rails.application.message_verifier("ActiveStorage")</c>: signed blob ids, variation keys, disk URLs.</summary>
    public ActiveStorageVerifier StorageVerifier { get; } = storageVerifier;

    /// <summary>The Turbo streams and Action Cable broadcasts the app makes.</summary>
    public RoomBroadcasts Broadcasts { get; } = broadcasts;

    /// <summary>The job queues (<c>perform_later</c>).</summary>
    public JobChannels Jobs { get; } = jobs;

    /// <summary><c>asset_path(logical)</c>: the digested path of an asset.</summary>
    public string AssetPath( string logicalPath ) => assetPaths.AssetPath( logicalPath );

    /// <summary><c>Time.current</c> on the business clock.</summary>
    public DateTimeOffset Now => Clock.GetUtcNow( );

    /// <summary>
    /// A rich text context for rendering on <paramref name="db"/> (a leased connection): mentions resolve on the same
    /// connection, never another one, which could deadlock the lease pool. <paramref name="requestHost"/> is
    /// <c>Current.request_host</c> (<see langword="null"/> outside a request), which link previews may not point back
    /// at. A message's cached fragment depends on it, so with a configured public origin its host stands in for the
    /// request's: a forged <c>Host</c> can't change what the fragment keyed on that origin holds (configured public origin).
    /// </summary>
    public RichTextContext RichText( CampfireDb db, string? requestHost ) =>
        new( new DbAttachableResolver( db, this ), requestHost is null ? null : Options.PublicHost ?? requestHost );
}
