using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// <c>MessagesController#create</c>'s <c>params.require(:message).permit(:body, :attachment, :client_message_id)</c>:
/// a <c>message</c> that is a plain value instead of a hash has no <c>permit</c> (<c>NoMethodError</c>, which Rails
/// answers with 500); a missing <c>message</c> is <c>ActionController::ParameterMissing</c> (400). Neither writes.
/// </summary>
public sealed class MessageCreateParamsTests( SliceAppFactory factory ) : IClassFixture<SliceAppFactory>
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    [Fact]
    public async Task A_message_param_that_is_a_plain_value_answers_500_and_creates_nothing( )
    {
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await client.CsrfTokenAsync( Routes.Room( Designers ) );
        var before = MessageCount( );

        var reply = await client.SubmitFormAsync( HttpMethod.Post, Routes.RoomMessages( Designers ), token, [("message", "hello")] );

        Assert.Equal( HttpStatusCode.InternalServerError, reply.Status );
        Assert.Equal( before, MessageCount( ) );
    }

    [Fact]
    public async Task A_missing_message_param_answers_400_and_creates_nothing( )
    {
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await client.CsrfTokenAsync( Routes.Room( Designers ) );
        var before = MessageCount( );

        var reply = await client.SubmitFormAsync( HttpMethod.Post, Routes.RoomMessages( Designers ), token, [("body", "hello")] );

        Assert.Equal( HttpStatusCode.BadRequest, reply.Status );
        Assert.Equal( before, MessageCount( ) );
    }

    private long MessageCount( ) => factory.Scalar<long>( "SELECT count(*) FROM messages" );
}
