namespace Campfire.Application.Data;

/// <summary>Turns a lookup that found nothing into Rails' <c>ActiveRecord::RecordNotFound</c>.</summary>
internal static class RecordNotFoundExtensions
{
    /// <summary>The row, or <see cref="RecordNotFoundException"/> for <paramref name="model"/> when there is none.</summary>
    /// <exception cref="RecordNotFoundException">The row is null.</exception>
    internal static T OrNotFound<T>( this T? row, string model ) where T : class => row ?? throw new RecordNotFoundException( model );
}
