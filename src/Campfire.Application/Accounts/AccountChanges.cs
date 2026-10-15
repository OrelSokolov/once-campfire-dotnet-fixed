using Campfire.Application.Data;
using Campfire.Application.Data.Records;

namespace Campfire.Application.Accounts;

/// <summary>Attributes for <c>@account.update!(account_params)</c>; an unset member leaves that attribute alone.</summary>
public sealed record AccountChanges
{
    public Assigned<string> Name { get; init; }

    public Assigned<string?> CustomStyles { get; init; }

    /// <summary>Form values for <c>settings: {}</c>, cast by the settings schema.</summary>
    public IReadOnlyDictionary<string, string?>? Settings { get; init; }

    /// <summary>A new logo replaces the current one, whose blob is purged after commit.</summary>
    public NewAttachment? Logo { get; init; }

    /// <summary>A directly uploaded blob, given by its signed id, replaces the current logo; nothing is uploaded.</summary>
    public SignedBlobAttachment? LogoBlob { get; init; }
}
