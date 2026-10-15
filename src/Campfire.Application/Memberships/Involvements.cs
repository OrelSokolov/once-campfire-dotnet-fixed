namespace Campfire.Application.Memberships;

/// <summary>The names stored in <c>memberships.involvement</c>.</summary>
public static class Involvements
{
    public static string Name( Involvement involvement ) => involvement switch
    {
        Involvement.Invisible => "invisible",
        Involvement.Nothing => "nothing",
        Involvement.Mentions => "mentions",
        Involvement.Everything => "everything",
        _ => throw new ArgumentOutOfRangeException( nameof( involvement ), involvement, "Unknown involvement" ),
    };

    /// <summary>Parses a stored or submitted name; unknown names give <see langword="false"/>.</summary>
    public static bool TryParse( string? name, out Involvement involvement )
    {
        involvement = name switch
        {
            "invisible" => Involvement.Invisible,
            "nothing" => Involvement.Nothing,
            "mentions" => Involvement.Mentions,
            "everything" => Involvement.Everything,
            _ => ( Involvement ) (-1),
        };

        return Enum.IsDefined( involvement );
    }

    /// <exception cref="InvalidOperationException">The name is not an involvement.</exception>
    public static Involvement Parse( string name )
    {
        if ( !TryParse( name, out var involvement ) )
        {
            throw new InvalidOperationException( $"'{name}' is not a valid involvement" );
        }

        return involvement;
    }
}
