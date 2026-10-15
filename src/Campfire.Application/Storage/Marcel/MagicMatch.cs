namespace Campfire.Application.Storage.Marcel;

/// <summary>
/// One entry of <c>Marcel::MAGIC</c>: <c>[offset, value, children]</c>. With <see cref="RangeEnd"/>
/// the offset is a range and <see cref="Value"/> may appear anywhere inside it; otherwise it must
/// appear exactly at <see cref="Offset"/>. An entry without a value never matches.
/// </summary>
internal sealed class MagicMatch( int offset, int? rangeEnd, string? hexValue, MagicMatch[] children )
{
    public int Offset { get; } = offset;

    public int? RangeEnd { get; } = rangeEnd;

    public byte[]? Value { get; } = hexValue is null ? null : Convert.FromHexString( hexValue );

    public MagicMatch[] Children { get; } = children;

    /// <summary>The number of leading bytes this entry (and its children) may read.</summary>
    public int Reach
    {
        get
        {
            var length = Value?.Length ?? 0;
            var ownReach = RangeEnd is { } end ? end + length : Offset + length;

            return Children.Aggregate( ownReach, ( reach, child ) => Math.Max( reach, child.Reach ) );
        }
    }
}
