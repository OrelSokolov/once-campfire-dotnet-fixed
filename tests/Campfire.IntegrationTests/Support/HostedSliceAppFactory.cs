using System.Globalization;
using Campfire.Web.Workers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// The slice app with its host at hand, so a test can stop it (and read the database) before disposing it;
/// <paramref name="drainTimeout"/>, when given, is the workers' drain deadline on stop.
/// </summary>
internal sealed class HostedSliceAppFactory( TimeSpan? drainTimeout = null ) : SliceAppFactory
{
    public IHost? Host { get; private set; }

    protected override void ConfigureWebHost( IWebHostBuilder builder )
    {
        base.ConfigureWebHost( builder );
        if ( drainTimeout is { } timeout )
        {
            var drainTimeoutKey = $"{WorkerOptions.SectionName}:{nameof( WorkerOptions.DrainTimeout )}";
            builder.UseSetting( drainTimeoutKey, timeout.ToString( "c", CultureInfo.InvariantCulture ) );
        }
    }

    protected override IHost CreateHost( IHostBuilder builder )
    {
        Host = base.CreateHost( builder );
        return Host;
    }
}
