namespace Campfire.Web.Views.Caching;

/// <summary>The observer's lock waits so far: how many, their total in <c>Stopwatch</c> ticks, and per-bucket counts.</summary>
internal sealed record CaptureWaitSnapshot( long Count, long Ticks, long[] Buckets );
