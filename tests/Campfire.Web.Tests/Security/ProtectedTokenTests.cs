using Campfire.Web.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Campfire.Web.Tests.Security;

public sealed class ProtectedTokenTests
{
    [Fact]
    public void Repeated_urls_reuse_the_protected_value_but_different_versions_do_not( )
    {
        using var tokens = new ProtectedTokens( new EphemeralDataProtectionProvider( ) );
        var expires = DateTimeOffset.UtcNow.AddHours( 1 );
        var first = tokens.Protect( "User/7/updated-v1", "avatar" );
        Assert.Equal( first, tokens.Protect( "User/7/updated-v1", "avatar" ) );
        Assert.NotEqual( first, tokens.Protect( "User/7/updated-v2", "avatar" ) );
        Assert.NotEqual( first, tokens.Protect( "User/7/updated-v1", "download" ) );
        Assert.NotEqual( first, tokens.Protect( "User/7/updated-v1", "avatar", expires.AddMinutes( 1 ) ) );
    }

    [Fact]
    public void Tampered_expired_and_different_purpose_tokens_fail_closed( )
    {
        var tokens = new ProtectedTokens( new EphemeralDataProtectionProvider( ) );
        var now = DateTimeOffset.UtcNow;
        var key = tokens.Protect( "7", "record/User", now.AddMinutes( 1 ) );
        Assert.Equal( "7", tokens.Unprotect( key, "record/User", now ) );
        Assert.Null( tokens.Unprotect( key, "record/Blob", now ) );
        Assert.Null( tokens.Unprotect( key, "record/User", now.AddMinutes( 1 ) ) );
        var authenticatedBytes = WebEncoders.Base64UrlDecode( key );
        authenticatedBytes[^1] ^= 1;
        Assert.Null( tokens.Unprotect( WebEncoders.Base64UrlEncode( authenticatedBytes ), "record/User", now ) );
        Assert.Null( tokens.Unprotect( "not-a-token", "record/User", now ) );
    }

    [Fact]
    public void Persisted_keys_allow_a_new_provider_to_read_a_token_after_restart( )
    {
        var directory = Directory.CreateTempSubdirectory( "campfire-token-" );
        try
        {
            var first = new ProtectedTokens( DataProtectionProvider.Create( directory, builder => builder.SetApplicationName( "Campfire" ) ) );
            var token = first.Protect( "session-transfer", "transfer/User" );
            var restarted = new ProtectedTokens( DataProtectionProvider.Create( directory, builder => builder.SetApplicationName( "Campfire" ) ) );
            Assert.Equal( "session-transfer", restarted.Unprotect( token, "transfer/User", DateTimeOffset.UtcNow ) );
            var otherApp = new ProtectedTokens( DataProtectionProvider.Create( directory, builder => builder.SetApplicationName( "Different" ) ) );
            Assert.Null( otherApp.Unprotect( token, "transfer/User", DateTimeOffset.UtcNow ) );
        }
        finally
        {
            directory.Delete( recursive: true );
        }
    }
}
