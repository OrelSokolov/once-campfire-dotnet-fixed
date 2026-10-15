namespace Campfire.Application.Tests.Fixtures;

/// <summary>What was loaded: table, then label, to id.</summary>
internal sealed class LoadedFixtures
{
    public Dictionary<string, Dictionary<string, long>> Ids { get; } = new( StringComparer.Ordinal );

    public long Id( string table, string label ) => Ids[table][label];
}
