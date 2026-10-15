namespace Campfire.Application.Storage;

/// <summary>
/// Storage configuration. <see cref="Root"/> and <see cref="ServiceName"/> are Campfire's
/// <c>local</c> service (<c>config/storage.yml</c>); the rest are bounds Rails doesn't have, chosen so
/// that every upload the reference accepts in practice still works.
/// </summary>
public sealed class StorageOptions
{
    /// <summary><c>Rails.root.join("storage", "files")</c>.</summary>
    public string Root { get; set; } = Path.Combine( "storage", "files" );

    public string ServiceName { get; set; } = "local";

    /// <summary>Where blobs are copied for analysis and processing (<c>Dir.tmpdir</c>); the system temp directory by default.</summary>
    public string? TempDirectory { get; set; }

    /// <summary><c>ActiveStorage.paths[:ffmpeg] || "ffmpeg"</c>: resolved through <c>PATH</c>, as in the Rails image.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary><c>ActiveStorage.paths[:ffprobe] || "ffprobe"</c>.</summary>
    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>The largest file an upload may stage: Rack's multipart parser limit (10 GiB), which bounds Rails' uploads too.</summary>
    public long MaxUploadBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    /// <summary>The largest image libvips may transform, in pixels (16384 × 16384). Rails has no limit; this stops decompression bombs.</summary>
    public long MaxImagePixels { get; set; } = 16384L * 16384;

    /// <summary>How many libvips or ffmpeg conversions may run at once.</summary>
    public int MaxConcurrentConversions { get; set; } = Environment.ProcessorCount;

    /// <summary>How long libvips may spend on one variant before it is stopped.</summary>
    public TimeSpan ImageTimeout { get; set; } = TimeSpan.FromSeconds( 60 );

    /// <summary>How long ffmpeg may take to draw a preview frame (Rust's post-parity value).</summary>
    public TimeSpan FfmpegTimeout { get; set; } = TimeSpan.FromSeconds( 60 );

    /// <summary>How long ffprobe may take to read a file's streams (Rust's post-parity value).</summary>
    public TimeSpan FfprobeTimeout { get; set; } = TimeSpan.FromSeconds( 30 );

    /// <summary>The most a preview frame (ffmpeg's standard output) may be.</summary>
    public long MaxPreviewBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>The most ffprobe's JSON may be.</summary>
    public long MaxProbeBytes { get; set; } = 4L * 1024 * 1024;

    public string ResolvedTempDirectory => TempDirectory ?? Path.GetTempPath( );

    /// <summary>Throws <see cref="ArgumentException"/> for a value the storage code can't run with; call at startup.</summary>
    public void Validate( )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( Root );
        ArgumentException.ThrowIfNullOrWhiteSpace( ServiceName );
        ArgumentException.ThrowIfNullOrWhiteSpace( FfmpegPath );
        ArgumentException.ThrowIfNullOrWhiteSpace( FfprobePath );
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( MaxUploadBytes );
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( MaxImagePixels );
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( MaxConcurrentConversions );
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( MaxPreviewBytes );
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( MaxProbeBytes );
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual( ImageTimeout, TimeSpan.Zero );
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual( FfmpegTimeout, TimeSpan.Zero );
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual( FfprobeTimeout, TimeSpan.Zero );
    }
}
