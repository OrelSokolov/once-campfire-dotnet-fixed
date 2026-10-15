using System.Security.Cryptography;

namespace Campfire.Application.Data;

/// <summary>Ruby's <c>SecureRandom</c> generators, as the models use them.</summary>
public static class SecureTokens
{
    private const string AlphanumericCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private const string Base58Characters = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary><c>SecureRandom.alphanumeric(length)</c>.</summary>
    public static string Alphanumeric( int length ) => RandomNumberGenerator.GetString( AlphanumericCharacters, length );

    /// <summary><c>SecureRandom.base58(length)</c>, as <c>has_secure_token</c> uses it.</summary>
    public static string Base58( int length ) => RandomNumberGenerator.GetString( Base58Characters, length );

    /// <summary><c>SecureRandom.uuid</c> / <c>Random.uuid</c>: a lower-case version 4 UUID.</summary>
    public static string Uuid( ) => Guid.NewGuid( ).ToString( );
}
