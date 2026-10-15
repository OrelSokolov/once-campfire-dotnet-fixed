using System.Globalization;

namespace Campfire.Application.Tests.Fixtures;

/// <summary>A YAML scalar: its text, and whether it was quoted (a quoted scalar is always a string).</summary>
internal sealed record YamlScalar( string Text, bool Quoted )
{
    /// <summary>Psych's plain-scalar nulls: empty, <c>~</c>, <c>null</c>.</summary>
    public bool IsNull => !Quoted && Text is "" or "~" or "null" or "Null" or "NULL";

    /// <summary>Psych's YAML 1.1 booleans, when unquoted.</summary>
    public bool? Boolean => Quoted ? null : Text switch
    {
        "true" or "True" or "TRUE" or "yes" or "Yes" or "YES" or "on" or "On" or "ON" => true,
        "false" or "False" or "FALSE" or "no" or "No" or "NO" or "off" or "Off" or "OFF" => false,
        _ => null,
    };

    /// <summary>A plain decimal integer (<c>0001</c> would be octal in Psych; the fixtures quote such values).</summary>
    public long? Integer =>
        !Quoted && long.TryParse(
            Text.Replace( "_", "", StringComparison.Ordinal ),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var value )
        && !(Text.TrimStart( '-', '+' ).StartsWith( '0' ) && Text.TrimStart( '-', '+' ).Length > 1)
        ? value
        : null;
}
