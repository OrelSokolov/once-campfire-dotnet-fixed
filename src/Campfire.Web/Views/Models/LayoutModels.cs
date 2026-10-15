namespace Campfire.Web.Views.Models;

/// <summary><c>layouts/mailer.html.erb</c> around a rendered, safe mail body.</summary>
public sealed record MailerLayout( SafeHtml Content );

/// <summary><c>layouts/mailer.text.erb</c> around a rendered text body.</summary>
public sealed record MailerTextLayout( string Content );

/// <summary><c>layouts/action_text/contents/_content.html.erb</c>: the wrapper around rendered, sanitized rich text.</summary>
public sealed record ActionTextContent( SafeHtml Content );
