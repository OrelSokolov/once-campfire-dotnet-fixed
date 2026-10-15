// A script's command-line options, read one at a time. Part of the shared script library (see Repository.cs).

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

/// <summary>
/// Walks a script's options in order. An option's value is taken only when it is there (and, for numbers, valid), so in
/// <c>case "--reps" when reader.TryTakePositive( out int reps ):</c> a missing or bad value leaves the reader where it
/// was and the option falls through to the script's unknown-option case. Numbers use the invariant culture.
/// </summary>
internal sealed class ArgumentReader( IReadOnlyList<string> arguments )
{
    private int _index = -1;

    /// <summary>The option being read.</summary>
    public string Current => arguments[_index];

    /// <summary>Moves to the next option; false when there is none.</summary>
    public bool MoveNext( ) => ++_index < arguments.Count;

    /// <summary>Takes the argument after the current option as its value, when there is one.</summary>
    public bool TryTakeValue( [NotNullWhen( true )] out string? value )
    {
        value = _index + 1 < arguments.Count ? arguments[_index + 1] : null;
        if ( value is null )
        {
            return false;
        }

        _index++;

        return true;
    }

    /// <summary>Takes the argument after the current option when it is a whole number above zero.</summary>
    public bool TryTakePositive<TNumber>( out TNumber value )
        where TNumber : IBinaryInteger<TNumber>
    {
        value = TNumber.Zero;
        if ( _index + 1 >= arguments.Count )
        {
            return false;
        }

        if ( !TNumber.TryParse( arguments[_index + 1], CultureInfo.InvariantCulture, out var parsed ) || parsed <= TNumber.Zero )
        {
            return false;
        }

        _index++;
        value = parsed;

        return true;
    }
}
