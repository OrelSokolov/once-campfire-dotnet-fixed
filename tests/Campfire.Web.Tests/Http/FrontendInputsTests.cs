using Campfire.Web.Http.Inputs;
using Microsoft.AspNetCore.Http;

namespace Campfire.Web.Tests.Http;

public sealed class FrontendInputsTests
{
    [Fact]
    public async Task Platform_fields_keep_query_and_form_separate_and_route_values_cannot_be_overridden( )
    {
        var httpContext = new DefaultHttpContext( );
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Body = new MemoryStream( "message%5Bbody%5D=hello&room_id=99"u8.ToArray( ) );
        httpContext.Request.QueryString = new QueryString( "?q=coffee&room_id=88" );
        httpContext.Request.RouteValues["room_id"] = 42L;
        var fields = await FrontendInputs.ReadAsync( httpContext );
        Assert.Equal( "hello", fields.Form.ValueAtPath( "message", "body" )?.AsString );
        Assert.Null( fields.Query["message"] );
        Assert.Equal( "coffee", fields.Query.GetString( "q" ) );
        Assert.Null( fields.Form["q"] );
        // Typed route arguments are supplied independently; input fields do not write them back.
        Assert.Equal( "99", fields.Combined.GetString( "room_id" ) );
        Assert.Equal( 42L, httpContext.Request.RouteValues["room_id"] );
    }

    [Theory]
    [InlineData( "x=one&x=two" )]
    [InlineData( "x=one&x%5By%5D=two" )]
    [InlineData( "x%5B=one" )]
    [InlineData( "x%5D=one" )]
    [InlineData( "x%5B%5D%5By%5D=one" )]
    [InlineData( "x%5Ba%5D%5Bb%5D%5Bc%5D%5Bd%5D=one" )]
    public async Task Ambiguous_or_unbounded_frontend_fields_are_rejected( string query )
    {
        var httpContext = new DefaultHttpContext( );
        httpContext.Request.QueryString = new QueryString( "?" + query );
        var error = await Assert.ThrowsAsync<BadHttpRequestException>( ( ) => FrontendInputs.ReadAsync( httpContext ) );
        Assert.Equal( StatusCodes.Status400BadRequest, error.StatusCode );
    }

    [Fact]
    public async Task Repeated_selected_array_fields_retain_order( )
    {
        var httpContext = new DefaultHttpContext( );
        httpContext.Request.QueryString = new QueryString( "?room%5Buser_ids%5D%5B%5D=2&room%5Buser_ids%5D%5B%5D=1" );
        var fields = await FrontendInputs.ReadAsync( httpContext );
        var members = Assert.IsType<InputList>( fields.Query.ValueAtPath( "room", "user_ids" ) );
        Assert.Equal( ["2", "1"], members.Select( value => value.AsString ) );
    }
}
