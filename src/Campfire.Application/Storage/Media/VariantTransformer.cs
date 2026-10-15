using System.Text.Json.Nodes;

namespace Campfire.Application.Storage.Media;

public static class VariantTransformer
{
    public static IReadOnlyList<ResizeToLimit> Operations( Variation variation )
    {
        if ( variation["resize_to_limit"] is null )
        {
            return [];
        }

        if ( variation["resize_to_limit"] is not JsonArray { Count: 2 } dimensions )
        {
            throw new StorageException( StorageFailure.InvalidVariation, "resize_to_limit requires two dimensions." );
        }

        var width = Dimension( dimensions[0] );
        var height = Dimension( dimensions[1] );
        if ( width is null && height is null )
        {
            throw new StorageException( StorageFailure.InvalidVariation, "Specify at least one dimension." );
        }

        return [new ResizeToLimit( width, height )];
    }
    private static int? Dimension( JsonNode? value )
    {
        if ( value is null )
        {
            return null;
        }

        if ( value is not JsonValue scalar || !TryDimension( scalar, out var dimension ) || dimension <= 0 )
        {
            throw new StorageException( StorageFailure.InvalidVariation, "Dimensions must be positive integers." );
        }

        return dimension;
    }

    private static bool TryDimension( JsonValue value, out int dimension )
    {
        if ( value.TryGetValue<int>( out dimension ) )
        {
            return true;
        }

        if ( value.TryGetValue<long>( out var longDimension ) && longDimension is > 0 and <= int.MaxValue )
        {
            dimension = ( int ) longDimension;
            return true;
        }

        return false;
    }
}
