namespace Campfire.Web.Assets;

/// <summary><c>Propshaft::MissingAssetError</c>, raised while rendering for an unknown logical path.</summary>
public sealed class MissingAssetException : Exception
{
    public MissingAssetException( string logicalPath )
        : base( $"The asset '{logicalPath}' was not found in the load path." ) => LogicalPath = logicalPath;

    public MissingAssetException( )
        : this( string.Empty )
    {
    }

    public MissingAssetException( string message, Exception innerException )
        : base( message, innerException ) => LogicalPath = string.Empty;

    public string LogicalPath { get; }
}
