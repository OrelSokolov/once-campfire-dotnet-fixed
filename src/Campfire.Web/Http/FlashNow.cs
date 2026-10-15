using System.Text.Json.Nodes;

namespace Campfire.Web.Http;

/// <summary><c>ActionDispatch::Flash::FlashNow</c>: <c>flash.now[key] = value</c>.</summary>
public sealed class FlashNow
{
    private readonly Flash _flash;

    internal FlashNow( Flash flash )
    {
        _flash = flash;
    }

    public JsonNode? this[string key]
    {
        get => _flash[key];
        set
        {
            _flash[key] = value;
            _flash.Discard( key );
        }
    }

    public string? Notice
    {
        get => _flash.Notice;
        set => this["notice"] = value;
    }

    public string? Alert
    {
        get => _flash.Alert;
        set => this["alert"] = value;
    }
}
