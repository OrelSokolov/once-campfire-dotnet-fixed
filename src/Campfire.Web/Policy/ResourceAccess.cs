using Campfire.Application.Data;
using Campfire.Web.Http;

namespace Campfire.Web.Policy;

/// <summary>Explicit resource access checks shared by endpoint modules. No callback interpreter.</summary>
public static class ResourceAccess
{
    public static async ValueTask<FrontendResult?> SetRoom( RequestContext requestContext, long roomId )
    {
        var user = requestContext.CurrentUser( );
        if ( user is null )
        {
            return requestContext.Head( StatusCodes.Status401Unauthorized );
        }

        var scope = requestContext.HttpContext.RequestServices.GetRequiredService<IRoomScope>( );
        var membership = await scope.FindMembershipByRoomAsync( user, roomId, requestContext.RequestAborted );
        if ( membership is null )
        {
            throw new RecordNotFoundException( ModelNames.Membership );
        }

        requestContext.SetCurrent( new CurrentMembership( membership ) );
        return null;
    }

    public static FrontendResult Forbidden( RequestContext requestContext ) => requestContext.Head( StatusCodes.Status403Forbidden );
}
