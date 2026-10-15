using Campfire.Web.Http;
using Campfire.Web.Policy;

namespace Campfire.Web.Endpoints;

public static class Authorization
{
    public static ValueTask<FrontendResult?> EnsureCanAdministerAccountAsync( RequestContext requestContext ) =>
        ValueTask.FromResult( requestContext.CurrentUserRecord( )?.CanAdminister( ) == true ? null : ResourceAccess.Forbidden( requestContext ) );
}
