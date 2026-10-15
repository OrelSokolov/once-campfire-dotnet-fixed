using Campfire.Application.Data;

using Microsoft.AspNetCore.Identity;

namespace Campfire.Application.Users;

/// <summary>
/// Active-user authentication with the password check outside every lease: the row
/// is read on a reader, the reader is returned, then the password is checked. An unknown address still costs one
/// password verification, so timing does not reveal which addresses exist.
/// </summary>
public static class UserAuthentication
{
    /// <summary>
    /// <c>User.active.authenticate_by(email_address:, password:)</c>: the active user with that address whose password
    /// matches, or null. A digest that needs a stronger hash is upgraded in place, unless the password changed meanwhile.
    /// </summary>
    public static async Task<User?> AuthenticateByAsync(
        CampfireDatabase database, string? emailAddress, string? password, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( database );

        if ( string.IsNullOrEmpty( password ) )
        {
            return null;
        }

        var user = await database.ReadAsync( db => UserQueries.FindActiveByEmailAddress( db, emailAddress ), cancellationToken );
        if ( user is null )
        {
            Passwords.SpendEqualTime( password );
            return null;
        }

        var result = user.PasswordDigest is { } digest ? Passwords.VerifyResult( password, digest ) : PasswordVerificationResult.Failed;
        if ( result == PasswordVerificationResult.Failed )
        {
            return null;
        }

        if ( result == PasswordVerificationResult.SuccessRehashNeeded )
        {
            var upgraded = Passwords.Digest( password );
            // Never overwrite a concurrent password change after the costly hash work.
            await database.WriteAsync( scope =>
            {
                var current = UserQueries.FindById( scope.Db, user.Id );
                if ( current?.PasswordDigest == user.PasswordDigest )
                {
                    UserOperations.Update( scope, user.Id, new UserChanges { PasswordDigest = upgraded } );
                }
            }, cancellationToken );
        }

        return user;
    }
}
