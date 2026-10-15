using Campfire.Application.Users;
using Campfire.Web.Paths;
using Microsoft.Extensions.Primitives;

namespace Campfire.Web.Http.Inputs;

/// <summary>Read ASP.NET-parsed fields; this is not a second URL-encoding or multipart parser.</summary>
public static class FrontendInputs
{
    public const int MaximumNesting = 4;

    public static async Task<FrontendFields> ReadAsync( HttpContext httpContext )
    {
        var formFields = new InputObject( );
        if ( httpContext.Request.HasFormContentType && !BotRequestBody.HasRawBody( httpContext ) )
        {
            var form = await httpContext.Request.ReadFormAsync( httpContext.RequestAborted );
            foreach ( var (name, values) in form )
            {
                AddValues( formFields, name, values );
            }

            foreach ( var file in form.Files )
            {
                if ( file.FileName.Length == 0 && file.Length == 0 )
                {
                    continue;
                }

                var uploaded = await UploadedFile.FromFormFileAsync( file, httpContext.RequestAborted );
                httpContext.Response.RegisterForDispose( uploaded );
                Add( formFields, file.Name, new InputFile( uploaded ) );
            }
        }

        var queryFields = new InputObject( );
        foreach ( var (name, values) in httpContext.Request.Query )
        {
            AddValues( queryFields, name, values );
        }

        var result = new InputObject( );
        result.Merge( queryFields );
        result.Merge( formFields );
        return new FrontendFields( result, formFields, queryFields );
    }

    private static void AddValues( InputObject result, string name, StringValues values )
    {
        if ( name == ParamNames.Users.RoleField && values is [UserRoleNames.Member, UserRoleNames.Administrator] )
        {
            // Only the retained hidden/checked role pair has a defined duplicate meaning.
            Add( result, name, new InputText( values[1]! ) );
            return;
        }

        foreach ( var value in values )
        {
            Add( result, name, new InputText( value ?? "" ) );
        }
    }

    private static void Add( InputObject root, string name, InputValue value )
    {
        var parts = FieldParts( name );
        var current = root;
        for ( var index = 0; index < parts.Length - 1; index++ )
        {
            var part = parts[index];
            if ( index == parts.Length - 2 && parts[^1].Length == 0 )
            {
                if ( current[part] is null )
                {
                    current[part] = new InputList( );
                }

                var list = current[part] as InputList ?? throw new BadHttpRequestException( "Conflicting form fields.", StatusCodes.Status400BadRequest );
                list.Add( value );
                return;
            }

            if ( current[part] is null )
            {
                current[part] = new InputObject( );
            }

            if ( current[part] is not InputObject nested )
            {
                throw new BadHttpRequestException( "Conflicting form fields.", StatusCodes.Status400BadRequest );
            }

            current = nested;
        }

        if ( current.ContainsKey( parts[^1] ) )
        {
            throw new BadHttpRequestException( "Duplicate or conflicting form field.", StatusCodes.Status400BadRequest );
        }

        current[parts[^1]] = value;
    }

    private static string[] FieldParts( string name )
    {
        var parts = name.Split( '[' );
        if ( parts.Length > MaximumNesting || parts[0].Length == 0 || parts[0].Contains( ']' ) )
        {
            throw new BadHttpRequestException( "Invalid form field.", StatusCodes.Status400BadRequest );
        }

        for ( var index = 1; index < parts.Length; index++ )
        {
            if ( !parts[index].EndsWith( ']' ) )
            {
                throw new BadHttpRequestException( "Invalid form field.", StatusCodes.Status400BadRequest );
            }

            parts[index] = parts[index][..^1];
            if ( parts[index].Contains( ']' ) || (parts[index].Length == 0 && index != parts.Length - 1) )
            {
                throw new BadHttpRequestException( "Invalid form field.", StatusCodes.Status400BadRequest );
            }
        }

        return parts;
    }
}
