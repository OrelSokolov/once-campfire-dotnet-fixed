using Campfire.Application.Data;

namespace Campfire.Application.Tests.Data;

public sealed class RecordNotFoundExceptionTests
{
    [Fact]
    public void With_id_has_the_message_of_find_and_names_the_model( )
    {
        var error = RecordNotFoundException.WithId( ModelNames.PushSubscription, 42 );

        Assert.Equal( "Couldn't find Push::Subscription with 'id'=42", error.Message );
        Assert.Equal( ModelNames.PushSubscription, error.Model );
    }
}
