namespace Campfire.Web.Http;

/// <summary>The doorkeeper's charged bytes, occupied slots and replacements, for metrics.</summary>
internal readonly record struct GzipDoorkeeperSnapshot( long Bytes, int Count, long Turnover );
