namespace Campfire.Web.Policy;

/// <summary>A <see cref="UserAgentVersion"/> segment: a run of digits (leading zeros stripped) or a word.</summary>
public readonly record struct VersionSegment( bool IsNumber, string Value )
{
    public override string ToString( ) => IsNumber ? $"i:{Value}" : $"s:{Value}";

    internal static VersionSegment Number( string digits )
    {
        var trimmed = digits.TrimStart( '0' );

        return new VersionSegment( true, trimmed.Length == 0 ? "0" : trimmed );
    }

    internal ulong? AsNumber( ) => IsNumber && ulong.TryParse( Value, out var number ) ? number : null;
}
