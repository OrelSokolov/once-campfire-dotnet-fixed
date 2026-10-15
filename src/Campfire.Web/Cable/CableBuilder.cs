using System.Diagnostics.CodeAnalysis;
using Campfire.Application.Security;

namespace Campfire.Web.Cable;

/// <summary>Registers channel classes with the cable server.</summary>
public sealed class CableBuilder
{
    internal CableBuilder( IServiceCollection services, CableChannelRegistry registry )
    {
        Services = services;
        Registry = registry;
    }

    public IServiceCollection Services { get; }

    internal CableChannelRegistry Registry { get; }

    /// <summary>
    /// Registers <typeparamref name="TChannel"/> under its Ruby class name. Each subscription gets
    /// a new instance from the root service provider, so a channel takes singletons (for example
    /// <c>IServiceScopeFactory</c>) and opens its own scope for database work. The annotation keeps
    /// the channel's public constructors, which <see cref="ActivatorUtilities"/> looks up, under
    /// trimming and Native AOT.
    /// </summary>
    public CableBuilder AddChannel<[DynamicallyAccessedMembers( DynamicallyAccessedMemberTypes.PublicConstructors )] TChannel>( string className )
        where TChannel : CableChannel
    {
        var factory = ActivatorUtilities.CreateFactory<TChannel>( [] );
        Registry.Add( className, services => factory( services, null ) );

        return this;
    }

    /// <summary>Registers a channel class name with an explicit factory.</summary>
    public CableBuilder AddChannel( string className, Func<IServiceProvider, CableChannel> factory )
    {
        Registry.Add( className, factory );

        return this;
    }

    /// <summary>
    /// Registers <c>Turbo::StreamsChannel</c>, verifying signed stream names with the application's
    /// <see cref="TurboStreamNames"/>. Campfire passes <c>RoomStreamsAreAuthorized.IsGuardedStream</c> as the guard.
    /// </summary>
    public CableBuilder AddTurboStreamsChannel( Func<string, bool>? guard = null )
    {
        Registry.Add( TurboStreamsChannel.ClassName, services =>
        {
            var names = services.GetRequiredService<TurboStreamNames>( );

            return new TurboStreamsChannel( names.Verify, guard );
        } );

        return this;
    }
}
