using System.Globalization;

namespace Campfire.IntegrationTests.Support;

/// <summary>Rows the tests read back, on side connections.</summary>
internal static class SliceRows
{
    public sealed record MessageRow( long Id, long RoomId, string CreatedAt );

    public sealed record BlobRow( long Id, string Key, string Metadata );

    public static MessageRow Message( SliceAppFactory factory, string clientMessageId )
    {
        using var connection = factory.OpenSideConnection( );
        using var command = connection.CreateCommand( );
        command.CommandText = "SELECT id, room_id, created_at FROM messages WHERE client_message_id = $id";
        command.Parameters.AddWithValue( "$id", clientMessageId );
        using var reader = command.ExecuteReader( );
        Assert.True( reader.Read( ), $"no message {clientMessageId}" );
        return new MessageRow( reader.GetInt64( 0 ), reader.GetInt64( 1 ), reader.GetString( 2 ) );
    }

    /// <summary>The message's stored rich text body (<c>action_text_rich_texts</c>).</summary>
    public static string? MessageBody( SliceAppFactory factory, long messageId ) =>
        factory.Scalar<string>( $"SELECT body FROM action_text_rich_texts WHERE record_type = 'Message' AND record_id = {messageId}" );

    public static long MessageCount( SliceAppFactory factory, string clientMessageId ) =>
        factory.Scalar<long>( $"SELECT count(*) FROM messages WHERE client_message_id = '{clientMessageId}'" );

    public static BlobRow AttachedBlob( SliceAppFactory factory, long messageId )
    {
        using var connection = factory.OpenSideConnection( );
        using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT b.id, b.key, b.metadata FROM active_storage_blobs b
            JOIN active_storage_attachments a ON a.blob_id = b.id
            WHERE a.record_type = 'Message' AND a.record_id = $id AND a.name = 'attachment'
            """;
        command.Parameters.AddWithValue( "$id", messageId );
        using var reader = command.ExecuteReader( );
        Assert.True( reader.Read( ), $"message {messageId} has no attachment" );
        return new BlobRow( reader.GetInt64( 0 ), reader.GetString( 1 ), reader.IsDBNull( 2 ) ? "" : reader.GetString( 2 ) );
    }

    /// <summary>The key of the image of a blob's (only) recorded variant.</summary>
    public static string VariantImage( SliceAppFactory factory, long blobId ) => factory.Scalar<string>( $"""
        SELECT b.key FROM active_storage_variant_records v
        JOIN active_storage_attachments a ON a.record_type = 'ActiveStorage::VariantRecord' AND a.record_id = v.id AND a.name = 'image'
        JOIN active_storage_blobs b ON b.id = a.blob_id
        WHERE v.blob_id = {blobId}
        """ ) ?? throw new InvalidOperationException( $"blob {blobId} has no variant" );

    /// <summary><c>storage/files/ab/cd/&lt;key&gt;</c>.</summary>
    public static string BlobPath( SliceAppFactory factory, string key ) => Path.Combine( factory.StoragePath, "files", key[..2], key[2..4], key );

    /// <summary>The staging directory's files (uploads not placed yet).</summary>
    public static int StagedFiles( SliceAppFactory factory )
    {
        var staging = Path.Combine( factory.StoragePath, "files", ".staging" );
        return Directory.Exists( staging ) ? Directory.GetFiles( staging ).Length : 0;
    }

    public static string? UnreadAt( SliceAppFactory factory, string membershipLabel )
    {
        var membershipId = SliceAppFactory.Id( membershipLabel ).ToString( CultureInfo.InvariantCulture );
        return factory.Scalar<string>( $"SELECT unread_at FROM memberships WHERE id = {membershipId}" );
    }
}
