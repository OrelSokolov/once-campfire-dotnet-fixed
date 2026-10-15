using System.Net.Mime;

namespace Campfire.Web.Http;

/// <summary>Media types shared by negotiation, rendering and compression.</summary>
public static class ContentTypes
{
    public const string Html = MediaTypeNames.Text.Html;
    public const string PlainText = MediaTypeNames.Text.Plain;
    public const string Css = MediaTypeNames.Text.Css;
    public const string JavaScript = MediaTypeNames.Text.JavaScript;
    public const string ApplicationJavaScript = "application/javascript";
    public const string Json = MediaTypeNames.Application.Json;
    public const string Manifest = "application/manifest+json";
    public const string Png = MediaTypeNames.Image.Png;
    public const string Svg = MediaTypeNames.Image.Svg;
    public const string Binary = MediaTypeNames.Application.Octet;
    public const string TurboStream = "text/vnd.turbo-stream.html";
    public const string MultipartForm = MediaTypeNames.Multipart.FormData;
    public const string UrlEncodedForm = MediaTypeNames.Application.FormUrlEncoded;
    public const string Utf8Charset = "; charset=utf-8";
    public const string HtmlUtf8 = Html + Utf8Charset;
    public const string PlainTextUtf8 = PlainText + Utf8Charset;
    public const string JsonUtf8 = Json + Utf8Charset;
    public const string TurboStreamUtf8 = TurboStream + Utf8Charset;
}
