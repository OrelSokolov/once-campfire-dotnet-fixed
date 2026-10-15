using System.Globalization;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// A number as Ruby prints it: an Integer bare, a Float through <see cref="RubyText.RubyFloat"/>. Attachment
/// metadata keeps image sizes as Integers and video sizes as Floats, and the views print both.
/// </summary>
public readonly record struct RubyNumber
{
    private RubyNumber( long whole, double real, bool isReal )
    {
        WholeValue = whole;
        RealValue = real;
        IsReal = isReal;
    }

    /// <summary>The value of an Integer.</summary>
    public long WholeValue { get; }

    /// <summary>The value of a Float.</summary>
    public double RealValue { get; }

    /// <summary>Whether this is a Ruby Float.</summary>
    public bool IsReal { get; }

    /// <summary>A Ruby Integer.</summary>
    public static RubyNumber Whole( long value ) => new( value, 0, false );

    /// <summary>A Ruby Float.</summary>
    public static RubyNumber Real( double value ) => new( 0, value, true );

    public double ToDouble( ) => IsReal ? RealValue : WholeValue;

    /// <summary><c>number / 2</c>: floor division for Integers.</summary>
    public RubyNumber Half( ) => IsReal ? Real( RealValue / 2.0 ) : Whole( ( long ) Math.Floor( WholeValue / 2.0 ) );

    public override string ToString( ) =>
        IsReal ? RubyText.RubyFloat( RealValue ) : WholeValue.ToString( CultureInfo.InvariantCulture );
}
