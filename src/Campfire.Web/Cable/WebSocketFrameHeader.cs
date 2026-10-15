namespace Campfire.Web.Cable;

/// <summary>
/// The RFC 6455 §5.2 frame header bits the hybrid transport writes (<see cref="CableFrame"/>) and inspects
/// (<see cref="CableBatchStream"/>), with RFC 7692's per-message compression bit.
/// </summary>
internal static class WebSocketFrameHeader
{
    /// <summary>FIN: the frame is a whole message.</summary>
    public const byte Final = 0x80;

    /// <summary>RSV1 on a message's first frame: its payload is permessage-deflate compressed.</summary>
    public const byte Compressed = 0x40;

    /// <summary>The first byte's opcode bits.</summary>
    public const byte OpcodeMask = 0x0f;

    public const byte TextOpcode = 0x1;

    public const byte CloseOpcode = 0x8;

    public const byte PingOpcode = 0x9;

    public const byte PongOpcode = 0xa;

    /// <summary>The longest payload whose length fits the second byte itself.</summary>
    public const int MaxShortPayloadLength = 125;

    /// <summary>The second byte when a 16-bit length follows.</summary>
    public const byte MediumLengthMarker = 126;

    /// <summary>The second byte when a 64-bit length follows.</summary>
    public const byte LongLengthMarker = 127;

    /// <summary>The header of a short payload: the first byte, then the length.</summary>
    public const int ShortHeaderLength = 2;

    /// <summary>Two bytes, then a 16-bit length.</summary>
    public const int MediumHeaderLength = 4;

    /// <summary>Two bytes, then a 64-bit length.</summary>
    public const int LongHeaderLength = 10;
}
