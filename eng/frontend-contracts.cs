#!/usr/bin/env dotnet
// Static inventory only. Dynamic selectors and possible matches need browser confirmation.
// dotnet eng/frontend-contracts.cs OUTPUT.md
#:property RestorePackagesWithLockFile=false
#:include lib/Repository.cs

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

if ( args.Length != 1 )
{
    Console.Error.WriteLine( "Usage: frontend-contracts.cs OUTPUT.md" );
    return 2;
}

var root = Repository.Root;
var scriptRoot = Path.Combine( root, "reference/app/javascript" );
var templates = Directory.GetFiles( Path.Combine( root, "src/Campfire.Web/Views" ), "*", SearchOption.AllDirectories )
    .Where( file => file.EndsWith( ".cshtml", StringComparison.Ordinal ) || file.EndsWith( ".cs", StringComparison.Ordinal ) )
    .Select( file => (File: Path.GetRelativePath( root, file ), Text: File.ReadAllText( file )) ).ToArray( );
var inventory = new List<(string Source, int Line, string Kind, string Contract, string Matches)>( );
var scripts = Directory.GetFiles( scriptRoot, "*.js", SearchOption.AllDirectories ).Order( ).ToArray( );
// A Stimulus controller's `static targets = [...]` (or values, classes, outlets) declaration.
const string staticDeclaration = @"static\s+(targets|values|classes|outlets)\s*=\s*(\[[\s\S]*?\]|\{[\s\S]*?\})";
foreach ( var file in scripts )
{
    var source = Path.GetRelativePath( root, file );
    var text = File.ReadAllText( file );
    var controller = Path.GetFileNameWithoutExtension( file ).Replace( "_controller", "", StringComparison.Ordinal ).Replace( '_', '-' );
    foreach ( Match match in Regex.Matches( text, staticDeclaration, RegexOptions.CultureInvariant ) )
    {
        var kind = match.Groups[1].Value;
        var body = match.Groups[2].Value;
        var names = kind == "values"
            ? Regex.Matches( body, @"(?:\{|,)\s*([a-zA-Z_$][\w$]*)\s*:" ).Select( item => item.Groups[1].Value )
            : Regex.Matches( body, "[\"']([^\"']+)[\"']" ).Select( item => item.Groups[1].Value );
        foreach ( var name in names )
        {
            var suffix = kind switch
            {
                "targets" => "target",
                "values" => name + "-value",
                "classes" => name + "-class",
                _ => name + "-outlet",
            };
            var contract = "data-" + controller + "-" + suffix;
            Add( source, text, match.Index, "static " + kind, contract, name );
        }
    }

    foreach ( Match match in Regex.Matches( text, "data-[a-z0-9_-]+(?:-target|-action|-value|-class|-outlet)?", RegexOptions.CultureInvariant ) )
    {
        Add( source, text, match.Index, "attribute", match.Value, match.Value );
    }

    foreach ( Match match in Regex.Matches( text, @"querySelector(?:All)?\s*\(([^\r\n]+)", RegexOptions.CultureInvariant ) )
    {
        var expression = match.Groups[1].Value;
        var literal = Regex.Match( expression, "^[\"']([^\"']+)[\"']" );
        Add( source, text, match.Index, literal.Success ? "selector" : "dynamic selector", expression,
            literal.Success ? literal.Groups[1].Value : null );
    }

    foreach ( Match match in Regex.Matches( text, @"\.dataset\.([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant ) )
    {
        var name = Regex.Replace( match.Groups[1].Value, "[A-Z]", letter => "-" + letter.Value.ToLowerInvariant( ) );
        Add( source, text, match.Index, "dataset", "data-" + name, name );
    }
}

var output = new StringBuilder( "# Frontend contract inventory\n\n"
    + "Static source matches are candidates, not DOM proof. "
    + "Unmatched/dynamic entries require a browser flow or a documented client-generated source.\n\n" );
output.AppendLine( $"Scripts: {scripts.Length}; view/helper sources: {templates.Length}; contract occurrences: {inventory.Count}." );
output.AppendLine( "\n| JavaScript | Kind | Required contract | Possible template/helper matches |\n|---|---|---|---|" );
foreach ( var item in inventory.OrderBy( item => item.Source ).ThenBy( item => item.Line ) )
{
    output.AppendLine( $"| {Escape( item.Source )}:{item.Line} | {item.Kind} | `{Escape( item.Contract )}` | {Escape( item.Matches )} |" );
}

output.AppendLine( "\n## Source identities\n" );
foreach ( var file in scripts.Concat( templates.Select( item => Path.Combine( root, item.File ) ) ).Order( ) )
{
    output.AppendLine( $"- `{Convert.ToHexStringLower( SHA256.HashData( File.ReadAllBytes( file ) ) )}` {Path.GetRelativePath( root, file )}" );
}

var destination = Path.GetFullPath( args[0] );
Directory.CreateDirectory( Path.GetDirectoryName( destination )! );
File.WriteAllText( destination, output.ToString( ) );
Console.WriteLine( $"Inventory generated: {inventory.Count} occurrences; browser confirmation pending." );
return 0;

void Add( string file, string text, int offset, string kind, string contract, string? needle )
{
    var line = text.AsSpan( 0, offset ).Count( '\n' ) + 1;
    var matches = needle is null ? [] : TemplatesMentioning( contract, needle );
    var resolution = matches.Length == 0 ? "UNRESOLVED / client-generated" : string.Join( ", ", matches );
    inventory.Add( (file, line, kind, contract, resolution) );
}

// The view/helper sources holding the contract (as written, or with underscores) or the bare name.
string[] TemplatesMentioning( string contract, string needle ) => templates
    .Where( template => template.Text.Contains( contract, StringComparison.Ordinal )
        || template.Text.Contains( contract.Replace( '-', '_' ), StringComparison.Ordinal )
        || template.Text.Contains( needle, StringComparison.Ordinal ) )
    .Select( template => template.File )
    .ToArray( );

static string Escape( string text ) => text.Replace( "|", "&#124;", StringComparison.Ordinal ).Replace( "`", "&#96;", StringComparison.Ordinal );
