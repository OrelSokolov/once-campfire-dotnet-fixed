namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// The tests around the capture waits the <c>Campfire.Freshness</c> meter reports. They run alone: a
/// <c>MeterListener</c> one of them starts sees the instruments of every app in the process, not only its own test's.
/// </summary>
[CollectionDefinition( "Observer listener", DisableParallelization = true )]
public sealed class ObserverListenerGroup;
