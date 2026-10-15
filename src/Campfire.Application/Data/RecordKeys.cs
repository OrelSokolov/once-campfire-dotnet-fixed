namespace Campfire.Application.Data;

/// <summary>The keys that place records in a transaction's commit order.</summary>
public static class RecordKeys
{
    public static RecordKey Account( long id ) => new( TableNames.Accounts, id );

    public static RecordKey User( long id ) => new( TableNames.Users, id );

    public static RecordKey Ban( long id ) => new( TableNames.Bans, id );

    public static RecordKey Session( long id ) => new( TableNames.Sessions, id );

    public static RecordKey PushSubscription( long id ) => new( TableNames.PushSubscriptions, id );

    public static RecordKey Webhook( long id ) => new( TableNames.Webhooks, id );

    public static RecordKey Room( long id ) => new( TableNames.Rooms, id );

    public static RecordKey Membership( long id ) => new( TableNames.Memberships, id );

    public static RecordKey Message( long id ) => new( TableNames.Messages, id );

    public static RecordKey Boost( long id ) => new( TableNames.Boosts, id );

    public static RecordKey RichText( long id ) => new( TableNames.RichTexts, id );

    public static RecordKey Blob( long id ) => new( TableNames.Blobs, id );

    public static RecordKey Attachment( long id ) => new( TableNames.Attachments, id );

    public static RecordKey Search( long id ) => new( TableNames.Searches, id );
}
