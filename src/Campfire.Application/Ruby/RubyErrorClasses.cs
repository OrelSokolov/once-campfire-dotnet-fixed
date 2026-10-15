namespace Campfire.Application.Ruby;

/// <summary>Ruby exception class names, as Rails reports them for failed deliveries, unfurls and rich text parsing.</summary>
public static class RubyErrorClasses
{
    public const string StandardError = "StandardError";
    public const string ArgumentError = "ArgumentError";
    public const string NoMethodError = "NoMethodError";
    public const string SocketError = "SocketError";
    public const string SystemCallError = "SystemCallError";
    public const string ConnectionRefused = "Errno::ECONNREFUSED";
    public const string OpenTimeout = "Net::OpenTimeout";
    public const string ReadTimeout = "Net::ReadTimeout";
    public const string HttpBadResponse = "Net::HTTPBadResponse";
    public const string ZlibDataError = "Zlib::DataError";
    public const string InvalidUri = "URI::InvalidURIError";
    public const string InvalidUriComponent = "URI::InvalidComponentError";
    public const string SslError = "OpenSSL::SSL::SSLError";
    public const string EcKeyError = "OpenSSL::PKey::ECError";
    public const string EcPointError = "OpenSSL::PKey::EC::Point::Error";
    public const string WebPushError = "WebPush::Error";
}
