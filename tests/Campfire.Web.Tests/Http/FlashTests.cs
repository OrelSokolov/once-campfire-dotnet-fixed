using System.Text.Json.Nodes;
using Campfire.Web.Http;

namespace Campfire.Web.Tests.Http;

/// <summary><c>FlashHash</c>, ported from Rust <c>crates/kit/src/session.rs</c> tests at <c>3fe9f61</c>.</summary>
public sealed class FlashTests
{
    [Fact]
    public void A_flash_round_trips_and_is_discarded_after_one_request( )
    {
        var flash = new Flash { Notice = "✓" };

        var stored = flash.ToSessionValue( );
        Assert.True( JsonNode.DeepEquals( JsonNode.Parse( "{\"discard\":[],\"flashes\":{\"notice\":\"✓\"}}" ), stored ) );

        var next = Flash.FromSessionValue( stored );
        Assert.Equal( "✓", next.Notice );
        Assert.Null( next.ToSessionValue( ) );
    }

    [Fact]
    public void Flash_now_is_not_persisted( )
    {
        var flash = new Flash( );

        flash.Now.Alert = "Too many requests or unauthorized.";

        Assert.Equal( "Too many requests or unauthorized.", flash.Alert );
        Assert.Null( flash.ToSessionValue( ) );
    }

    [Fact]
    public void Flashes_in_a_legacy_discard_list_are_dropped_and_kept_flashes_persist( )
    {
        var flash = Flash.FromSessionValue( JsonNode.Parse( "{\"discard\":[\"alert\"],\"flashes\":{\"notice\":\"hi\",\"alert\":\"gone\"}}" ) );

        Assert.Null( flash.Alert );
        flash.Keep( "notice" );

        Assert.True( JsonNode.DeepEquals( JsonNode.Parse( "{\"discard\":[],\"flashes\":{\"notice\":\"hi\"}}" ), flash.ToSessionValue( ) ) );
    }

    [Fact]
    public void Discarded_flashes_are_not_persisted_and_sweep_removes_them( )
    {
        var flash = new Flash { Notice = "a", Alert = "b" };

        flash.Discard( "alert" );
        Assert.True( JsonNode.DeepEquals( JsonNode.Parse( "{\"discard\":[],\"flashes\":{\"notice\":\"a\"}}" ), flash.ToSessionValue( ) ) );

        flash.Sweep( );
        Assert.Null( flash.Alert );
        Assert.Equal( "a", flash.Notice );
        Assert.Null( flash.ToSessionValue( ) );
    }
}
