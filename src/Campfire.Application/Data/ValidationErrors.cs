namespace Campfire.Application.Data;

/// <summary><c>ActiveModel::Errors</c>: attribute and message pairs, in the order they were added.</summary>
public sealed class ValidationErrors
{
    private readonly List<(string Attribute, string Message)> _errors = [];

    public bool IsEmpty => _errors.Count == 0;

    public IReadOnlyList<(string Attribute, string Message)> All => _errors;

    public void Add( string attribute, string message ) => _errors.Add( (attribute, message) );

    /// <summary><c>_errors[attribute]</c>.</summary>
    public IReadOnlyList<string> ErrorsOn( string attribute ) =>
        [.. _errors.Where( error => error.Attribute == attribute ).Select( error => error.Message )];

    /// <summary><c>_errors.full_messages</c>, e.g. "Endpoint must use HTTPS".</summary>
    public IReadOnlyList<string> FullMessages => [.. _errors.Select( error => $"{Humanize( error.Attribute )} {error.Message}" )];

    /// <summary>Throws <see cref="RecordInvalidException"/> unless empty.</summary>
    public void ThrowIfAny( )
    {
        if ( !IsEmpty )
        {
            throw new RecordInvalidException( this );
        }
    }

    public override string ToString( ) => string.Join( ", ", FullMessages );

    private static string Humanize( string attribute )
    {
        var words = (attribute.EndsWith( "_id", StringComparison.Ordinal ) ? attribute[..^3] : attribute).Replace( '_', ' ' );
        return words.Length == 0 ? words : char.ToUpperInvariant( words[0] ) + words[1..];
    }
}
