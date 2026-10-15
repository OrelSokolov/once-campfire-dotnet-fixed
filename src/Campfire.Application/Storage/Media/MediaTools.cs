using System.Text.Json;
using System.Text.Json.Nodes;
using Campfire.Application.Serialization;

namespace Campfire.Application.Storage.Media;

/// <summary>
/// ffmpeg and ffprobe as Active Storage runs them: <c>VideoPreviewer</c>'s
/// <c>ffmpeg -i &lt;input&gt; &lt;video_preview_arguments&gt; -</c> and the analyzers'
/// <c>ffprobe -print_format json -show_streams -show_format -v error &lt;input&gt;</c>, with the
/// deadlines and output caps of <see cref="StorageOptions"/>.
/// </summary>
public sealed class MediaTools
{
    private static readonly TimeSpan VersionCheckTimeout = TimeSpan.FromSeconds( 10 );

    private readonly StorageOptions _options;
    private readonly Lazy<Task<bool>> _ffmpegExists;

    public MediaTools( StorageOptions options )
    {
        ArgumentNullException.ThrowIfNull( options );

        _options = options;
        _ffmpegExists = new Lazy<Task<bool>>(
            ( ) => BoundedProcess.SucceedsAsync( options.FfmpegPath, ["-version"], VersionCheckTimeout, CancellationToken.None ) );
    }

    /// <summary><c>VideoPreviewer.ffmpeg_exists?</c>: <c>system(ffmpeg, "-version")</c>, memoized for the process.</summary>
    public Task<bool> FfmpegExistsAsync( ) => _ffmpegExists.Value;

    /// <summary>The exact argument vector of <c>draw_relevant_frame_from(file)</c>.</summary>
    public static IReadOnlyList<string> PreviewArguments( string inputPath ) =>
        ["-i", inputPath, .. StorageContentTypes.VideoPreviewArguments, "-"];

    /// <summary>The exact argument vector of <c>probe_from(file)</c>.</summary>
    public static IReadOnlyList<string> ProbeArguments( string inputPath ) =>
        ["-print_format", "json", "-show_streams", "-show_format", "-v", "error", inputPath];

    /// <summary>
    /// <c>draw_relevant_frame_from(input)</c>: writes ffmpeg's standard output (one JPEG frame) to
    /// <paramref name="output"/>. A non-zero exit is <see cref="StorageFailure.Preview"/> with Rails'
    /// message: <c>ffmpeg failed (status N): &lt;stderr&gt;</c>.
    /// </summary>
    public async Task DrawPreviewAsync( string inputPath, Stream output, CancellationToken cancellationToken )
    {
        ProcessOutcome outcome;
        try
        {
            outcome = await BoundedProcess.RunAsync(
                _options.FfmpegPath,
                PreviewArguments( inputPath ),
                output,
                _options.MaxPreviewBytes,
                _options.FfmpegTimeout,
                cancellationToken );
        }
        catch ( FileNotFoundException error )
        {
            throw new StorageException( StorageFailure.Preview, error.Message, error );
        }

        if ( !outcome.Succeeded )
        {
            throw new StorageException(
                StorageFailure.Preview, $"{_options.FfmpegPath} failed (status {outcome.ExitCode}): {outcome.StandardErrorTail}" );
        }
    }

    /// <summary>
    /// <c>probe_from(file)</c>: ffprobe's JSON, parsed as <c>JSON.parse</c> does (the exit status is not
    /// checked, as <c>IO.popen</c> doesn't). An empty object when ffprobe isn't installed; unreadable
    /// output is <see cref="StorageFailure.Analyze"/>.
    /// </summary>
    public async Task<JsonNode?> ProbeAsync( string inputPath, CancellationToken cancellationToken )
    {
        using var output = new MemoryStream( );
        try
        {
            await BoundedProcess.RunAsync(
                _options.FfprobePath,
                ProbeArguments( inputPath ),
                output,
                _options.MaxProbeBytes,
                _options.FfprobeTimeout,
                cancellationToken );
        }
        catch ( FileNotFoundException )
        {
            // `rescue Errno::ENOENT`: "Skipping video analysis because ffprobe isn't installed".
            return new JsonObject( );
        }

        try
        {
            return JsonPayload.Parse( output.GetBuffer( ).AsSpan( 0, ( int ) output.Length ) );
        }
        catch ( JsonException error )
        {
            throw new StorageException( StorageFailure.Analyze, $"ffprobe's output is not JSON: {error.Message}", error );
        }
    }
}
