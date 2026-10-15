namespace Campfire.Application.Data;

/// <summary>A database row an operation touched, as Active Record tracks it in a transaction.</summary>
public readonly record struct RecordKey( string Table, long Id )
{
    public override string ToString( ) => $"{Table}#{Id}";
}
