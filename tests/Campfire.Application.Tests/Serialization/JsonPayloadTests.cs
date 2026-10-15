using System.Text;
using Campfire.Application.Serialization;

namespace Campfire.Application.Tests.Serialization;

public sealed class JsonPayloadTests
{
    [Fact]
    public void Script_json_keeps_literal_unicode_quotes_and_plus_but_escapes_html_and_line_separators( )
    {
        var json = JsonPayload.EncodeString( "é' + <>&\u2028\u2029" );

        Assert.Equal( "\"é' + \\u003c\\u003e\\u0026\\u2028\\u2029\"", json );
        Assert.Equal( json, Encoding.UTF8.GetString( JsonUtf8.EncodeString( Encoding.UTF8.GetBytes( "é' + <>&\u2028\u2029" ) ) ) );
    }
}
