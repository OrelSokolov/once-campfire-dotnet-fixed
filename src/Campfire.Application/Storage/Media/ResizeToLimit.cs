namespace Campfire.Application.Storage.Media;

/// <summary>One <c>resize_to_limit(width, height)</c> step; a missing dimension is unbounded.</summary>
public readonly record struct ResizeToLimit( int? Width, int? Height );
