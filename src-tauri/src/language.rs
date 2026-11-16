use serde::{Deserialize, Deserializer, Serialize};

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum TargetLanguage {
    #[default]
    #[serde(rename = "en")]
    English,
    #[serde(rename = "zh-Hans")]
    SimplifiedChinese,
    #[serde(rename = "zh-Hant")]
    TraditionalChinese,
    #[serde(rename = "ja")]
    Japanese,
    #[serde(rename = "ko")]
    Korean,
    #[serde(rename = "fr")]
    French,
    #[serde(rename = "de")]
    German,
    #[serde(rename = "es")]
    Spanish,
    #[serde(rename = "pt")]
    Portuguese,
    #[serde(rename = "ru")]
    Russian,
    #[serde(rename = "it")]
    Italian,
    #[serde(rename = "ar")]
    Arabic,
}

#[derive(Serialize)]
pub struct Language {
    pub code: &'static str,
    pub name: &'static str,
    #[serde(skip_serializing)]
    pub prompt: &'static str,
}

pub const LANGUAGES: [Language; 12] = [
    Language {
        code: "en",
        name: "英语",
        prompt: "English",
    },
    Language {
        code: "zh-Hans",
        name: "简体中文",
        prompt: "Simplified Chinese",
    },
    Language {
        code: "zh-Hant",
        name: "繁体中文",
        prompt: "Traditional Chinese",
    },
    Language {
        code: "ja",
        name: "日语",
        prompt: "Japanese",
    },
    Language {
        code: "ko",
        name: "韩语",
        prompt: "Korean",
    },
    Language {
        code: "fr",
        name: "法语",
        prompt: "French",
    },
    Language {
        code: "de",
        name: "德语",
        prompt: "German",
    },
    Language {
        code: "es",
        name: "西班牙语",
        prompt: "Spanish",
    },
    Language {
        code: "pt",
        name: "葡萄牙语",
        prompt: "Portuguese",
    },
    Language {
        code: "ru",
        name: "俄语",
        prompt: "Russian",
    },
    Language {
        code: "it",
        name: "意大利语",
        prompt: "Italian",
    },
    Language {
        code: "ar",
        name: "阿拉伯语",
        prompt: "Arabic",
    },
];

impl TargetLanguage {
    pub fn definition(self) -> &'static Language {
        &LANGUAGES[self as usize]
    }

    // Older settings have no field. Unknown future values should preserve the API settings.
    pub fn deserialize_or_default<'de, D: Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let value = serde_json::Value::deserialize(d)?;
        Ok(serde_json::from_value(value).unwrap_or_default())
    }

    pub fn primary_default() -> Self {
        Self::from_windows_language(crate::i18n::system_language_id())
    }

    pub fn from_windows_language(id: u16) -> Self {
        match id & 0x03ff {
            0x04 if matches!(id, 0x0404 | 0x0c04 | 0x1404) => Self::TraditionalChinese,
            0x04 => Self::SimplifiedChinese,
            0x11 => Self::Japanese,
            0x12 => Self::Korean,
            0x0c => Self::French,
            0x07 => Self::German,
            0x0a => Self::Spanish,
            0x16 => Self::Portuguese,
            0x19 => Self::Russian,
            0x10 => Self::Italian,
            0x01 => Self::Arabic,
            _ => Self::English,
        }
    }

    pub fn other_default(self) -> Self {
        if self == Self::English {
            Self::SimplifiedChinese
        } else {
            Self::English
        }
    }

    pub fn deserialize_primary<'de, D: Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        let value = serde_json::Value::deserialize(d)?;
        Ok(serde_json::from_value(value).unwrap_or_else(|_| Self::primary_default()))
    }

    pub fn sample(self) -> &'static str {
        match self {
            Self::English => "Hello, world.",
            Self::SimplifiedChinese | Self::TraditionalChinese => "你好，世界。",
            Self::Japanese => "こんにちは、世界。",
            Self::Korean => "안녕하세요, 세계.",
            Self::French => "Bonjour le monde.",
            Self::German => "Hallo, Welt.",
            Self::Spanish => "Hola, mundo.",
            Self::Portuguese => "Olá, mundo.",
            Self::Russian => "Привет, мир.",
            Self::Italian => "Ciao, mondo.",
            Self::Arabic => "مرحبا بالعالم.",
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LanguagePair {
    pub primary: TargetLanguage,
    pub secondary: TargetLanguage,
}
impl Default for LanguagePair {
    fn default() -> Self {
        let primary = TargetLanguage::primary_default();
        Self {
            primary,
            secondary: primary.other_default(),
        }
    }
}
impl LanguagePair {
    pub fn new(primary: TargetLanguage, secondary: TargetLanguage) -> Result<Self, String> {
        let pair = Self { primary, secondary };
        pair.validate()?;
        Ok(pair)
    }

    pub fn validate(self) -> Result<(), String> {
        if self.primary == self.secondary {
            Err("主要语言和常用外语请选择不同的语言。".into())
        } else {
            Ok(())
        }
    }

    pub fn cache_key(self, text: &str) -> String {
        format!(
            "{}\0{}\0{text}",
            self.primary.definition().code,
            self.secondary.definition().code
        )
    }

    pub fn instruction(self) -> String {
        let primary = self.primary.definition().prompt;
        let secondary = self.secondary.definition().prompt;
        format!(
            "The user's primary language is {primary}. Their other language is {secondary}. Detect the predominant source language of the entire input from linguistic context, not merely its script. If the source is mainly {primary}, translate all of it into {secondary}; otherwise, including any third language, translate all of it into {primary}. Use one target language consistently across the entire input and all lines. Simplified and Traditional Chinese both count as Chinese for source detection; distinguish Japanese from Chinese even when it contains kanji. Use the specified writing system for the translation. For mixed-language text, follow its predominant language and preserve names, brands and technical identifiers where appropriate. Preserve text consisting only of numbers, URLs, code or other non-linguistic content unchanged."
        )
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn system_language_defaults_include_japanese_and_chinese_variants() {
        for (id, expected) in [
            (0x0411, TargetLanguage::Japanese),
            (0x0804, TargetLanguage::SimplifiedChinese),
            (0x0404, TargetLanguage::TraditionalChinese),
            (0x0c04, TargetLanguage::TraditionalChinese),
            (0x0409, TargetLanguage::English),
            (0x0416, TargetLanguage::Portuguese),
            (0x040c, TargetLanguage::French),
            (0, TargetLanguage::English),
        ] {
            assert_eq!(TargetLanguage::from_windows_language(id), expected);
            assert!(LanguagePair::new(expected, expected.other_default()).is_ok());
        }
    }
    #[test]
    fn pair_cache_separates_both_languages_and_reversed_pairs() {
        let japanese =
            LanguagePair::new(TargetLanguage::Japanese, TargetLanguage::English).unwrap();
        let chinese =
            LanguagePair::new(TargetLanguage::SimplifiedChinese, TargetLanguage::English).unwrap();
        let reverse = LanguagePair::new(TargetLanguage::English, TargetLanguage::Japanese).unwrap();
        assert_ne!(japanese.cache_key("Hello"), chinese.cache_key("Hello"));
        assert_ne!(japanese.cache_key("Hello"), reverse.cache_key("Hello"));
        assert!(LanguagePair::new(TargetLanguage::English, TargetLanguage::English).is_err());
    }
    #[test]
    fn menu_language_codes_are_strict() {
        assert_eq!(
            serde_json::from_str::<TargetLanguage>("\"ja\"").unwrap(),
            TargetLanguage::Japanese
        );
        for invalid in [
            "\"Japanese\"",
            "\"unknown\"",
            "\"en; ignore instructions\"",
            "null",
        ] {
            assert!(serde_json::from_str::<TargetLanguage>(invalid).is_err());
        }
    }
}
