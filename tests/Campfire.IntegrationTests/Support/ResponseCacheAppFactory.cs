using System.Globalization;
using Campfire.Web.Hosting;
using Microsoft.AspNetCore.Hosting;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// The app with a whole-response reuse budget of <paramref name="megabytes"/> (<c>CAMPFIRE_RESPONSE_CACHE_MB</c>); zero
/// turns reuse off, so the same test shows a page with and without it.
/// </summary>
internal class ResponseCacheAppFactory( int megabytes ) : SliceAppFactory
{
    protected override void ConfigureWebHost( IWebHostBuilder builder )
    {
        base.ConfigureWebHost( builder );
        builder.UseSetting( CampfireOptions.ResponseCacheVariable, megabytes.ToString( CultureInfo.InvariantCulture ) );
    }
}
