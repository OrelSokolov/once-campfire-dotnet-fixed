namespace Campfire.Web.Http;

/// <summary>What a piece lookup decided: reuse a stored piece, compress and store it, or compress it for this response only.</summary>
internal enum GzipAdmission
{
    Hit,
    Admit,
    OneOff,
}
