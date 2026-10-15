use std::ffi::OsStr;
use std::sync::OnceLock;

static ENABLED: OnceLock<bool> = OnceLock::new();

pub fn initialize() {
    ENABLED.set(configured(std::env::var_os("LOADGEN_DEBUG").as_deref()))
        .expect("debug logging initialized once per command");
}

pub fn enabled() -> bool {
    *ENABLED.get().expect("debug logging initialized before work")
}

// Preserve explicit nonempty debug settings; an empty environment value is off.
fn configured(value: Option<&OsStr>) -> bool {
    value.is_some_and(|value| !value.is_empty())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_or_missing_debug_setting_is_off() {
        assert!(!configured(None));
        assert!(!configured(Some(OsStr::new(""))));
    }

    #[test]
    fn explicit_debug_setting_is_on() {
        assert!(configured(Some(OsStr::new("1"))));
    }
}
