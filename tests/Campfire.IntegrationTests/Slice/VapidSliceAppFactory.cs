using Campfire.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;

namespace Campfire.IntegrationTests.Slice;

/// <summary>The slice app with the test VAPID keys of <c>parity/.env.reference</c>, so pushes can be signed.</summary>
internal sealed class VapidSliceAppFactory : SliceAppFactory
{
    protected override void ConfigureWebHost( IWebHostBuilder builder )
    {
        base.ConfigureWebHost( builder );
        builder.UseSetting( "VAPID_PUBLIC_KEY", PushSubscriber.VapidPublicKey );
        builder.UseSetting( "VAPID_PRIVATE_KEY", PushSubscriber.VapidPrivateKey );
    }
}
