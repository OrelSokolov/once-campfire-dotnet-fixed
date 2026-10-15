namespace Campfire.Web.Hosting;

/// <summary>ACME wire vocabulary shared by signed requests and challenge selection.</summary>
internal static class AcmeProtocol
{
    internal const string JoseContentType = "application/jose+json";
    internal const string BadNonce = "urn:ietf:params:acme:error:badNonce";
    internal const string RateLimited = "urn:ietf:params:acme:error:rateLimited";
    internal const string HttpChallenge = "http-01";
    internal const string TlsChallenge = "tls-alpn-01";
    internal const string AcmeIdentifierExtension = "1.3.6.1.5.5.7.1.31";
    internal const string NewNonce = "newNonce";
    internal const string NewOrder = "newOrder";
    internal const string NewAccount = "newAccount";
    internal const string TermsAgreed = "termsOfServiceAgreed";
    internal const string ExternalBinding = "externalAccountBinding";
    internal const string Identifiers = "identifiers";
    internal const string Identifier = "identifier";
    internal const string Type = "type";
    internal const string Value = "value";
    internal const string Dns = "dns";
    internal const string Algorithm = "alg";
    internal const string Nonce = "nonce";
    internal const string Url = "url";
    internal const string Jwk = "jwk";
    internal const string KeyId = "kid";
    internal const string Protected = "protected";
    internal const string Payload = "payload";
    internal const string Signature = "signature";
    internal const string HmacSha256 = "HS256";
    internal const string Authorizations = "authorizations";
    internal const string Finalize = "finalize";
    internal const string Csr = "csr";
    internal const string Certificate = "certificate";
    internal const string Status = "status";
    internal const string Challenges = "challenges";
    internal const string Token = "token";
    internal const string Ready = "ready";
    internal const string Valid = "valid";
    internal const string Invalid = "invalid";
    internal const string Expired = "expired";
    internal const string Revoked = "revoked";
    internal const string Deactivated = "deactivated";
    internal const string Curve = "crv";
    internal const string P256 = "P-256";
    internal const string KeyType = "kty";
    internal const string Ec = "EC";
    internal const string XCoordinate = "x";
    internal const string YCoordinate = "y";
    internal const string Exponent = "e";
    internal const string Rsa = "RSA";
    internal const string Modulus = "n";
    internal const string EcSha256 = "ES256";
    internal const string RsaSha256 = "RS256";
}
