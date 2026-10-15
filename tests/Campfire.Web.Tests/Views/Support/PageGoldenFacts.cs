using System.Text.Json.Nodes;
using Campfire.Application.Users;
using Campfire.Web.Views;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Tests.Views.Support;

/// <summary>
/// The page goldens: the HTML the Rails app rendered per case under <c>vectors/views/a/</c> (views A, written by
/// <c>reference-tools/views/a/render.rb</c>) and their <c>facts.json</c>, from which this builds the
/// <see cref="ViewContext"/> a case was rendered with (Rust <c>crates/views/tests/support/facts.rs</c>).
/// </summary>
internal static class PageGoldenFacts
{
    public const string Token = "TOKEN";

    private static readonly Lazy<JsonNode> Facts = new( ( ) => RepositoryPaths.ReadVectorJson( "views", "a", "facts.json" ) );

    public static JsonNode Root => Facts.Value;

    public static string BaseUrl => Root["base_url"].RequiredString( );

    public static string GoldenPath( string file ) => RepositoryPaths.VectorPath( "views", "a", file );

    // Header-based forgery protection retains empty tags. Unknown values on the actual side still fail.
    public static string Golden( string name, string extension ) => File.ReadAllText( GoldenPath( $"{name}.{extension}" ) )
        .Replace( "name=\"csrf-param\" content=\"authenticity_token\"", "name=\"csrf-param\" content=\"\"", StringComparison.Ordinal );

    public static JsonNode Case( string name ) => Required( Root["cases"]![name], $"No case {name}." );

    public static JsonNode User( string caseName, string userName ) =>
        Required( Case( caseName )["users"]![userName], $"No user {userName} in case {caseName}." );

    public static JsonNode UserByEmail( string caseName, string email ) =>
        Case( caseName )["users"]!.AsObject( ).Select( pair => pair.Value! ).First( user => user["email_address"].OptionalString( ) == email );

    public static JsonNode Account( string caseName ) => Case( caseName )["account"]!;

    public static JsonNode Data( string caseName ) => Case( caseName )["data"]!;

    /// <summary>A value the facts must have.</summary>
    public static T Required<T>( T? value, string missing )
        where T : class
    {
        if ( value is null )
        {
            throw new InvalidOperationException( missing );
        }

        return value;
    }

    public static Func<string, string> AssetPath { get; } = logical => Required( Root["assets"]![logical].OptionalString( ), $"Unknown asset {logical}." );

    public static AssetTagsView Assets =>
        new( AssetPath, SafeHtml.Raw( Root["stylesheet_tags"].OptionalString( ) ), SafeHtml.Raw( Root["importmap_tags"].OptionalString( ) ) );

    public static Platform Platform( string label )
    {
        var platform = Root["platforms"]![label]!;
        return new Platform
        {
            Ios = platform["ios"].RequiredBool( ),
            Android = platform["android"].RequiredBool( ),
            Mac = platform["mac"].RequiredBool( ),
            Windows = platform["windows"].RequiredBool( ),
            Chrome = platform["chrome"].RequiredBool( ),
            Firefox = platform["firefox"].RequiredBool( ),
            Safari = platform["safari"].RequiredBool( ),
            Edge = platform["edge"].RequiredBool( ),
            Mobile = platform["mobile"].RequiredBool( ),
            Desktop = platform["desktop"].RequiredBool( ),
            AppleMessages = platform["apple_messages"].RequiredBool( ),
            Browser = platform["browser"].OptionalString( ) ?? string.Empty,
            OperatingSystem = platform["operating_system"].OptionalString( ) ?? string.Empty,
        };
    }

    /// <summary>The context the reference app had for case <paramref name="name"/>.</summary>
    public static ViewContext Context( string name, PageGoldenRequest? request = null )
    {
        request ??= new PageGoldenRequest( );
        var @case = Case( name );

        // Partials rendered with ApplicationController.renderer (broadcasts) have no CSRF tokens.
        var current = @case["as"].OptionalString( ) is { } email ? UserByEmail( name, email ) : null;
        var account = @case["account"];
        return new ViewContext
        {
            CurrentUser = current is null
                ? null
                : new CurrentUserView(
                    current["id"].RequiredLong( ),
                    current["name"].RequiredString( ),
                    current["role"].OptionalString( ) == "administrator",
                    current["role"].OptionalString( ) == "bot",
                    current["avatar_path"].RequiredString( ) ),
            Account = new AccountSummary(
                account?["name"].OptionalString( ) ?? string.Empty,
                account?["logo_path"].OptionalString( ) ?? "/account/logo",
                account?["has_logo"]?.GetValue<bool>( ) ?? false ),
            IncludeCsrfTags = !request.Partial,
            FlashNotice = request.FlashNotice,
            FlashAlert = request.FlashAlert,
            Platform = Platform( @case["ua"].RequiredString( ) ),
            VapidPublicKey = Root["vapid_public_key"].OptionalString( ),
            Assets = Assets,
            CustomStyles = account?["custom_styles"].OptionalString( ),
            CableUrl = "/cable",
            BaseUrl = BaseUrl,
            RequestUrl = BaseUrl + @case["path"].OptionalString( ),
            Referrer = @case["referrer"].OptionalString( ),
            LastRoomVisitedId = current?["original_room_id"] is JsonValue room ? room.GetValue<long>( ) : null,
            AppVersion = Root["app_version"].RequiredString( ),
        };
    }

    /// <summary>A user in case <paramref name="caseName"/>, as the views see them.</summary>
    public static UserSummary Summary( JsonNode user ) => new( )
    {
        Id = user["id"].RequiredLong( ),
        Name = user["name"].RequiredString( ),
        Bio = user["bio"].OptionalString( ),
        EmailAddress = user["email_address"].OptionalString( ),
        Role = user["role"].OptionalString( ) switch
        {
            "administrator" => UserRole.Administrator,
            "bot" => UserRole.Bot,
            _ => UserRole.Member,
        },
        Status = user["status"].OptionalString( ) switch
        {
            "deactivated" => UserStatus.Deactivated,
            "banned" => UserStatus.Banned,
            _ => UserStatus.Active,
        },
        AvatarPath = user["avatar_path"].RequiredString( ),
    };

    public static UserSummary Named( string caseName, string userName ) => Summary( User( caseName, userName ) );

    /// <summary>All users in the case, <c>User.ordered</c> (by lower-cased name).</summary>
    public static List<UserSummary> OrderedUsers( string caseName ) =>
        Case( caseName )["users"]!.AsObject( )
            .Select( pair => Summary( pair.Value! ) )
            .OrderBy( user => user.Name.ToLowerInvariant( ), StringComparer.Ordinal )
            .ToList( );

    public static HelpContact HelpContact( string caseName )
    {
        var david = User( caseName, "David" );
        return new HelpContact( david["name"].RequiredString( ), david["email_address"].RequiredString( ) );
    }

    public static MentionUserView Mention( string caseName, string userName ) =>
        new( Named( caseName, userName ), User( caseName, userName )["attachable_sgid"].RequiredString( ) );
}
