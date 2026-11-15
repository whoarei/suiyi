use std::{fs, io::Write, path::PathBuf, sync::OnceLock};
static ROOT: OnceLock<PathBuf> = OnceLock::new();
pub fn init(path: PathBuf) {
    let _ = ROOT.set(path);
}
pub fn write(stage: &str, outcome: &str, process: Option<&str>) {
    let folder = ROOT.get().cloned().unwrap_or_else(|| {
        PathBuf::from(std::env::var_os("LOCALAPPDATA").unwrap_or_default()).join("TranslationFloat")
    });
    let _ = (|| -> std::io::Result<()> {
        fs::create_dir_all(&folder)?;
        let path = folder.join("m0.log");
        if path.metadata().is_ok_and(|m| m.len() > 256 * 1024) {
            let _ = fs::remove_file(path.with_extension("log.previous"));
            fs::rename(&path, path.with_extension("log.previous"))?;
        }
        let mut file = fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(path)?;
        writeln!(
            file,
            "{}",
            serde_json::json!({"timeMs":std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_millis(),"stage":stage,"outcome":outcome,"process":process})
        )?;
        Ok(())
    })();
}
