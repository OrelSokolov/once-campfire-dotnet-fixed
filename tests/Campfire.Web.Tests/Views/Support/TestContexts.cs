using Campfire.Web.Views.Models;

namespace Campfire.Web.Tests.Views.Support;

/// <summary>Minimal view contexts for tests that don't replay a golden.</summary>
internal static class TestContexts
{
    /// <summary>A context outside any request: no user, no CSRF tokens, the golden asset digests.</summary>
    public static ViewContext Plain( ) => new( )
    {
        Account = new AccountSummary( "37signals", "/account/logo", false ),
        Assets = PageGoldenFacts.Assets,
        BaseUrl = "http://campfire.test",
    };
}
