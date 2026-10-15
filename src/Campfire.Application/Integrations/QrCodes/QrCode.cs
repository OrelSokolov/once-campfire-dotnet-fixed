using System.Globalization;
using System.Text;
using Campfire.Application.Ruby;

namespace Campfire.Application.Integrations.QrCodes;

/// <summary>
/// <c>QrCodeController#show</c> (<c>app/controllers/qr_code_controller.rb</c>):
/// <c>RQRCode::QRCode.new(url).as_svg(viewbox: true, fill: :white, color: :black)</c> for the Base64url-decoded
/// <c>id</c>, as a port of rqrcode_core 2.1.0 and rqrcode 3.2.0's SVG (Rect) output.
/// </summary>
/// <remarks>
/// The output has to be byte-identical to the gem's, so this follows its algorithms rather than the QR standard's
/// optimal choices: one segment whose mode is numeric, alphanumeric or 8-bit byte (in that order of preference), error
/// correction level H, the smallest version whose capacity is strictly greater than the segment's bits, and the mask
/// with the fewest lost points as the gem scores them. Vectors: <c>vectors/qr/rqrcode.json</c>.
/// <para>
/// Bound: input that doesn't fit version 40 at level H (3,056 digits, 1,852 alphanumeric characters or 1,273 bytes)
/// throws <see cref="QrCodeCapacityException"/> before any symbol is built, as rqrcode raises. The web layer lets it
/// become the 500 Rails answers; the work per request is therefore at most one version 40 symbol and eight mask
/// evaluations.
/// </para>
/// </remarks>
public static class QrCode
{
    /// <summary>The side of one module in the SVG, in user units (rqrcode's default <c>module_size</c>).</summary>
    private const int ModuleSize = 11;

    /// <summary>
    /// <c>Base64.urlsafe_decode64(params[:id])</c>: the bytes to encode, or null where Ruby raises
    /// <c>ArgumentError</c> (a 500 in Rails).
    /// </summary>
    public static byte[]? DecodeId( string id ) => RubyBase64.UrlSafeDecode( id );

    /// <summary>The SVG document <c>as_svg(viewbox: true, fill: :white, color: :black)</c> renders for <paramref name="data"/>.</summary>
    public static string Svg( ReadOnlySpan<byte> data )
    {
        var modules = Modules( data );
        var count = modules.GetLength( 0 );
        var dimension = (count * ModuleSize).ToString( CultureInfo.InvariantCulture );
        var svg = new StringBuilder( 256 + (count * count * 30) );
        svg.Append( "<?xml version=\"1.0\" standalone=\"yes\"?>" );
        svg.Append( "<svg version=\"1.1\" xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" " );
        svg.Append( "xmlns:ev=\"http://www.w3.org/2001/xml-events\" viewBox=\"0 0 " ).Append( dimension ).Append( ' ' ).Append( dimension );
        svg.Append( "\" shape-rendering=\"crispEdges\">" );
        svg.Append( "<rect width=\"" ).Append( dimension ).Append( "\" height=\"" ).Append( dimension ).Append( "\" x=\"0\" y=\"0\" fill=\"white\"/>" );
        for ( var row = 0; row < count; row++ )
        {
            for ( var column = 0; column < count; column++ )
            {
                if ( modules[row, column] )
                {
                    svg.Append(
                        CultureInfo.InvariantCulture,
                        $"<rect width=\"{ModuleSize}\" height=\"{ModuleSize}\" x=\"{column * ModuleSize}\" y=\"{row * ModuleSize}\" fill=\"black\"/>" );
                }
            }
        }

        return svg.Append( "</svg>" ).ToString( );
    }

    /// <summary>The symbol's modules (true is dark), rows first.</summary>
    public static bool[,] Modules( ReadOnlySpan<byte> data ) => Modules( data, out _ );

    /// <summary>The symbol's modules and the version rqrcode picks.</summary>
    public static bool[,] Modules( ReadOnlySpan<byte> data, out int version )
    {
        var mode = QrCodewords.ModeOf( data );
        if ( QrCodewords.MinimumVersion( mode, data.Length ) is not { } minimum )
        {
            throw new QrCodeCapacityException( );
        }

        version = minimum;
        return QrMatrix.Build( version, QrCodewords.Create( version, mode, data ) );
    }
}
