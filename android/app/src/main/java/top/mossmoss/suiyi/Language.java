package top.mossmoss.suiyi;

import java.util.Locale;

enum Language {
    EN("en", "English", "Hello, world.", R.string.language_en),
    ZH_HANS("zh-Hans", "Simplified Chinese", "你好，世界。", R.string.language_zh_hans),
    ZH_HANT("zh-Hant", "Traditional Chinese", "你好，世界。", R.string.language_zh_hant),
    JA("ja", "Japanese", "こんにちは、世界。", R.string.language_ja),
    KO("ko", "Korean", "안녕하세요, 세계.", R.string.language_ko),
    FR("fr", "French", "Bonjour le monde.", R.string.language_fr),
    DE("de", "German", "Hallo, Welt.", R.string.language_de),
    ES("es", "Spanish", "Hola, mundo.", R.string.language_es),
    PT("pt", "Portuguese", "Olá, mundo.", R.string.language_pt),
    RU("ru", "Russian", "Привет, мир.", R.string.language_ru),
    IT("it", "Italian", "Ciao, mondo.", R.string.language_it),
    AR("ar", "Arabic", "مرحبا بالعالم.", R.string.language_ar);

    final String code;
    final String promptName;
    final String sample;
    final int label;

    Language(String code, String promptName, String sample, int label) {
        this.code = code;
        this.promptName = promptName;
        this.sample = sample;
        this.label = label;
    }

    static Language fromCode(String code, Language fallback) {
        for (Language language : values()) {
            if (language.code.equals(code)) {
                return language;
            }
        }
        return fallback;
    }

    static Language forLocale(Locale locale) {
        if ("zh".equals(locale.getLanguage())) {
            String country = locale.getCountry();
            return "Hant".equals(locale.getScript()) || "TW".equals(country)
                    || "HK".equals(country) || "MO".equals(country) ? ZH_HANT : ZH_HANS;
        }
        return fromCode(locale.getLanguage(), EN);
    }

    Language defaultOther() {
        return this == EN ? ZH_HANS : EN;
    }

    static String instruction(Language primary, Language other) {
        return "The user's primary language is " + primary.promptName + ". Their other language is "
                + other.promptName + ". Detect the predominant source language of the entire input "
                + "from linguistic context, not merely its script. If the input is mainly "
                + primary.promptName + ", translate all of it into " + other.promptName
                + "; otherwise, including any third language, translate all of it into "
                + primary.promptName + ". Use one target language throughout. "
                + "Simplified and Traditional Chinese both count as Chinese for source detection; "
                + "distinguish Japanese from Chinese even when it contains kanji. Use the specified "
                + "writing system. Preserve text consisting only of numbers, URLs, code or other "
                + "non-linguistic content unchanged.";
    }
}
