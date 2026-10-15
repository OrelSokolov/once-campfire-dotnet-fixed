use regex::Regex;

/// The attachment image in a message response, after the author's avatar.
pub struct AttachmentImages {
    images: Regex,
    attributes: Regex,
}

impl AttachmentImages {
    pub fn new() -> Self {
        Self {
            images: Regex::new(r#"<img\b[^>]*>"#).unwrap(),
            attributes: Regex::new(r#"(?:^|\s)(class|src)\s*=\s*"([^"]*)""#).unwrap(),
        }
    }

    pub fn source<'a>(&self, html: &'a str) -> Option<&'a str> {
        for image in self.images.find_iter(html) {
            let mut source = None;
            let mut attachment = false;
            for attribute in self.attributes.captures_iter(image.as_str()) {
                match &attribute[1] {
                    "class" => {
                        attachment = attribute[2]
                            .split_ascii_whitespace()
                            .any(|class| class == "message__attachment")
                    }
                    "src" => source = attribute.get(2).map(|value| value.as_str()),
                    _ => unreachable!(),
                }
            }
            if attachment {
                return source;
            }
        }
        None
    }
}

/// The fixture is raster media; an avatar SVG or an HTML error page is not its thumbnail.
pub fn is_raster(bytes: &[u8], content_type: &str) -> bool {
    match content_type.split(';').next().unwrap_or("").trim() {
        "image/jpeg" => bytes.starts_with(&[0xff, 0xd8, 0xff]),
        "image/png" => bytes.starts_with(b"\x89PNG\r\n\x1a\n"),
        "image/gif" => bytes.starts_with(b"GIF87a") || bytes.starts_with(b"GIF89a"),
        "image/webp" => bytes.starts_with(b"RIFF") && bytes.get(8..12) == Some(b"WEBP"),
        _ => false,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn selects_attachment_after_author_avatar() {
        let html = r#"<img src="/users/7/avatar"><img class="message__attachment" loading="lazy" src="/rails/active_storage/representations/redirect/blob/thumb/file.jpg">"#;
        assert_eq!(
            AttachmentImages::new().source(html),
            Some("/rails/active_storage/representations/redirect/blob/thumb/file.jpg")
        );
    }

    #[test]
    fn finds_class_token_with_src_before_class() {
        let html = r#"<img class="message__attachment-extra" src="/wrong"><img src="/thumb?a=1&amp;b=2" class="rounded message__attachment">"#;
        assert_eq!(
            AttachmentImages::new().source(html),
            Some("/thumb?a=1&amp;b=2")
        );
    }

    #[test]
    fn avatar_only_is_not_a_successful_attachment() {
        assert_eq!(
            AttachmentImages::new().source(r#"<img src="/users/7/avatar">"#),
            None
        );
    }

    #[test]
    fn data_attributes_do_not_impersonate_class_or_src() {
        let html = r#"<img data-class="message__attachment" src="/wrong"><img class="message__attachment" data-src="/wrong" src="/thumb">"#;
        assert_eq!(AttachmentImages::new().source(html), Some("/thumb"));
    }

    #[test]
    fn rejects_svg_and_html_disguised_as_raster_media() {
        assert!(!is_raster(b"<svg></svg>", "image/svg+xml; charset=utf-8"));
        assert!(!is_raster(b"<html>Error</html>", "image/jpeg"));
        assert!(is_raster(&[0xff, 0xd8, 0xff, 0], "image/jpeg"));
        assert!(is_raster(b"\x89PNG\r\n\x1a\nbytes", "image/png"));
    }
}
