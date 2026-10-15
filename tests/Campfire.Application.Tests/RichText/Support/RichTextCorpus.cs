using System.Text.Json.Nodes;
using Campfire.Application.RichText;

namespace Campfire.Application.Tests.RichText.Support;

/// <summary>
/// The Rails-generated rich text corpus (<c>vectors/richtext/expected.json</c>, from
/// <c>reference-tools/richtext/generate.rb</c>; never edited by hand).
/// </summary>
internal static class RichTextCorpus
{
    private static readonly Lazy<JsonNode> Document = new( Load );
    private static readonly Lazy<Dictionary<string, JsonNode>> CasesByName = new( ( ) => Cases.ToDictionary( Name ) );
    private static readonly Lazy<CorpusResolver> CorpusResolver = new( ( ) => new CorpusResolver( Document.Value ) );

    public static IReadOnlyList<JsonNode> Cases => [.. Document.Value["cases"]!.AsArray( ).Select( item => item! )];

    public static IReadOnlyList<JsonNode> WebUrls => [.. Document.Value["web_urls"]!.AsArray( ).Select( item => item! )];

    public static IReadOnlyList<MentionUser> Users => CorpusResolver.Value.Users;

    public static IAttachableResolver Resolver => CorpusResolver.Value;

    public static JsonNode Case( string name ) => CasesByName.Value[name];

    public static string Name( JsonNode testCase ) => testCase["name"]!.GetValue<string>( );

    public static string Body( JsonNode testCase ) => testCase["body"]!.GetValue<string>( );

    public static RichTextContext ContextFor( JsonNode testCase ) => new( Resolver, testCase["host"]?.GetValue<string>( ) );

    /// <summary>The <c>ok</c> value of an outcome (<c>null</c> when Rails returned nil), or <c>Raised</c> when Rails raised.</summary>
    public static (bool Raised, JsonNode? Value) Outcome( JsonNode? outcome )
    {
        if ( outcome is not JsonObject result )
        {
            throw new InvalidDataException( "An outcome is an object." );
        }

        return result.ContainsKey( "error" ) ? (true, null) : (false, result["ok"]);
    }

    private static JsonNode Load( ) => RepositoryPaths.ReadVectorJson( "richtext", "expected.json" );
}
