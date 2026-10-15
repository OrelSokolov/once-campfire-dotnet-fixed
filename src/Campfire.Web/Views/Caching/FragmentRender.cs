namespace Campfire.Web.Views.Caching;

/// <summary>Endpoints whose output can contain cached fragments opt into a pre-read observation.</summary>
public sealed class FragmentRender
{
    public static FragmentRender Instance { get; } = new( );

    private FragmentRender( )
    {
    }
}
