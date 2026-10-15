using Campfire.Web.Http;
using Campfire.Web.Http.Inputs;

namespace Campfire.Web.Endpoints;

/// <summary>Strong parameters: <c>params.require(key)</c> for the forms, whose values must be hashes.</summary>
public static class StrongParams
{
    /// <summary>
    /// <c>params.require(key).permit(*filters)</c>: a missing or blank value is <c>ParameterMissing</c> (400); a scalar
    /// has no <c>permit</c> (<c>NoMethodError</c>, a 500).
    /// </summary>
    public static InputObject Permit( RequestContext requestContext, string key, params IEnumerable<InputField> filters )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        var required = requestContext.Params.Require( key );
        if ( required is InputObject map )
        {
            return map.Permit( filters );
        }

        if ( required is InputText )
        {
            throw new InvalidOperationException( UndefinedMethodErrors.PermitForString );
        }

        throw new InvalidOperationException( $"undefined method 'permit' for an instance of {required.GetType( ).Name}" );
    }

    /// <summary>A permitted scalar as the string column stores it (<c>nil</c> stays nil; numbers and booleans become text).</summary>
    public static string? Scalar( InputValue? value ) => value switch
    {
        null or InputNull => null,
        _ => value.AsString ?? value.ToText( ),
    };
}
