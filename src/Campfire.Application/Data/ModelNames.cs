namespace Campfire.Application.Data;

/// <summary>
/// Rails model class names, as they appear in polymorphic <c>record_type</c> columns, Global IDs, signed ids and
/// <c>ActiveRecord::RecordNotFound</c> messages.
/// </summary>
public static class ModelNames
{
    public const string Account = "Account";
    public const string User = "User";
    public const string Room = "Room";
    public const string Membership = "Membership";
    public const string Message = "Message";
    public const string Boost = "Boost";
    public const string Blob = "ActiveStorage::Blob";
    public const string VariantRecord = "ActiveStorage::VariantRecord";
    public const string PushSubscription = "Push::Subscription";
}
