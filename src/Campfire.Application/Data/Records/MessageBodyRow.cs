namespace Campfire.Application.Data.Records;

/// <summary>A message's stored rich-text body, accessible to the generated Dapper.AOT row reader.</summary>
internal sealed class MessageBodyRow
{
    public long RecordId { get; set; }

    public string? Body { get; set; }
}
