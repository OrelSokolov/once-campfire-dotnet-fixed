using System.Buffers.Text;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Campfire.Application.Data;
using Campfire.Application.Security;
using Campfire.Application.Users;
using Microsoft.Data.Sqlite;

namespace Campfire.Web.App;

/// <summary>Prepare a disposable benchmark copy, never an installed database.</summary>
public static partial class SyntheticFixturePreparation
{
    public const string OptInVariable = "CAMPFIRE_SYNTHETIC_FIXTURE";
    public const string PasswordVariable = "CAMPFIRE_SYNTHETIC_PASSWORD";
    public const string MarkerKey = "campfire_synthetic_fixture";
    public const string AvatarIdVariable = "CAMPFIRE_SYNTHETIC_AVATAR_ID";
    public const string NativeLabelsFile = "native-fixture.json";
    public const string LabelsPathVariable = "CAMPFIRE_SYNTHETIC_LABELS_PATH";

    public static void Run( IServiceProvider services )
    {
        if ( Environment.GetEnvironmentVariable( OptInVariable ) != "1" ||
            Environment.GetEnvironmentVariable( PasswordVariable ) is not { Length: > 0 } password )
        {
            throw new InvalidOperationException( "prepare-fixture requires explicit synthetic-fixture opt-in and password." );
        }

        var paths = services.GetRequiredService<StoragePaths>( );
        var connectionString = new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, Mode = SqliteOpenMode.ReadWrite }.ToString( );
        using var db = new SqliteConnection( connectionString );
        db.Open( );
        var signers = new SignedGlobalIds( services.GetRequiredService<ITokenProtector>( ) );
        Prepare( db, signers, password );
        var signedIds = new SignedIds( services.GetRequiredService<ITokenProtector>( ) );
        var labels = new JsonObject( );
        if ( long.TryParse( Environment.GetEnvironmentVariable( AvatarIdVariable ), NumberStyles.None, CultureInfo.InvariantCulture, out var avatarId ) )
        {
            labels["avatar_token"] = signedIds.Generate( ModelNames.User, avatarId, SignedIds.AvatarPurpose );
        }

        if ( Environment.GetEnvironmentVariable( LabelsPathVariable ) is { Length: > 0 } source )
        {
            if ( new FileInfo( source ).Length > 1024 * 1024 )
            {
                throw new InvalidDataException( "Synthetic labels exceed the limit." );
            }

            var originalLabels = JsonNode.Parse( File.ReadAllText( source ) ) as JsonObject
                ?? throw new InvalidDataException( "Synthetic labels must be an object." );
            labels["labels"] = NativeCaptureLabels( originalLabels, signedIds, services.GetRequiredService<TimeProvider>( ).GetUtcNow( ) );
        }

        File.WriteAllText( Path.Combine( Path.GetDirectoryName( paths.DatabaseFile )!, NativeLabelsFile ), labels.ToJsonString( ) );
        Console.WriteLine( "Synthetic fixture prepared: native credentials and attachment identifiers; logical rows and indexes retained." );
    }

    internal static void Prepare( SqliteConnection db, SignedGlobalIds signers, string password )
    {
        RequireSyntheticMarker( db );
        var bodies = ReadBodies( db );
        using var transaction = db.BeginTransaction( );
        using var users = db.CreateCommand( );
        users.Transaction = transaction;
        users.CommandText = "UPDATE users SET password_digest = @digest WHERE role <> @botRole";
        users.Parameters.AddWithValue( "@digest", Passwords.Digest( password ) );
        users.Parameters.AddWithValue( "@botRole", ( int ) UserRole.Bot );
        users.ExecuteNonQuery( );
        using var update = db.CreateCommand( );
        update.Transaction = transaction;
        update.CommandText = "UPDATE action_text_rich_texts SET body = @body WHERE id = @id";
        var bodyParameter = update.Parameters.Add( "@body", SqliteType.Text );
        var idParameter = update.Parameters.Add( "@id", SqliteType.Integer );
        foreach ( var (id, body) in bodies )
        {
            var rewritten = SignedAttachment( ).Replace( body, match => ReplaceIdentifier( match, signers ) );
            if ( rewritten == body )
            {
                continue;
            }

            bodyParameter.Value = rewritten;
            idParameter.Value = id;
            update.ExecuteNonQuery( );
        }

        RewriteVariantDigests( db, transaction );
        transaction.Commit( );
    }

    private static List<(long Id, string Body)> ReadBodies( SqliteConnection db )
    {
        using var command = db.CreateCommand( );
        command.CommandText = "SELECT id, body FROM action_text_rich_texts WHERE body LIKE '%sgid=%'";
        using var reader = command.ExecuteReader( );
        var result = new List<(long, string)>( );
        while ( reader.Read( ) )
        {
            result.Add( (reader.GetInt64( 0 ), reader.GetString( 1 )) );
        }

        return result;
    }

    private static string ReplaceIdentifier( Match match, SignedGlobalIds signers )
    {
        var token = match.Groups[2].Value;
        if ( signers.Locate( token, SignedGlobalIds.AttachablePurpose, DateTimeOffset.UtcNow ) is not null )
        {
            return match.Value;
        }

        // This decoder exists only in the offline fixture command. Runtime lookup accepts native protection only.
        var encoded = token.Split( "--", 2 )[0];
        var envelope = JsonNode.Parse( Base64Url.DecodeFromChars( encoded.TrimEnd( '=' ).Replace( '+', '-' ).Replace( '/', '_' ) ) );
        var rails = envelope?["_rails"];
        var text = rails?["data"]?.GetValue<string>( ) ?? DecodeFixtureString( rails?["message"]?.GetValue<string>( ) );
        var globalId = GlobalId.Parse( text )
            ?? throw new InvalidDataException( "Synthetic attachment has an unsupported identifier." );
        if ( globalId.App != GlobalId.CampfireApp || globalId.ModelName is not (ModelNames.User or ModelNames.Blob) ||
            !long.TryParse( globalId.Id, NumberStyles.None, CultureInfo.InvariantCulture, out _ ) )
        {
            throw new InvalidDataException( "Synthetic attachment identifier is outside the supported fixture models." );
        }

        return match.Groups[1].Value + signers.AttachableSignedGlobalId( globalId ) + match.Groups[3].Value;
    }

    [GeneratedRegex( "(sgid=[\"'])([^\"']+)([\"'])", RegexOptions.CultureInvariant )]
    private static partial Regex SignedAttachment( );
}
