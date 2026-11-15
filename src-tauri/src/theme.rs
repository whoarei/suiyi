use crate::store::Store;
use sha2::{Digest, Sha256};
use std::{collections::BTreeMap, fs};
use tauri::{Emitter, Manager};
pub const PALETTES: [(&str, &str, &str); 10] = [
    ("Blue", "蓝色", "#2E68ED"),
    ("Cyan", "青色", "#01B2FE"),
    ("Green", "绿色", "#00BB9E"),
    ("Lime", "青柠绿", "#B8D811"),
    ("Yellow", "黄色", "#FECB3C"),
    ("Orange", "橙色", "#FF8067"),
    ("Pink", "粉色", "#FA70AB"),
    ("Magenta", "洋红色", "#DF68DC"),
    ("Purple", "紫色", "#A35BFD"),
    ("Black", "黑色", "#000000"),
];
pub fn valid(id: &str) -> bool {
    PALETTES.iter().any(|p| p.0 == id)
}
pub fn dark() -> bool {
    use winreg::{RegKey, enums::*};
    RegKey::predef(HKEY_CURRENT_USER)
        .open_subkey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize")
        .ok()
        .and_then(|key| key.get_value::<u32, _>("AppsUseLightTheme").ok())
        == Some(0)
}
fn rgb(hex: &str) -> [u8; 3] {
    let n = u32::from_str_radix(hex.trim_start_matches('#'), 16).unwrap_or(0xff8067);
    [(n >> 16) as u8, (n >> 8) as u8, n as u8]
}
fn hex(c: [u8; 3]) -> String {
    format!("#{:02X}{:02X}{:02X}", c[0], c[1], c[2])
}
fn mix(a: [u8; 3], b: [u8; 3], t: f64) -> [u8; 3] {
    std::array::from_fn(|i| (a[i] as f64 * t + b[i] as f64 * (1. - t)).round() as u8)
}
pub fn contrast(a: [u8; 3], b: [u8; 3]) -> f64 {
    fn lum(c: [u8; 3]) -> f64 {
        let v = c.map(|v| {
            let s = v as f64 / 255.;
            if s <= 0.04045 {
                s / 12.92
            } else {
                ((s + 0.055) / 1.055).powf(2.4)
            }
        });
        v[0] * 0.2126 + v[1] * 0.7152 + v[2] * 0.0722
    }
    let (a, b) = (lum(a), lum(b));
    (a.max(b) + 0.05) / (a.min(b) + 0.05)
}
fn on(c: [u8; 3]) -> [u8; 3] {
    if contrast([255; 3], c) > contrast([23; 3], c) {
        [255; 3]
    } else {
        [23; 3]
    }
}
pub fn colors(id: &str, dark: bool) -> BTreeMap<String, String> {
    let accent = if id == "Black" && dark {
        [229, 233, 239]
    } else {
        rgb(PALETTES
            .iter()
            .find(|p| p.0 == id)
            .unwrap_or(&PALETTES[5])
            .2)
    };
    let surface = rgb(if dark { "#222831" } else { "#FFFFFF" });
    let field = rgb(if dark { "#2B323C" } else { "#F7F8FA" });
    let mut readable = if dark {
        mix([255; 3], accent, 0.30)
    } else {
        accent
    };
    for _ in 0..30 {
        if contrast(readable, surface) >= 4.5 {
            break;
        }
        readable = mix(on(surface), readable, 0.1)
    }
    [
        ("surface", hex(surface)),
        ("field", hex(field)),
        ("text", if dark { "#EDF0F5" } else { "#28312E" }.into()),
        ("muted", if dark { "#A5AFBC" } else { "#68746F" }.into()),
        ("border", if dark { "#414A57" } else { "#E1E6E2" }.into()),
        ("control", if dark { "#303844" } else { "#F4F6F5" }.into()),
        ("accent", hex(accent)),
        ("on-accent", hex(on(accent))),
        ("accent-text", hex(readable)),
        (
            "accent-soft",
            hex(mix(accent, surface, if dark { 0.18 } else { 0.12 })),
        ),
        ("selection", hex(mix(accent, field, 0.28))),
        ("error", if dark { "#FF9A90" } else { "#A4392F" }.into()),
    ]
    .into_iter()
    .map(|(k, v)| (k.into(), v))
    .collect()
}
pub fn icon(id: &str, dark: bool, size: u32) -> image::RgbaImage {
    let c = colors(id, dark);
    let bg = rgb(&c["accent"]);
    let fg = rgb(&c["on-accent"]);
    image::RgbaImage::from_fn(size, size, |px, py| {
        let mut sums = [0u32; 4];
        for sy in 0..4 {
            for sx in 0..4 {
                let x = (px as f64 + (sx as f64 + 0.5) / 4.) * 16. / size as f64;
                let y = (py as f64 + (sy as f64 + 0.5) / 4.) * 16. / size as f64;
                let dx = (x - 8.).abs() - 4.;
                let dy = (y - 8.).abs() - 4.;
                let inside = dx.max(0.).powi(2) + dy.max(0.).powi(2) <= 16.;
                if inside {
                    let rect = |l: f64, t: f64, r: f64, b: f64| x >= l && x < r && y >= t && y < b;
                    let glyph = rect(10.5, 3., 13., 4.5)
                        || rect(11., 4.5, 12.5, 11.5)
                        || rect(10.5, 11.5, 13., 13.)
                        || rect(3.5, 4., 8., 5.5)
                        || rect(3.5, 10.5, 7.5, 12.)
                        || rect(3.5, 7.2, 7., 8.8)
                        || ((7. ..=9.5).contains(&x) && (y - 8.).abs() <= 2.1 * (9.5 - x) / 2.5);
                    let color = if glyph { fg } else { bg };
                    for i in 0..3 {
                        sums[i] += color[i] as u32
                    }
                    sums[3] += 255
                }
            }
        }
        image::Rgba(sums.map(|s| (s / 16) as u8))
    })
}
pub fn refresh(app: &tauri::AppHandle, store: &Store, id: &str) -> Result<(), String> {
    let dark = dark();
    let bytes = icon(id, dark, 64).into_raw();
    let image = tauri::image::Image::new_owned(bytes, 64, 64);
    if let Some(tray) = app.tray_by_id("suiyi") {
        let _ = tray.set_icon(Some(image.clone()));
    }
    let folder = store.root.join("Icons");
    let _ = fs::create_dir_all(&folder);
    let ico = (|| -> Result<std::path::PathBuf, Box<dyn std::error::Error>> {
        use image::codecs::ico::{IcoEncoder, IcoFrame};
        let mut frames = vec![];
        for size in [16, 20, 24, 32, 40, 48, 64, 96, 128, 256] {
            let rgba = icon(id, dark, size);
            frames.push(IcoFrame::as_png(
                rgba.as_raw(),
                size,
                size,
                image::ExtendedColorType::Rgba8,
            )?)
        }
        let mut data = vec![];
        IcoEncoder::new(&mut data).encode_images(&frames)?;
        let hash = format!("{:x}", Sha256::digest(&data));
        let path = folder.join(format!(
            "tauri-{id}-{}-{}.ico",
            if dark { "dark" } else { "light" },
            &hash[..12]
        ));
        if fs::read(&path).ok().as_deref() != Some(data.as_slice()) {
            fs::write(&path, data)?;
        }
        Ok(path)
    })();
    for (_, window) in app.webview_windows() {
        let _ = window.set_icon(image.clone());
        let _ = window.emit("theme", colors(id, dark));
        if let Ok(path) = &ico
            && let Ok(hwnd) = window.hwnd()
        {
            crate::native::shell_icon(hwnd.0 as isize, path)
        }
    }
    if let Ok(path) = &ico
        && !store.isolated
    {
        crate::native::refresh_shortcuts(path)
    }
    Ok(())
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn palette_contrast() {
        for (id, _, _) in PALETTES {
            for dark in [false, true] {
                let c = colors(id, dark);
                assert!(contrast(rgb(&c["accent"]), rgb(&c["on-accent"])) >= 4.5);
                assert!(contrast(rgb(&c["surface"]), rgb(&c["accent-text"])) >= 4.5);
                assert_eq!(icon(id, dark, 32).len(), 4096)
            }
        }
    }
    #[test]
    fn black_in_dark_is_visible() {
        assert_eq!(colors("Black", true)["accent"], "#E5E9EF")
    }
}
