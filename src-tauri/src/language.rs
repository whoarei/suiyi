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
}
