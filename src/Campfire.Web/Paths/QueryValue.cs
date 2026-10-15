using System.Globalization;

namespace Campfire.Web.Paths;

/// <summary>A query-string value: one scalar (<c>key=v</c>) or an array (<c>key[]=a&amp;key[]=b</c>).</summary>
public readonly record struct QueryValue
{
    private QueryValue( string? one, IReadOnlyList<string>? many )
    {
        One = one;
        Many = many;
    }

    public string? One { get; }

    public IReadOnlyList<string>? Many { get; }

    public static QueryValue Of( string? value ) => new( value, null );

    public static QueryValue Of( long value ) => new( value.ToString( CultureInfo.InvariantCulture ), null );

    public static QueryValue List( IEnumerable<string> values ) => new( null, values.ToArray( ) );

    public static QueryValue List( IEnumerable<long> values ) =>
        new( null, values.Select( value => value.ToString( CultureInfo.InvariantCulture ) ).ToArray( ) );

    public static implicit operator QueryValue( string? value ) => Of( value );

    public static implicit operator QueryValue( long value ) => Of( value );
}
