namespace Campfire.Web.Http;

/// <summary>Set when a form's <c>_method</c> field overrode a POST: the method the client actually sent.</summary>
public interface IMethodOverrideFeature
{
    string OriginalMethod { get; }
}
