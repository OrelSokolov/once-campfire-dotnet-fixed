namespace Campfire.Web.Http;

/// <summary>Shared method combinations used when mapping endpoints.</summary>
public static class HttpMethodSets
{
    public static IReadOnlyList<string> GetAndHead { get; } = Array.AsReadOnly( new[] { HttpMethods.Get, HttpMethods.Head } );
    public static IReadOnlyList<string> PatchAndPut { get; } = Array.AsReadOnly( new[] { HttpMethods.Patch, HttpMethods.Put } );
    public static IReadOnlyList<string> GetAndConnect { get; } = Array.AsReadOnly( new[] { HttpMethods.Get, HttpMethods.Connect } );
}
