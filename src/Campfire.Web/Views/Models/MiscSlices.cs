namespace Campfire.Web.Views.Models;

/// <summary><c>sessions/new</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="EmailAddress"><c>params[:email_address]</c>.</param>
/// <param name="HelpContact"><c>User.administrator.first</c>, for <c>accounts/_help_contact</c>.</param>
public sealed record SessionsNewPage( ViewContext ViewContext, string? EmailAddress, HelpContact? HelpContact, string? ReturnUrl = null ) : IViewModel;

/// <summary><c>sessions/transfers/show</c>: an auto-submitting form that PUTs back to the page's own URL.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="Action">The request path, <c>session_transfer_path(params[:id])</c> (<c>url_for({})</c>).</param>
public sealed record SessionTransferPage( ViewContext ViewContext, string Action ) : IViewModel;

/// <summary><c>first_runs/show</c>: account setup, shown until the first user exists.</summary>
public sealed record FirstRunPage( ViewContext ViewContext ) : IViewModel;

/// <summary><c>welcome/show</c>: shown to users who aren't in any room yet.</summary>
public sealed record WelcomePage( ViewContext ViewContext ) : IViewModel;

/// <summary><c>autocompletable/users/index</c>: <c>&lt;lexxy-prompt-item&gt;</c>s for the mentions prompt, without a layout.</summary>
public sealed record AutocompletableUsersPage( ViewContext ViewContext, IReadOnlyList<MentionUserView> Users ) : IViewModel;

/// <summary><c>autocompletable/users/_prompt_item</c>.</summary>
public sealed record PromptItemPartial( ViewContext ViewContext, MentionUserView User ) : IViewModel;

/// <summary>
/// <c>ActionText::Attachment::OpengraphEmbed</c> (<c>lib/rails_ext/actiontext_opengraph_embeds.rb</c>), with
/// <paramref name="Href"/> and <paramref name="Url"/> already vetted by <c>web_url</c>.
/// </summary>
public sealed record OpengraphEmbed( string? Href, string? Url, string Filename, string? Description )
{
    /// <summary><c>twitter_avatar?</c>.</summary>
    public bool TwitterAvatar => (Url ?? string.Empty).StartsWith( "https://pbs.twimg.com/profile_images", StringComparison.Ordinal );
}

/// <summary><c>action_text/attachables/_opengraph_embed</c>.</summary>
public sealed record OpengraphEmbedPartial( OpengraphEmbed OpengraphEmbed );

/// <summary><c>pwa/manifest.json.erb</c>.</summary>
/// <param name="ViewContext">The view context (its base URL and assets make <c>image_url</c>).</param>
/// <param name="AccountName"><c>Current.account&amp;.name</c>.</param>
/// <param name="LogoPathSmall"><c>fresh_account_logo_path(size: :small)</c>.</param>
/// <param name="LogoPath"><c>fresh_account_logo_path</c>.</param>
public sealed record ManifestPage( ViewContext ViewContext, string? AccountName, string LogoPathSmall, string LogoPath ) : IViewModel;
