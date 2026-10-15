#!/usr/bin/env dotnet
// Regenerates params interpolation vectors with the pinned Rails image's Ruby and Action Pack.
using System.Diagnostics;

var root = Path.GetFullPath( Path.Combine( ( string ) AppContext.GetData( "EntryPointFileDirectoryPath" )!, "../.." ) );
var start = new ProcessStartInfo( "docker", ["run", "--rm", "-i", "--entrypoint", "bundle", "campfire-reference:app", "exec", "ruby", "-raction_controller", "-rjson"] )
{
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    UseShellExecute = false,
};
using var process = Process.Start( start )!;
await process.StandardInput.WriteAsync( """
    values = [
      "plain", "", nil, true, false, 42, -2.5,
      ["j", "x"], {"a" => "j"}, {"nested" => {"x" => ["j", 4, true]}},
      ["quote\"", "back\\slash", "line\n", "tab\t", "\a\b\v\f\r\e", "\u0000\u001f\u007f", 'a#{b}', 'a#@b', 'a#$b', "é☃"],
      [], {}, [[], {}]
    ]
    rows = values.map do |value|
      params = ActionController::Parameters.new("query" => value)
      { "input" => value, "output" => params[:query].to_s }
    end
    puts JSON.pretty_generate({ "ruby" => RUBY_VERSION, "action_pack" => ActionPack::VERSION::STRING, "cases" => rows })
    """ );
process.StandardInput.Close( );
var output = await process.StandardOutput.ReadToEndAsync( );
await process.WaitForExitAsync( );
if ( process.ExitCode != 0 )
{
    return process.ExitCode;
}

var destination = Path.Combine( root, "vectors", "params", "interpolation.json" );
Directory.CreateDirectory( Path.GetDirectoryName( destination )! );
await File.WriteAllTextAsync( destination, output );
Console.WriteLine( destination );
return 0;
