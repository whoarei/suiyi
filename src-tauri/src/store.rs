use crate::ffi;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{fs, path::PathBuf};
use winreg::{RegKey, enums::*};

#[derive(Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Config {
    pub version: u32,
    pub model: String,
    #[serde(default)]
    pub base_url: String,
}
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Hotkey {
    pub modifiers: u32,
    pub virtual_key: u32,
}
impl Default for Hotkey {
    fn default() -> Self {
        Self {
            modifiers: 3,
            virtual_key: 0x54,
        }
    }
}
impl Hotkey {
    pub fn valid(self) -> bool {
        self.modifiers & !7 == 0
            && self.modifiers & 3 != 0
            && matches!(self.virtual_key,0x30..=0x39|0x41..=0x5a|0x70..=0x7b)
    }
    pub fn display(self) -> String {
        format!(
            "{}{}{}{}",
            if self.modifiers & 2 != 0 { "Ctrl+" } else { "" },
            if self.modifiers & 1 != 0 { "Alt+" } else { "" },
            if self.modifiers & 4 != 0 {
                "Shift+"
            } else {
                ""
            },
            if self.virtual_key >= 0x70 {
                format!("F{}", self.virtual_key - 0x6f)
            } else {
                char::from_u32(self.virtual_key).unwrap_or('T').to_string()
            }
        )
    }
}
#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Preferences {
    pub hotkey: Hotkey,
    pub quick_mode: bool,
    pub theme: String,
    pub auto_start: bool,
}
impl Default for Preferences {
    fn default() -> Self {
        Self {
            hotkey: Hotkey::default(),
            quick_mode: false,
            theme: "Orange".into(),
            auto_start: true,
        }
    }
}
#[derive(Clone)]
pub struct Store {
    pub root: PathBuf,
    pub isolated: bool,
}
impl Store {
    pub fn new(profile: Option<PathBuf>) -> Self {
        match profile {
            Some(root) => Self {
                root,
                isolated: true,
            },
            None => Self {
                root: PathBuf::from(std::env::var_os("LOCALAPPDATA").unwrap_or_default())
                    .join("TranslationFloat"),
                isolated: false,
            },
        }
    }
    pub fn config(&self) -> Config {
        let mut config: Config = fs::read(self.root.join("settings.json"))
            .ok()
            .and_then(|v| serde_json::from_slice(&v).ok())
            .unwrap_or_default();
        if config.version == 1 {
            config.base_url = "https://api.deepseek.com/".into();
        }
        if !(config.version == 1 || config.version == 2) {
            return Config::default();
        }
        match normalize(&config.base_url) {
            Ok(url) => {
                config.base_url = url;
                config
            }
            Err(_) => Config::default(),
        }
    }
    pub fn credential(&self, address: &str, replacement: &str) -> Result<String, String> {
        let address = normalize(address)?;
        if !replacement.trim().is_empty() {
            validate_key(replacement.trim())?;
            return Ok(replacement.trim().into());
        }
        let mut path = self.key_path(&address);
        let legacy = !path.exists() && address == "https://api.deepseek.com/";
        if legacy {
            path = self.root.join("deepseek.key")
        }
        if let Ok(data) = fs::read(path)
            && let Ok(bytes) = protect(&data, false)
            && let Ok(key) = String::from_utf8(bytes)
            && validate_key(&key).is_ok()
        {
            return Ok(key);
        }
        if legacy
            && !self.isolated
            && let Ok(key) = std::env::var("DEEPSEEK_API_KEY")
            && validate_key(key.trim()).is_ok()
        {
            return Ok(key.trim().into());
        }
        Ok(String::new())
    }
    pub fn save_config(&self, address: &str, model: &str, key: &str) -> Result<Config, String> {
        if address.trim().is_empty() && model.trim().is_empty() && key.trim().is_empty() {
            let config = Config {
                version: 2,
                ..Config::default()
            };
            self.atomic("settings.json", &serde_json::to_vec(&config).unwrap())?;
            return Ok(config);
        }
        let base_url = normalize(address)?;
        let model = model.trim().to_string();
        if !model.is_empty() {
            validate_model(&model)?
        }
        let key = self.credential(&base_url, key)?;
        if !key.is_empty() {
            let encrypted = protect(key.as_bytes(), true)?;
            self.atomic(
                self.key_path(&base_url)
                    .file_name()
                    .unwrap()
                    .to_str()
                    .unwrap(),
                &encrypted,
            )?
        }
        let config = Config {
            version: 2,
            model,
            base_url,
        };
        self.atomic("settings.json", &serde_json::to_vec(&config).unwrap())?;
        Ok(config)
    }
    pub fn key_path(&self, address: &str) -> PathBuf {
        self.root
            .join(format!("api-{:x}.key", Sha256::digest(address.as_bytes())))
    }
    pub fn atomic(&self, name: &str, bytes: &[u8]) -> Result<(), String> {
        fs::create_dir_all(&self.root).map_err(|_| "无法创建用户配置目录。")?;
        let path = self.root.join(name);
        let temp = path.with_extension(format!("{}.tmp", uuid::Uuid::new_v4()));
        let result = (|| {
            fs::write(&temp, bytes)?; // Windows rename cannot replace an existing file: use MoveFileExW.
            unsafe extern "system" {
                fn MoveFileExW(from: *const u16, to: *const u16, flags: u32) -> i32;
            }
            if unsafe {
                MoveFileExW(
                    ffi::wide(&temp.to_string_lossy()).as_ptr(),
                    ffi::wide(&path.to_string_lossy()).as_ptr(),
                    1 | 8,
                )
            } == 0
            {
                return Err(std::io::Error::last_os_error());
            }
            Ok(())
        })();
        let _ = fs::remove_file(temp);
        result.map_err(|_: std::io::Error| "无法保存配置，请检查用户目录权限。".into())
    }
    pub fn preferences(&self) -> Preferences {
        if self.isolated {
            let mut preferences: Preferences = fs::read(self.root.join("preferences.json"))
                .ok()
                .and_then(|v| serde_json::from_slice(&v).ok())
                .unwrap_or_default();
            if !preferences.hotkey.valid() {
                preferences.hotkey = Hotkey::default();
            }
            if !crate::theme::valid(&preferences.theme) {
                preferences.theme = "Orange".into();
            }
            return preferences;
        }
        let mut p = Preferences::default();
        if let Ok(key) = RegKey::predef(HKEY_CURRENT_USER).open_subkey("Software\\Suiyi") {
            if let Ok(value) = key.get_value::<String, _>("HotKey") {
                let parts: Vec<_> = value.split(':').collect();
                if parts.len() == 2
                    && let (Ok(modifiers), Ok(virtual_key)) = (parts[0].parse(), parts[1].parse())
                {
                    let h = Hotkey {
                        modifiers,
                        virtual_key,
                    };
                    if h.valid() {
                        p.hotkey = h
                    }
                }
            }
            p.quick_mode = key.get_value::<u32, _>("QuickModeEnabled").unwrap_or(0) == 1;
            p.theme = key
                .get_value("ThemeColor")
                .unwrap_or_else(|_| "Orange".into());
            p.auto_start = key.get_value::<u32, _>("AutoStartEnabled").unwrap_or(1) == 1;
        }
        if !crate::theme::valid(&p.theme) {
            p.theme = "Orange".into()
        }
        p
    }
    pub fn save_preferences(&self, p: &Preferences) -> Result<(), String> {
        if !p.hotkey.valid() || !crate::theme::valid(&p.theme) {
            return Err("快捷键或主题无效。".into());
        }
        if self.isolated {
            return self.atomic("preferences.json", &serde_json::to_vec(p).unwrap());
        }
        let (key, _) = RegKey::predef(HKEY_CURRENT_USER)
            .create_subkey("Software\\Suiyi")
            .map_err(|_| "无法保存界面偏好。")?;
        (|| -> std::io::Result<()> {
            key.set_value(
                "HotKey",
                &format!("{}:{}", p.hotkey.modifiers, p.hotkey.virtual_key),
            )?;
            key.set_value("QuickModeEnabled", &(p.quick_mode as u32))?;
            key.set_value("ThemeColor", &p.theme)?;
            key.set_value("AutoStartEnabled", &(p.auto_start as u32))?;
            Ok(())
        })()
        .map_err(|_| "无法保存界面偏好。".into())
    }
    pub fn startup(&self, enabled: bool) -> Result<(), String> {
        if self.isolated {
            return Ok(());
        }
        let exe = std::env::current_exe().map_err(|_| "无法确认程序路径。")?;
        let command = format!("\"{}\"", exe.display());
        if command.encode_utf16().count() > 260 {
            return Err("开机启动路径过长，请移到较短的固定目录。".into());
        }
        let (key, _) = RegKey::predef(HKEY_CURRENT_USER)
            .create_subkey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")
            .map_err(|_| "无法修改开机启动。")?;
        if enabled {
            if key.get_value::<String, _>("Suiyi").ok().as_deref() != Some(&command) {
                key.set_value("Suiyi", &command)
                    .map_err(|_| "无法修改开机启动。")?
            }
        } else {
            match key.delete_value("Suiyi") {
                Ok(()) => (),
                Err(e) if e.kind() == std::io::ErrorKind::NotFound => (),
                Err(_) => return Err("无法修改开机启动。".into()),
            }
        }
        Ok(())
    }
}
pub fn normalize(address: &str) -> Result<String, String> {
    if address.len() > 2048 {
        return Err("API 地址过长。".into());
    }
    let mut url = reqwest::Url::parse(address.trim()).map_err(|_| "请填写有效的 API 根地址。")?;
    if !matches!(url.scheme(), "http" | "https")
        || url.host_str().is_none()
        || !url.username().is_empty()
        || url.password().is_some()
        || url.query().is_some()
        || url.fragment().is_some()
    {
        return Err("请填写不含认证信息或查询参数的 API 根地址。".into());
    }
    let mut path = url.path().trim_end_matches('/').to_string();
    if path.to_ascii_lowercase().ends_with("/chat/completions") {
        path.truncate(path.len() - 17)
    }
    url.set_path(&(path + "/"));
    Ok(url.to_string())
}
pub fn validate_key(key: &str) -> Result<(), String> {
    if key.len() > 4096 || key.bytes().any(|c| !(33..=126).contains(&c)) {
        Err("API Key 格式无效。".into())
    } else {
        Ok(())
    }
}
pub fn validate_model(model: &str) -> Result<(), String> {
    if model.trim().is_empty() || model.chars().count() > 256 || model.chars().any(char::is_control)
    {
        Err("请填写模型名称，或点击获取。".into())
    } else {
        Ok(())
    }
}
fn protect(data: &[u8], encrypt: bool) -> Result<Vec<u8>, String> {
    unsafe {
        let input = ffi::Blob {
            len: data.len() as u32,
            data: data.as_ptr() as *mut u8,
        };
        let mut output = ffi::Blob::default();
        let ok = if encrypt {
            ffi::CryptProtectData(
                &input,
                std::ptr::null(),
                std::ptr::null(),
                std::ptr::null(),
                std::ptr::null(),
                1,
                &mut output,
            )
        } else {
            ffi::CryptUnprotectData(
                &input,
                std::ptr::null_mut(),
                std::ptr::null(),
                std::ptr::null(),
                std::ptr::null(),
                1,
                &mut output,
            )
        };
        if ok == 0 {
            return Err("无法加密或读取密钥，请重新填写。".into());
        }
        let bytes = std::slice::from_raw_parts(output.data, output.len as usize).to_vec();
        std::ptr::write_bytes(output.data, 0, output.len as usize);
        ffi::LocalFree(output.data as isize);
        Ok(bytes)
    }
}
pub fn utf16_len(text: &str) -> usize {
    text.encode_utf16().count()
}
#[cfg(test)]
pub fn isolated_test_store() -> Store {
    Store::new(Some(
        std::env::temp_dir().join(format!("Suiyi.Rust.Tests.{}", uuid::Uuid::new_v4())),
    ))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn endpoints() {
        assert_eq!(
            normalize("https://example.com/v1/chat/completions").unwrap(),
            "https://example.com/v1/"
        );
        for bad in [
            "file:///tmp/x",
            "https://u:p@example.com",
            "https://example.com/?key=x",
            "https://example.com/#x",
        ] {
            assert!(normalize(bad).is_err())
        }
    }
    #[test]
    fn hotkeys() {
        assert_eq!(Hotkey::default().display(), "Ctrl+Alt+T");
        assert!(
            !Hotkey {
                modifiers: 4,
                virtual_key: 65
            }
            .valid()
        );
        assert!(
            !Hotkey {
                modifiers: 8,
                virtual_key: 65
            }
            .valid()
        );
        assert!(
            Hotkey {
                modifiers: 2,
                virtual_key: 0x7b
            }
            .valid()
        )
    }
    #[test]
    fn key_isolation_dpapi() {
        let s = isolated_test_store();
        s.save_config("https://one.example/v1", "model:1", "fake-test-key")
            .unwrap();
        assert_eq!(
            s.credential("https://one.example/v1", "").unwrap(),
            "fake-test-key"
        );
        assert_eq!(s.credential("https://two.example/v1", "").unwrap(), "");
        assert!(
            !String::from_utf8_lossy(&fs::read(s.key_path("https://one.example/v1/")).unwrap())
                .contains("fake-test-key")
        );
        assert!(
            !fs::read_to_string(s.root.join("settings.json"))
                .unwrap()
                .contains("fake-test-key")
        );
        fs::remove_dir_all(&s.root).unwrap()
    }
    #[test]
    fn legacy_migration() {
        let s = isolated_test_store();
        s.atomic("settings.json", br#"{"Version":1,"Model":"legacy-model"}"#)
            .unwrap();
        assert_eq!(s.config().base_url, "https://api.deepseek.com/");
        s.save_config(&s.config().base_url, "legacy-model", "")
            .unwrap();
        assert_eq!(s.config().version, 2);
        fs::remove_dir_all(s.root).unwrap()
    }
    #[test]
    fn unicode_limits() {
        assert_eq!(utf16_len("你好😀"), 4)
    }
    #[test]
    fn preferences_are_isolated_and_round_trip() {
        let s = isolated_test_store();
        assert!(!s.preferences().quick_mode);
        assert!(s.preferences().auto_start);
        let p = Preferences {
            quick_mode: true,
            auto_start: false,
            theme: "Blue".into(),
            hotkey: Hotkey {
                modifiers: 3,
                virtual_key: 89,
            },
        };
        s.save_preferences(&p).unwrap();
        let restored = s.preferences();
        assert!(restored.quick_mode);
        assert!(!restored.auto_start);
        assert_eq!(restored.theme, "Blue");
        assert_eq!(restored.hotkey, p.hotkey);
        fs::remove_dir_all(s.root).unwrap();
    }
    #[test]
    fn corrupt_configuration_does_not_guess_an_address() {
        let s = isolated_test_store();
        s.atomic(
            "settings.json",
            br#"{"Version":2,"BaseUrl":"","Model":"model"}"#,
        )
        .unwrap();
        assert!(s.config().base_url.is_empty());
        assert_eq!(s.credential("https://another.example/", "").unwrap(), "");
        fs::remove_dir_all(s.root).unwrap();
    }
}
