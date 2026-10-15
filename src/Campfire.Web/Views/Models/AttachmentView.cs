using Campfire.Web.Views.Helpers;

namespace Campfire.Web.Views.Models;

/// <summary>A message attachment.</summary>
/// <param name="Filename"><c>attachment.filename.to_s</c>.</param>
/// <param name="BlobPath"><c>rails_blob_path(attachment)</c>.</param>
/// <param name="DownloadPath"><c>rails_blob_path(attachment, disposition: "attachment")</c>.</param>
/// <param name="Preview">How it previews.</param>
/// <param name="Width"><c>metadata[:width]</c>: an Integer for images, a Float for videos.</param>
/// <param name="Height"><c>metadata[:height]</c>.</param>
public sealed record AttachmentView( string Filename, string BlobPath, string DownloadPath, AttachmentPreview Preview, RubyNumber? Width, RubyNumber? Height );
