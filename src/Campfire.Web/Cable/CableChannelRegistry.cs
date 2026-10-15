namespace Campfire.Web.Cable;

/// <summary>
/// The channel classes a client can subscribe to, by Ruby class name (what the client puts in the
/// identifier's <c>channel</c>). Rails constantizes the name and accepts any
/// <c>ActionCable::Channel::Base</c> subclass; here every such class is registered explicitly.
/// </summary>
public sealed class CableChannelRegistry
{
    private readonly Dictionary<string, Func<IServiceProvider, CableChannel>> _factories = new( StringComparer.Ordinal );

    /// <summary>Registers (or replaces) the factory for <paramref name="className"/>; each subscription gets a new instance.</summary>
    public void Add( string className, Func<IServiceProvider, CableChannel> factory )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( className );
        ArgumentNullException.ThrowIfNull( factory );

        _factories[Normalize( className )] = factory;
    }

    /// <summary>The factory for a client-supplied class name, or <c>null</c> ("Subscription class not found").</summary>
    public Func<IServiceProvider, CableChannel>? Find( string className ) =>
        _factories.GetValueOrDefault( Normalize( className ) );

    /// <summary><c>safe_constantize</c> resolves <c>::RoomChannel</c> to <c>RoomChannel</c>.</summary>
    internal static string Normalize( string className ) =>
        className.StartsWith( "::", StringComparison.Ordinal ) ? className[2..] : className;
}
