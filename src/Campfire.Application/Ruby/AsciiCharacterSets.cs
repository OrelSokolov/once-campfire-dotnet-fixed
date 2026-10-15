using System.Buffers;

namespace Campfire.Application.Ruby;

/// <summary>ASCII character classes of Ruby's regular expressions, as search values for span scans.</summary>
internal static class AsciiCharacterSets
{
    /// <summary><c>\h</c>: <c>[0-9a-fA-F]</c>.</summary>
    public static readonly SearchValues<char> HexDigits = SearchValues.Create( "0123456789abcdefABCDEF" );
}
