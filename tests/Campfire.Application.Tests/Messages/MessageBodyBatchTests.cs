using Campfire.Application.Data.Records;
using Campfire.Application.Messages;
using Campfire.Application.Tests.Support;

namespace Campfire.Application.Tests.Messages;

public sealed class MessageBodyBatchTests
{
    [Fact]
    public async Task Batch_bodies_preserve_unicode_null_and_absence_without_crossing_record_types( )
    {
        using var db = await TestDatabase.CreateAsync( );
        const string text = "<div>🦋 &amp; <b>hello</b></div>";
        var first = await db.WriteAsync( scope => MessageOperations.Create( scope, New( "batch-first", text ) ) );
        var blank = await db.WriteAsync( scope => MessageOperations.Create( scope, New( "batch-null", null ) ) );
        await db.ExecuteAsync(
            "INSERT INTO action_text_rich_texts (body,created_at,name,record_id,record_type,updated_at) VALUES (@p0,datetime('now'),'body',@p1,'User'," +
                "datetime('now'))",
            "wrong type",
            blank.Message.Id );
        var bodies = await db.ReadAsync( connection => MessageBodies.BodiesByIds(
            connection,
            [first.Message.Id, blank.Message.Id, first.Message.Id, long.MaxValue] ) );
        Assert.Equal( 3, bodies.Count );
        Assert.Equal( text, bodies[first.Message.Id] );
        Assert.Null( bodies[blank.Message.Id] );
        Assert.Null( bodies[long.MaxValue] );
        Assert.Equal( text, first.CanonicalText.Value );
        Assert.True( first.CanonicalText.IsSet );
        Assert.Empty( await db.ReadAsync( connection => MessageBodies.BodiesByIds( connection, [] ) ) );
    }

    [Fact]
    public async Task Batch_attachments_select_the_first_blob_and_keep_missing_entries_null( )
    {
        using var db = await TestDatabase.CreateAsync( );
        var attached = await db.WriteAsync( scope => MessageOperations.Create( scope, New( "batch-image", "image" ) with
        {
            Attachment = new NewAttachment
            {
                Key = "batch-image-key",
                Filename = "moon.jpg",
                ContentType = "image/jpeg",
                Metadata = "{\"identified\":true}",
                ServiceName = "local",
                ByteSize = 7,
                UploadSource = "moon.jpg"
            }
        } ) );
        var plain = await db.WriteAsync( scope => MessageOperations.Create( scope, New( "batch-no-image", "plain" ) ) );
        var second = await db.WriteAsync( scope => MessageOperations.Create( scope, New( "batch-second-image", "second" ) with
        {
            Attachment = new NewAttachment
            {
                Key = "batch-second-key",
                Filename = "second.jpg",
                ContentType = "image/jpeg",
                Metadata = "{\"identified\":true}",
                ServiceName = "local",
                ByteSize = 9,
                UploadSource = "second.jpg"
            }
        } ) );
        await db.ExecuteAsync(
            "INSERT INTO active_storage_attachments (name,record_type,record_id,blob_id,created_at) VALUES ('attachment','Message',@p0,@p1,datetime('now'))",
            attached.Message.Id,
            second.Blob!.Id );
        await db.ExecuteAsync(
            "INSERT INTO active_storage_attachments (name,record_type,record_id,blob_id,created_at) VALUES ('avatar','Message',@p0,@p1,datetime('now'))",
            plain.Message.Id,
            attached.Blob!.Id );
        var rows = await db.ReadAsync( connection => MessageBodies.AttachmentsByIds(
            connection,
            [attached.Message.Id, plain.Message.Id, long.MaxValue, attached.Message.Id] ) );
        Assert.Equal( 3, rows.Count );
        var value = Assert.IsType<(ActiveStorageAttachment Attachment, ActiveStorageBlob Blob)>( rows[attached.Message.Id] );
        Assert.Equal( "moon.jpg", value.Blob.Filename );
        Assert.Equal( 7, value.Blob.ByteSize );
        Assert.Equal( "attachment", value.Attachment.Name );
        Assert.Null( rows[plain.Message.Id] );
        Assert.Null( rows[long.MaxValue] );
        Assert.False( attached.CanonicalText.IsSet );
        Assert.Empty( await db.ReadAsync( connection => MessageBodies.AttachmentsByIds( connection, [] ) ) );
    }

    private static NewMessage New( string id, string? body ) => new( )
    {
        RoomId = TestDatabase.Id( "designers" ),
        CreatorId = TestDatabase.Id( "david" ),
        ClientMessageId = id,
        Body = body
    };
}
