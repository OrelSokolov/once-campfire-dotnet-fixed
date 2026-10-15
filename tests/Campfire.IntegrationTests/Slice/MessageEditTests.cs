using System.Net;
using System.Text.Json.Nodes;
using Campfire.Application.Rooms;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Channels;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// <c>MessagesController#show</c>, <c>edit</c>, <c>update</c> and <c>destroy</c> (<c>test/controllers/messages_controller_test.rb</c>),
/// with the operation's update and destroy sequences: the write, the search index after commit, then the room's broadcast.
/// </summary>
public sealed class MessageEditTests( SharedSliceApp shared ) : IClassFixture<SharedSliceApp>
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );
    private static readonly long Second = SliceAppFactory.Id( "second" );
    private static readonly long First = SliceAppFactory.Id( "first" );
    private static readonly long Fourth = SliceAppFactory.Id( "fourth" );

    /// <summary>The edit page, the update and the destroy of Jason's message in Designers.</summary>
    public static TheoryData<string, string> ChangesToSomeoneElsesMessage => new( )
    {
        { "GET", Routes.EditRoomMessage( Designers, First ) },
        { "PUT", Routes.RoomMessage( Designers, First ) },
        { "DELETE", Routes.RoomMessage( Designers, First ) },
    };

    /// <summary>A watercooler message under the Designers path, and under the short path.</summary>
    public static TheoryData<string, HttpStatusCode> PathsToAWatercoolerMessage => new( )
    {
        { Routes.RoomMessage( Designers, Fourth ), HttpStatusCode.NotFound },
        { Routes.Message( Fourth ), HttpStatusCode.OK },
    };

    [Fact]
    public async Task Show_renders_the_message_in_the_application_layout_even_for_a_frame( )
    {
        var client = await shared.SignedInAsync( "david@37signals.com" );
        using var framed = new HttpRequestMessage( HttpMethod.Get, Routes.RoomMessage( Designers, Second ) );
        framed.Headers.Add( "Turbo-Frame", "message_0002_edit" );

        var page = await client.SendAsync( framed, "text/html, application/xhtml+xml" );

        Assert.Equal( HttpStatusCode.OK, page.Status );
        Assert.Contains( "rel=\"stylesheet\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( "id=\"message_0002\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( "Seconded.", page.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Edit_puts_the_editable_body_in_the_editor( )
    {
        var client = await shared.SignedInAsync( "david@37signals.com" );

        var page = await client.GetAsync( Routes.EditRoomMessage( Designers, Second ) );

        Assert.Equal( HttpStatusCode.OK, page.Status );
        Assert.Contains( "<turbo-frame id=\"edit_message_0002\">", page.Body, StringComparison.Ordinal );
        Assert.Contains( "value=\"Seconded.\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( $"action=\"/rooms/{Designers}/messages/{Second}\"", page.Body, StringComparison.Ordinal );
    }

    [Theory]
    [MemberData( nameof( ChangesToSomeoneElsesMessage ) )]
    public async Task A_member_cannot_change_someone_elses_message( string method, string path )
    {
        var (client, token) = await shared.SignedInWithTokenAsync( "jz@37signals.com" );

        var reply = await client.SubmitFormAsync( new HttpMethod( method ), path, token, [("message[body]", "Mine now")] );

        Assert.Equal( HttpStatusCode.Forbidden, reply.Status );
        Assert.Equal( 1, shared.Scalar<long>( $"SELECT count(*) FROM messages WHERE id = {First}" ) );
    }

    [Theory]
    [MemberData( nameof( PathsToAWatercoolerMessage ) )]
    public async Task The_scoped_path_rejects_a_wrong_room_and_the_short_path_resolves_the_messages_room( string path, HttpStatusCode expected )
    {
        var client = await shared.SignedInAsync( "david@37signals.com" );

        var reply = await client.GetAsync( path );

        Assert.Equal( expected, reply.Status );
        if ( expected == HttpStatusCode.OK )
        {
            Assert.Contains( "id=\"message_0004\"", reply.Body, StringComparison.Ordinal );
        }
    }

    [Fact]
    public async Task The_short_message_URL_requires_room_membership( )
    {
        var member = await shared.SignedInAsync( "david@37signals.com" );
        var nonmember = await shared.SignedInAsync( "kevin@37signals.com" );

        var visible = await member.GetAsync( Routes.Message( Fourth ) );
        var hidden = await nonmember.GetAsync( Routes.Message( Fourth ) );

        Assert.Equal( HttpStatusCode.OK, visible.Status );
        Assert.Contains( "id=\"message_0004\"", visible.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.NotFound, hidden.Status );
    }

    [Fact]
    public async Task Updating_reindexes_after_commit_then_replaces_the_presentation_then_redirects( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Designers ) );
        factory.Execute( $"INSERT INTO message_search_index(rowid, body) VALUES ({Second}, 'Seconded.')" );
        using var taps = await SliceTaps.OnRoomAsync( factory, Designers );
        var before = factory.Scalar<string>( $"SELECT updated_at FROM messages WHERE id = {Second}" );
        factory.Clock.Advance( TimeSpan.FromSeconds( 5 ) );
        factory.Events.Clear( );

        var reply = await client.SubmitFormAsync(
            HttpMethod.Put,
            Routes.RoomMessage( Designers, Second ),
            token,
            [("message[body]", "<p>Updated <b>body</b></p>")] );

        Assert.Equal( HttpStatusCode.Found, reply.Status );
        Assert.Equal( $"{factory.BaseAddress}rooms/{Designers}/messages/{Second}", reply.Location );
        Assert.Equal( "<p>Updated <b>body</b></p>", SliceRows.MessageBody( factory, Second ) );
        Assert.Equal( "Updated body", factory.Scalar<string>( $"SELECT body FROM message_search_index WHERE rowid = {Second}" ) );
        Assert.NotEqual( before, factory.Scalar<string>( $"SELECT updated_at FROM messages WHERE id = {Second}" ) );
        Assert.Equal( ["plain_text", "broadcast replace"], factory.Events.Only( "plain_text", "broadcast" ) );
        client.Dispose( );
    }

    [Fact]
    public async Task The_broadcast_replaces_the_presentation_and_keeps_the_scroll( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Designers ) );
        var app = factory.App;
        var payloads = new List<string>( );
        var room = await app.Database.ReadAsync( db => RoomQueries.FindById( db, Designers )!, TestContext.Current.CancellationToken );
        using var subscription = app.Cable.Hub.Subscribe( string.Join( ':', RoomBroadcasts.RoomMessages( room ) ), payload => payloads.Add( payload ) );

        await client.SubmitFormAsync( HttpMethod.Patch, Routes.RoomMessage( Designers, Second ), token, [("message[body]", "Edited")] );

        var payload = JsonNode.Parse( Assert.Single( payloads ) )!.GetValue<string>( );
        Assert.StartsWith(
            "<turbo-stream maintain_scroll=\"true\" action=\"replace\" target=\"presentation_message_0002\"><template>",
            payload,
            StringComparison.Ordinal );
        Assert.Contains( "Edited", payload, StringComparison.Ordinal );
        Assert.DoesNotContain( "authenticity_token", payload, StringComparison.Ordinal );
        client.Dispose( );
    }

    [Fact]
    public async Task An_unsupported_json_update_is_rejected_before_mutation_or_broadcast( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Designers ) );
        using var taps = await SliceTaps.OnRoomAsync( factory, Designers );
        factory.Events.Clear( );

        var reply = await client.SubmitFormAsync(
            HttpMethod.Put,
            Routes.RoomMessage( Designers, Second ),
            token,
            [("message[body]", "JSON please")],
            accept: "application/json" );

        Assert.Equal( HttpStatusCode.NotAcceptable, reply.Status );
        Assert.Equal( "Seconded.", SliceRows.MessageBody( factory, Second ) );
        Assert.Empty( factory.Events.Only( "broadcast" ) );
        client.Dispose( );
    }

    [Fact]
    public async Task Destroying_removes_the_message_from_the_index_then_the_room_then_answers_the_stream( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Designers ) );
        factory.Execute( $"INSERT INTO message_search_index(rowid, body) VALUES ({Second}, 'Seconded.')" );
        using var taps = await SliceTaps.OnRoomAsync( factory, Designers );
        factory.Events.Clear( );

        var reply = await client.SubmitFormAsync( HttpMethod.Delete, Routes.RoomMessage( Designers, Second ), token );

        Assert.Equal( HttpStatusCode.OK, reply.Status );
        Assert.Equal( "text/vnd.turbo-stream.html; charset=utf-8", reply.ContentType );
        Assert.Equal( "<turbo-stream action=\"remove\" target=\"message_0002\"></turbo-stream>", reply.Body.Trim( ) );
        Assert.Equal( 0, factory.Scalar<long>( $"SELECT count(*) FROM messages WHERE id = {Second}" ) );
        Assert.Equal( 0, factory.Scalar<long>( $"SELECT count(*) FROM message_search_index WHERE rowid = {Second}" ) );
        Assert.Equal( ["broadcast remove"], factory.Events.Only( "broadcast" ) );
        client.Dispose( );
    }

    [Fact]
    public async Task An_unsupported_accept_is_rejected_before_destroying_the_message( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Designers ) );
        var watercooler = SliceAppFactory.Id( "watercooler" );
        var fifth = SliceAppFactory.Id( "fifth" );

        var before = factory.Scalar<long>( $"SELECT count(*) FROM messages WHERE id = {fifth}" );
        var reply = await client.SubmitFormAsync( HttpMethod.Delete, Routes.RoomMessage( watercooler, fifth ), token, accept: "text/html" );

        Assert.Equal( HttpStatusCode.NotAcceptable, reply.Status );
        Assert.Equal( before, factory.Scalar<long>( $"SELECT count(*) FROM messages WHERE id = {fifth}" ) );
        client.Dispose( );
    }

    /// <summary>
    /// The token in the <c>authenticity_token</c> field instead of the <c>X-CSRF-Token</c> header, next to the
    /// <c>_method</c> override, as <c>button_to</c> renders them.
    /// </summary>
    [Fact]
    public async Task A_browser_form_deletes_through_method_override( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Designers ) );

        var reply = await client.SubmitFormAsync(
            HttpMethod.Post,
            Routes.RoomMessage( Designers, Second ),
            token: null,
            [("_method", "delete"), ("authenticity_token", token)] );

        Assert.Equal( HttpStatusCode.OK, reply.Status );
        Assert.Equal( 0, factory.Scalar<long>( $"SELECT count(*) FROM messages WHERE id = {Second}" ) );
        client.Dispose( );
    }
}
