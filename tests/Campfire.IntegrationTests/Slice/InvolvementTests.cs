using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// <c>Rooms::InvolvementsController</c> (<c>test/controllers/rooms/involvements_controller_test.rb</c>): the bell and its
/// sidebar broadcasts.
/// </summary>
public sealed class InvolvementTests( SharedSliceApp shared ) : IClassFixture<SharedSliceApp>
{
    private static readonly long Watercooler = SliceAppFactory.Id( "watercooler" );

    [Fact]
    public async Task The_bell_shows_the_involvement_and_offers_the_next_one( )
    {
        var client = await shared.SignedInAsync( "david@37signals.com" );
        var designers = SliceAppFactory.Id( "designers" );
        using var framed = new HttpRequestMessage( HttpMethod.Get, Routes.RoomInvolvement( designers ) );
        framed.Headers.Add( "Turbo-Frame", $"involvement_rooms_closed_{designers}" );

        var page = await client.GetAsync( Routes.RoomInvolvement( designers ) );
        var frame = await client.SendAsync( framed, "text/html" );

        Assert.Equal( (HttpStatusCode.OK, HttpStatusCode.OK), (page.Status, frame.Status) );
        Assert.Contains( "class=\"btn mentions\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( $"action=\"/rooms/{designers}/involvement?involvement=everything\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( "rel=\"stylesheet\"", page.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "rel=\"stylesheet\"", frame.Body, StringComparison.Ordinal );
        Assert.Contains( $"id=\"involvement_rooms_closed_{designers}\"", frame.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Going_invisible_leaves_the_sidebar_and_coming_back_prepends_the_room( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Watercooler ) );
        using var taps = StreamTaps.On( factory, StreamTaps.UserRooms( "david" ) );
        factory.Events.Clear( );

        var hidden = await client.SubmitFormAsync( HttpMethod.Put, Routes.RoomInvolvement( Watercooler ), token, [("involvement", "invisible")] );
        var hiddenAt = Involvement( factory );
        var shown = await client.SubmitFormAsync( HttpMethod.Put, Routes.RoomInvolvement( Watercooler ), token, [("involvement", "everything")] );

        Assert.Equal( $"{factory.BaseAddress}rooms/{Watercooler}/involvement", hidden.Location );
        Assert.Equal( ("invisible", "everything"), (hiddenAt, Involvement( factory )) );
        Assert.Equal( [$"stream david remove list_rooms_closed_{Watercooler}", "stream david prepend shared_rooms"], factory.Events.Only( "stream" ) );
        Assert.Equal( HttpStatusCode.Found, shown.Status );
        client.Dispose( );
    }

    [Theory]
    [InlineData( "watercooler", "david_watercooler", "mentions" )]
    [InlineData( "david_and_jason", "david_david_and_jason", "nothing" )]
    public async Task Changes_between_visible_involvements_and_any_change_in_a_ping_broadcast_nothing( string room, string membership, string involvement )
    {
        using var factory = new SliceAppFactory( );
        var roomId = SliceAppFactory.Id( room );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( roomId ) );
        using var taps = StreamTaps.On( factory, StreamTaps.UserRooms( "david" ) );

        var reply = await client.SubmitFormAsync( HttpMethod.Put, Routes.RoomInvolvement( roomId ), token, [("involvement", involvement)] );

        Assert.Equal( HttpStatusCode.Found, reply.Status );
        Assert.Equal( involvement, factory.Scalar<string>( $"SELECT involvement FROM memberships WHERE id = {SliceAppFactory.Id( membership )}" ) );
        Assert.Empty( taps.Payloads );
        client.Dispose( );
    }

    [Fact]
    public async Task An_unknown_involvement_fails_and_changes_nothing( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Watercooler ) );

        var reply = await client.SubmitFormAsync( HttpMethod.Put, Routes.RoomInvolvement( Watercooler ), token, [("involvement", "loud")] );

        Assert.Equal( HttpStatusCode.InternalServerError, reply.Status );
        Assert.Equal( "everything", Involvement( factory ) );
        client.Dispose( );
    }

    [Fact]
    public async Task A_blank_involvement_is_stored_as_null_and_then_neither_the_bell_nor_a_second_change_can_render( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Watercooler ) );

        var cleared = await client.SubmitFormAsync( HttpMethod.Put, Routes.RoomInvolvement( Watercooler ), token, [("involvement", "")] );
        var bell = await client.GetAsync( Routes.RoomInvolvement( Watercooler ) );
        var again = await client.SubmitFormAsync( HttpMethod.Put, Routes.RoomInvolvement( Watercooler ), token, [("involvement", "mentions")] );

        Assert.Equal( HttpStatusCode.Found, cleared.Status );
        Assert.Equal( HttpStatusCode.InternalServerError, bell.Status );
        // involvement_previously_was.inquiry on nil raises after the update committed.
        Assert.Equal( HttpStatusCode.InternalServerError, again.Status );
        Assert.Equal( "mentions", Involvement( factory ) );
        client.Dispose( );
    }

    private static string? Involvement( SliceAppFactory factory ) =>
        factory.Scalar<string>( $"SELECT involvement FROM memberships WHERE id = {SliceAppFactory.Id( "david_watercooler" )}" );
}
