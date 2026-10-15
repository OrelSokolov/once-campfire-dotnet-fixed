using Campfire.Web.Http;
using QrCodes = Campfire.Application.Integrations.QrCodes.QrCode;

namespace Campfire.Web.Endpoints.QrCode;

/// <summary>
/// <c>QrCodeController</c> (<c>app/controllers/qr_code_controller.rb</c>): a QR code SVG of a Base64url-encoded URL
/// (the transfer link's zoomed QR code), public and cached for a year.
/// </summary>
public static partial class QrCodeEndpoints
{
    /// <summary><c>expires_in 1.year</c>: ActiveSupport's year, 365.2425 days.</summary>
    public const long OneYearSeconds = 31_556_952;

    /// <summary><c>render plain: qr_code, content_type: "image/svg+xml"</c>.</summary>
    public const string SvgContentType = "image/svg+xml; charset=utf-8";

    /// <summary>
    /// <c>RQRCode::QRCode.new(Base64.urlsafe_decode64(params[:id])).as_svg(viewbox: true, fill: :white, color: :black)</c>
    /// with <c>expires_in 1.year, public: true</c>. Malformed Base64 raises <c>ArgumentError</c> and data beyond a
    /// version 40 symbol raises too: both are 500s, as in Rails.
    /// </summary>
    private static Task<FrontendResult> ShowAsync( RequestContext requestContext, string id )
    {
        var data = QrCodes.DecodeId( id );
        if ( data is null )
        {
            return Task.FromResult( requestContext.Head( StatusCodes.Status400BadRequest ) );
        }

        var svg = QrCodes.Svg( data );
        requestContext.ExpiresIn( OneYearSeconds, new ExpiresInOptions { Public = true } );
        return Task.FromResult( requestContext.RenderAs( StatusCodes.Status200OK, SvgContentType, ResponseBody.FromString( svg ) ) );
    }
}
