use std::{collections::BTreeMap, sync::OnceLock};

pub fn locale_from_language_id(id: u16) -> &'static str {
    if id & 0x03ff == 0x04 { "zh-CN" } else { "en" }
}

pub fn locale() -> &'static str {
    static LOCALE: OnceLock<&'static str> = OnceLock::new();
    LOCALE.get_or_init(|| {
        #[link(name = "kernel32")]
        unsafe extern "system" {
            fn GetUserDefaultUILanguage() -> u16;
        }
        locale_from_language_id(unsafe { GetUserDefaultUILanguage() })
    })
}

pub fn messages() -> &'static BTreeMap<String, String> {
    static MESSAGES: OnceLock<BTreeMap<String, String>> = OnceLock::new();
    MESSAGES.get_or_init(|| {
        serde_json::from_str(include_str!("../locales/en.json"))
            .expect("bundled English translations")
    })
}

pub fn text(source: &str) -> &str {
    if locale() == "en" {
        messages().get(source).map(String::as_str).unwrap_or(source)
    } else {
        source
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn windows_ui_language_including_chinese_variants() {
        for id in [0x0804, 0x0404, 0x0c04, 0x1004, 0x1404] {
            assert_eq!(locale_from_language_id(id), "zh-CN");
        }
        for id in [0x0409, 0x0809, 0x0411, 0x040c, 0] {
            assert_eq!(locale_from_language_id(id), "en");
        }
        assert_eq!(messages()["随译 · 设置"], "Suiyi · Settings");
    }
}
