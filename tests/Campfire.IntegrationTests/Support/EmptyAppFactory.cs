namespace Campfire.IntegrationTests.Support;

/// <summary>An empty database, as a fresh install has before its first run.</summary>
public sealed class EmptyAppFactory : SliceAppFactory
{
    public EmptyAppFactory( )
        : base( loadDatabase: null )
    {
    }
}
