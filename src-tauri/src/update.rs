use crate::api::bounded;
use reqwest::Client;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use sha2::{Digest, Sha256};
use std::{path::PathBuf, process::Command, time::Duration};
use tokio::io::AsyncWriteExt;
use tokio_util::sync::CancellationToken;
const API: &str = "https://api.github.com/repos/whoarei/suiyi/releases/latest";
#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Installer {
    pub version: String,
    pub tag: String,
    pub size: u64,
    pub sha256: String,
}
#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Check {
    pub current: String,
    pub latest: String,
    pub available: bool,
    pub ahead: bool,
    pub page: String,
    pub installer: Option<Installer>,
}
#[derive(Clone)]
pub struct Download {
    pub path: PathBuf,
    pub sha256: String,
}
pub fn version(value: &str) -> Option<semver::Version> {
    semver::Version::parse(value.trim_start_matches(['v', 'V'])).ok()
}
pub fn parse_release(v: &Value, current: &str) -> Result<Check, String> {
    let invalid = || "更新信息暂时不可用，请稍后重试。".to_string();
    if v["draft"] != false || v["prerelease"] != false {
        return Err(invalid());
    }
    let tag = v["tag_name"].as_str().ok_or_else(invalid)?;
    let latest = version(tag)
        .filter(|v| v.pre.is_empty())
        .ok_or_else(invalid)?;
    let current_v = version(current).ok_or_else(invalid)?;
    let installer = v["assets"].as_array().and_then(|assets| {
        assets.iter().find_map(|a| {
            let size = a["size"].as_u64()?;
            let hash = a["digest"].as_str()?.strip_prefix("sha256:")?;
            if a["name"] != format!("suiyi-{latest}-windows-x64-setup.exe")
                || a["state"] != "uploaded"
                || size == 0
                || size > 256 * 1024 * 1024
                || hash.len() != 64
                || !hash.bytes().all(|c| c.is_ascii_hexdigit())
            {
                return None;
            }
            Some(Installer {
                version: latest.to_string(),
                tag: tag.into(),
                size,
                sha256: hash.into(),
            })
        })
    });
    Ok(Check {
        current: current.into(),
        latest: latest.to_string(),
        available: latest.cmp_precedence(&current_v).is_gt(),
        ahead: latest.cmp_precedence(&current_v).is_lt(),
        page: format!("https://github.com/whoarei/suiyi/releases/tag/{tag}"),
        installer,
    })
}
pub async fn check(client: &Client, cancel: &CancellationToken) -> Result<Check, String> {
    tokio::select! {_=cancel.cancelled()=>Err("检查已取消。".into()),result=async{let response=client.get(API).header("User-Agent",concat!("Suiyi/",env!("SUIYI_VERSION"))).header("Accept","application/vnd.github+json").send().await.map_err(|_|"无法连接 GitHub，请稍后重试。")?;if !response.status().is_success(){return Err(if matches!(response.status().as_u16(),403|429){"GitHub 暂时限制了检查请求，请稍后重试。"}else{"无法检查更新，请稍后重试。"}.into())}let bytes=bounded(response,1024*1024).await.map_err(|e|e.message)?;let json=serde_json::from_slice(&bytes).map_err(|_|"更新信息格式无效。")?;parse_release(&json,env!("SUIYI_VERSION"))}=>result}
}
pub fn allowed_redirect(url: &reqwest::Url, initial: &reqwest::Url) -> bool {
    url.scheme() == "https"
        && url.port_or_known_default() == Some(443)
        && url.username().is_empty()
        && url.password().is_none()
        && url.fragment().is_none()
        && (url == initial
            || matches!(
                url.host_str(),
                Some("release-assets.githubusercontent.com" | "objects.githubusercontent.com")
            ))
}
pub async fn download(
    installer: &Installer,
    cancel: &CancellationToken,
    progress: impl Fn(u64, u64),
) -> Result<Download, String> {
    let tag = version(&installer.tag)
        .filter(|v| v.pre.is_empty() && v.to_string() == installer.version)
        .ok_or("安装包信息无效。")?;
    let _ = tag;
    let initial = reqwest::Url::parse(&format!(
        "https://github.com/whoarei/suiyi/releases/download/{}/suiyi-{}-windows-x64-setup.exe",
        installer.tag, installer.version
    ))
    .map_err(|_| "安装包地址无效。")?;
    let client = Client::builder()
        .redirect(reqwest::redirect::Policy::none())
        .timeout(Duration::from_secs(600))
        .build()
        .map_err(|_| "无法创建下载连接。")?;
    let folder = std::env::temp_dir()
        .join("Suiyi.Updates")
        .join(uuid::Uuid::new_v4().simple().to_string());
    std::fs::create_dir_all(&folder).map_err(|_| "无法创建下载目录。")?;
    let part = folder.join("download.part");
    let path = folder.join(format!("suiyi-{}-windows-x64-setup.exe", installer.version));
    let result = tokio::select! {_=cancel.cancelled()=>Err("下载已取消。".into()),result=tokio::time::timeout(Duration::from_secs(600),async{
        let mut url=initial.clone();let mut response=None;for _ in 0..6{let r=client.get(url.clone()).header("User-Agent","Suiyi-Updater/1.0").send().await.map_err(|_|"下载安装包失败，请检查网络。")?;if r.status()==reqwest::StatusCode::OK{response=Some(r);break} else if !r.status().is_redirection(){return Err("下载安装包失败，请稍后重试。".to_string())}let next=r.headers().get("location").and_then(|v|v.to_str().ok()).and_then(|v|url.join(v).ok()).ok_or("下载地址无效。")?;if !allowed_redirect(&next,&initial){return Err("下载重定向地址无效。".into())}url=next}
        let mut response=response.ok_or("下载重定向过多。")?;if response.content_length().is_some_and(|v|v!=installer.size){return Err("安装包大小不符。".into())}let mut file=tokio::fs::File::create(&part).await.map_err(|_|"无法保存安装包。")?;let mut hash=Sha256::new();let mut total=0u64;let mut last=std::time::Instant::now();while let Some(chunk)=response.chunk().await.map_err(|_|"安装包下载中断。")?{total+=chunk.len()as u64;if total>installer.size{return Err("安装包大小不符。".into())}hash.update(&chunk);file.write_all(&chunk).await.map_err(|_|"无法保存安装包。")?;if last.elapsed()>Duration::from_millis(150){progress(total,installer.size);last=std::time::Instant::now()}}
        file.flush().await.map_err(|_|"无法保存安装包。")?;drop(file);if total!=installer.size||!format!("{:x}",hash.finalize()).eq_ignore_ascii_case(&installer.sha256){return Err("安装包校验失败，请重试下载。".into())}tokio::fs::rename(&part,&path).await.map_err(|_|"无法保存安装包。")?;progress(total,installer.size);Ok(Download{path:path.clone(),sha256:installer.sha256.clone()})
    })=>result.unwrap_or_else(|_|Err("下载超时，请重试。".to_string()))};
    if result.is_err() {
        let _ = std::fs::remove_file(&part);
        let _ = std::fs::remove_dir(&folder);
    }
    result
}
pub fn discard(file: &Download) {
    if file.path.parent().is_some_and(|parent| {
        parent.parent() == Some(std::env::temp_dir().join("Suiyi.Updates").as_path())
            && uuid::Uuid::parse_str(&parent.file_name().unwrap_or_default().to_string_lossy())
                .is_ok()
    }) {
        let _ = std::fs::remove_file(&file.path);
        let _ = std::fs::remove_dir(file.path.parent().unwrap());
    }
}
pub fn launch(file: &Download) -> Result<(), String> {
    use std::io::Read;
    let mut source = std::fs::File::open(&file.path).map_err(|_| "安装包已不可用，请重新下载。")?;
    let mut hash = Sha256::new();
    let mut buffer = [0u8; 65536];
    loop {
        let count = source.read(&mut buffer).map_err(|_| "无法校验安装包。")?;
        if count == 0 {
            break;
        }
        hash.update(&buffer[..count])
    }
    if !format!("{:x}", hash.finalize()).eq_ignore_ascii_case(&file.sha256) {
        return Err("安装包校验失败，请重新下载。".into());
    }
    Command::new(&file.path)
        .args(["/SP-", "/SILENT", "/NORESTART", "/SUIYIUPDATE=1"])
        .spawn()
        .map_err(|_| "无法启动安装器，请重试。")?;
    Ok(())
}
pub fn open_release(page: &str) -> Result<(), String> {
    if page != "https://github.com/whoarei/suiyi/releases/latest"
        && !page.starts_with("https://github.com/whoarei/suiyi/releases/tag/")
    {
        return Err("发布地址无效。".into());
    }
    unsafe {
        use windows::{
            Win32::{UI::Shell::ShellExecuteW, UI::WindowsAndMessaging::SW_SHOWNORMAL},
            core::PCWSTR,
        };
        let result = ShellExecuteW(
            None,
            PCWSTR(crate::ffi::wide("open").as_ptr()),
            PCWSTR(crate::ffi::wide(page).as_ptr()),
            None,
            None,
            SW_SHOWNORMAL,
        );
        if result.0 as isize <= 32 {
            return Err("无法打开发布页面。".into());
        }
    }
    Ok(())
}
#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    #[test]
    fn versions() {
        assert!(version("v0.3.0").unwrap() > version("0.2.10").unwrap());
        assert!(version("x;cmd").is_none())
    }
    #[test]
    fn untrusted_metadata() {
        assert!(
            parse_release(
                &json!({"draft":true,"prerelease":false,"tag_name":"v0.4.0"}),
                "0.3.0"
            )
            .is_err()
        );
        let c=parse_release(&json!({"draft":false,"prerelease":false,"tag_name":"v0.4.0","html_url":"https://evil.example","assets":[]}),"0.3.0").unwrap();
        assert!(c.available);
        assert!(c.installer.is_none());
        assert_eq!(
            c.page,
            "https://github.com/whoarei/suiyi/releases/tag/v0.4.0"
        )
    }
    #[test]
    fn redirect_scope() {
        let initial = reqwest::Url::parse(
            "https://github.com/whoarei/suiyi/releases/download/v0.4.0/setup.exe",
        )
        .unwrap();
        assert!(allowed_redirect(
            &reqwest::Url::parse("https://release-assets.githubusercontent.com/a").unwrap(),
            &initial
        ));
        assert!(!allowed_redirect(
            &reqwest::Url::parse("https://evil.example/a").unwrap(),
            &initial
        ));
        assert!(!allowed_redirect(
            &reqwest::Url::parse("http://objects.githubusercontent.com/a").unwrap(),
            &initial
        ))
    }
}
