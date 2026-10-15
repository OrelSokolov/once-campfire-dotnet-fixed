using Campfire.Application.Accounts;
using Campfire.Application.Data;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Endpoints.Users;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.FirstRuns;

/// <summary>
/// <c>FirstRunsController</c> (<c>app/controllers/first_runs_controller.rb</c>): the account, its first administrator
/// and the first room, set up once.
/// </summary>
public static partial class FirstRunsEndpoints
{
    private static Task<FrontendResult> ShowAsync( RequestContext requestContext ) =>
        Pages.PageAsync( requestContext, viewContext => Views.first_runs.show.Create( new FirstRunPage( viewContext ) ) );

    /// <summary>
    /// <c>FirstRun.create!(user_params)</c>, <c>start_new_session_for user</c>, <c>redirect_to root_url</c>. Two first runs
    /// racing both pass <c>prevent_repeats</c>; the writer lease runs them one after the other, and the second's
    /// <c>Account.create!</c> hits the singleton guard: <c>rescue ActiveRecord::RecordNotUnique</c> sends it to the root
    /// without a session.
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var (attributes, avatarAssignment) = UserParams.PermitNewUser( requestContext );
        using var avatar = await StagedAssignment.StageAsync( app, avatarAssignment, requestContext.RequestAborted );
        var withAvatar = attributes.WithAvatar( avatar );

        User administrator;
        try
        {
            using var lease = await app.Database.AcquireWriterAsync( requestContext.RequestAborted );
            administrator = FirstRun.Create( lease, withAvatar );
        }
        catch ( RecordNotUniqueException )
        {
            return requestContext.RedirectTo( requestContext.UrlFor( Routes.Root ) );
        }

        await requestContext.StartNewSessionForAsync( administrator );
        return requestContext.RedirectTo( requestContext.UrlFor( Routes.Root ) );
    }

    /// <summary><c>redirect_to root_url if Account.any?</c>.</summary>
    private static async ValueTask<FrontendResult?> PreventRepeatsAsync( RequestContext requestContext )
    {
        var database = requestContext.Database( );
        var any = await database.ReadAsync( db => AccountQueries.First( db ) is not null, requestContext.RequestAborted );
        return any ? requestContext.RedirectTo( requestContext.UrlFor( Routes.Root ) ) : null;
    }
}
