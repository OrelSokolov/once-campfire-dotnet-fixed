namespace Campfire.Application.Storage;

/// <summary>Why a storage operation failed, named after the Active Storage error it stands for.</summary>
public enum StorageFailure
{
    /// <summary><c>ActiveStorage::FileNotFoundError</c>: the blob's file is missing.</summary>
    FileNotFound,

    /// <summary><c>ActiveStorage::IntegrityError</c>: the bytes don't match the blob's checksum.</summary>
    Integrity,

    /// <summary><c>ActiveSupport::MessageVerifier::InvalidSignature</c>: a bad, expired or off-purpose signed value.</summary>
    InvalidSignature,

    /// <summary>
    /// <c>ArgumentError</c> from <c>Variation#format</c>, or a transformation this port does not
    /// implement (Campfire only defines <c>resize_to_limit</c>).
    /// </summary>
    InvalidVariation,

    /// <summary><c>ActiveStorage::InvariableError</c>.</summary>
    Invariable,

    /// <summary><c>ActiveStorage::UnpreviewableError</c>.</summary>
    Unpreviewable,

    /// <summary><c>ActiveStorage::UnrepresentableError</c>.</summary>
    Unrepresentable,

    /// <summary><c>Vips::Error</c> while transforming an image.</summary>
    Vips,

    /// <summary><c>ActiveStorage::PreviewError</c>: ffmpeg failed to draw a frame.</summary>
    Preview,

    /// <summary>An analyzer could not read its tool's output (Ruby raises out of <c>JSON.parse</c>, <c>Float()</c> or <c>Integer()</c>).</summary>
    Analyze,

    /// <summary>A hardening bound with no Rails twin: upload size, image pixels or subprocess output.</summary>
    LimitExceeded,

    /// <summary>A hardening bound with no Rails twin: a conversion or subprocess ran past its deadline and was stopped.</summary>
    TimedOut,
}

/// <summary>A storage operation failed; <see cref="Failure"/> says how, so callers can map it to a response.</summary>
public sealed class StorageException : Exception
{
    public StorageException( StorageFailure failure, string message, Exception? innerException = null )
        : base( message, innerException )
    {
        Failure = failure;
    }

    public StorageFailure Failure { get; }
}
