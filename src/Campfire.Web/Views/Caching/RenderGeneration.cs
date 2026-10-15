namespace Campfire.Web.Views.Caching;

/// <summary>Capture before authentication and application reads; never replace a delayed render's namespace.</summary>
public sealed class RenderGeneration( RequestDelegate next )
{
    public Task InvokeAsync( HttpContext httpContext )
    {
        if ( IsRequired( httpContext ) )
        {
            httpContext.Items[typeof( RenderGeneration )] = httpContext.RequestServices.GetService<DatabaseGeneration>( )?.Capture( );
        }

        return next( httpContext );
    }

    internal static bool IsRequired( HttpContext httpContext ) => httpContext.GetEndpoint( )?.Metadata.GetMetadata<FragmentRender>( ) is not null;

    internal static long? Of( HttpContext httpContext ) => httpContext.Items.TryGetValue( typeof( RenderGeneration ), out var value )
        ? value as long?
        : throw new InvalidOperationException( "Fragment rendering requires a generation captured before application reads." );
}
