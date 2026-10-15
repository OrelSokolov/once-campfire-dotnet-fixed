namespace Campfire.IntegrationTests.Support;

/// <summary>Counting markup in a page, e.g. the rows of a list.</summary>
internal static class TextOccurrences
{
    /// <summary>How many times <paramref name="value"/> occurs in <paramref name="text"/>, without overlaps.</summary>
    public static int Count( string text, string value )
    {
        ArgumentNullException.ThrowIfNull( text );
        ArgumentException.ThrowIfNullOrEmpty( value );

        var count = 0;
        var index = text.IndexOf( value, StringComparison.Ordinal );
        while ( index >= 0 )
        {
            count++;
            index = text.IndexOf( value, index + value.Length, StringComparison.Ordinal );
        }

        return count;
    }
}
