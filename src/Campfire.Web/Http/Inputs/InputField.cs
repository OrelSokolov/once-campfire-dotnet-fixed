namespace Campfire.Web.Http.Inputs;

/// <summary>A permitted parameter for <c>params.permit</c>: its name, and whether a nested hash is allowed.</summary>
public sealed record InputField( string Name, bool AllowObject = false )
{
    public static implicit operator InputField( string name ) => Key( name );
    public static InputField Key( string name ) => new( name );
    public static InputField AnyHash( string name ) => new( name, true );
}
