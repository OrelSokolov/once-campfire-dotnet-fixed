namespace Campfire.Application.Data;

/// <summary>
/// A value that may be absent, distinct from a present null: a Rails parameter that was or was not sent. The
/// default is absent.
/// </summary>
public readonly record struct Assigned<T>( T Value )
{
    public bool IsSet { get; } = true;

    public static implicit operator Assigned<T>( T value ) => new( value );
}
