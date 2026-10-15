using System.Text.RegularExpressions;
using NetVips;

namespace Campfire.Application.Storage.Media;

/// <summary>
/// The libvips calls ruby-vips makes for Active Storage: the image analyzer's header read, and
/// <c>ImageProcessing::Vips</c>' <c>resize_to_limit</c> pipeline with the saver picked from the output
/// file's extension (so the saver's defaults apply, exactly as <c>write_to_file</c> does).
/// </summary>
public static partial class VipsImages
{
    /// <summary><c>Vips::MAX_COORD</c>, which stands in for an omitted dimension.</summary>
    public const int MaxCoord = 10_000_000;

    /// <summary>
    /// <c>ImageProcessing::Vips::Processor::SHARPEN_MASK</c>:
    /// <c>new_from_array([[-1, -1, -1], [-1, 32, -1], [-1, -1, -1]], 24)</c>.
    /// </summary>
    private static readonly double[,] SharpenMask = { { -1, -1, -1 }, { -1, 32, -1 }, { -1, -1, -1 } };

    /// <summary>
    /// What <c>ImageAnalyzer::Vips</c> reads: <c>Vips::Image.new_from_file(path, access: :sequential)</c>'s
    /// width and height, swapped for an EXIF orientation that turns the image by 90°. <c>null</c> when
    /// libvips can't (or, with untrusted loaders blocked, won't) read the file: the analyzer then records nothing.
    /// </summary>
    public static (int Width, int Height)? ReadDimensions( string path )
    {
        VipsRuntime.EnsureInitialized( );
        try
        {
            using var image = Image.NewFromFile( path, access: Enums.Access.Sequential );

            return IsRotated( image ) ? (image.Height, image.Width) : (image.Width, image.Height);
        }
        catch ( VipsException )
        {
            return null;
        }
    }

    /// <summary>
    /// <c>ImageProcessing::Vips.source(input).loader(page: 0).convert(format).apply(operations).call</c>,
    /// saving to <paramref name="output"/> (whose extension picks the saver). Images over
    /// <paramref name="maxPixels"/> are refused, and <paramref name="cancellationToken"/> stops libvips
    /// mid-evaluation (a hardening bound; Rails has neither).
    /// </summary>
    public static void Transform( string input, string output, IReadOnlyList<ResizeToLimit> resizes, long maxPixels, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( resizes );

        VipsRuntime.EnsureInitialized( );
        var pipeline = new List<Image>( );
        try
        {
            var loaded = Load( input );
            pipeline.Add( loaded );
            if ( ( long ) loaded.Width * loaded.Height > maxPixels )
            {
                throw new StorageException( StorageFailure.LimitExceeded, $"The image is {loaded.Width}×{loaded.Height}, more than {maxPixels} pixels." );
            }

            // Killing the pipeline's source makes every region computed from it fail, which ends the save.
            using var stop = cancellationToken.Register( ( ) => loaded.SetKill( true ) );
            var image = Add( pipeline, loaded.Autorot( ) );
            foreach ( var resize in resizes )
            {
                image = Add( pipeline, ResizeImage( pipeline, image, resize ) );
            }

            cancellationToken.ThrowIfCancellationRequested( );
            image.WriteToFile( output );
        }
        catch ( VipsException error )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            throw new StorageException( StorageFailure.Vips, error.Message.Trim( ), error );
        }
        finally
        {
            foreach ( var image in pipeline )
            {
                image.Dispose( );
            }
        }
    }

    /// <summary>
    /// <c>Processor.load_image(path, page: 0)</c>: <c>Utils.select_valid_loader_options</c> passes
    /// <c>page</c> only to loaders that take it (GIF, TIFF, WebP, HEIF, ...).
    /// </summary>
    private static Image Load( string path )
    {
        var loader = Image.FindLoad( path );
        if ( loader is not null && Introspect.Get( loader ).OptionalInput.ContainsKey( "page" ) )
        {
            return Image.NewFromFile( path, kwargs: new VOption { { "page", 0 } } );
        }

        return Image.NewFromFile( path );
    }

    /// <summary>
    /// <c>resize_to_limit(width, height)</c>: <c>thumbnail_image(width, height:, size: :down, no_rotate: true)</c>
    /// (the image was already <c>autorot</c>ed on load), then <c>conv(SHARPEN_MASK, precision: :integer)</c>.
    /// </summary>
    private static Image ResizeImage( List<Image> pipeline, Image image, ResizeToLimit resize )
    {
        var thumbnail = Add(
            pipeline, image.ThumbnailImage( resize.Width ?? MaxCoord, height: resize.Height ?? MaxCoord, size: Enums.Size.Down, noRotate: true ) );
        var mask = Add( pipeline, Image.NewFromArray( SharpenMask, 24 ) );

        return thumbnail.Conv( mask, precision: Enums.Precision.Integer );
    }

    /// <summary><c>ROTATIONS === image.get("exif-ifd0-Orientation")</c>, where a missing field is not rotated.</summary>
    private static bool IsRotated( Image image )
    {
        const string field = "exif-ifd0-Orientation";
        if ( image.GetTypeOf( field ) == 0 )
        {
            return false;
        }

        try
        {
            return image.Get( field ) is string orientation && Rotations( ).IsMatch( orientation );
        }
        catch ( VipsException )
        {
            return false;
        }
    }

    private static Image Add( List<Image> pipeline, Image image )
    {
        pipeline.Add( image );

        return image;
    }

    /// <summary><c>ImageAnalyzer::Vips::ROTATIONS</c>.</summary>
    [GeneratedRegex( "Right-top|Left-bottom|Top-right|Bottom-left" )]
    private static partial Regex Rotations( );
}
