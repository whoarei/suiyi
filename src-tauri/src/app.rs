use crate::{
    api::{self, Provider},
    native::{self, HotkeyWorker, Receipt, Target},
    store::{Config, Hotkey, Preferences, Store},
    theme, update,
};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use std::{
    collections::{HashMap, VecDeque},
    path::PathBuf,
    sync::{
        Arc, Mutex,
        atomic::{AtomicBool, AtomicU64, Ordering},
    },
    time::{Duration, Instant},
};
use tauri::{AppHandle, Emitter, Manager, State, WebviewUrl, WebviewWindow, WindowEvent};
use tokio_util::sync::CancellationToken;

#[derive(Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
struct OpenContext {
    selected: Option<String>,
    target_name: String,
    has_target: bool,
    message: String,
}
struct Cache {
    values: HashMap<String, (String, Instant)>,
    order: VecDeque<String>,
}
pub struct Runtime {
    store: Store,
    config: Mutex<Config>,
    prefs: Mutex<Preferences>,
    hotkey: Mutex<Option<HotkeyWorker>>,
    client: reqwest::Client,
    target: Arc<Mutex<Option<Target>>>,
    context: Mutex<OpenContext>,
    busy: AtomicBool,
    settings_open: AtomicBool,
    normal: Mutex<CancellationToken>,
    quick: Mutex<Option<CancellationToken>>,
    settings_cancel: Mutex<CancellationToken>,
    update_cancel: Mutex<CancellationToken>,
    generation: AtomicU64,
    normal_serial: AtomicU64,
    ready: AtomicBool,
    cache: Mutex<Cache>,
    schedule: tokio::sync::Mutex<Instant>,
    cooldown: Mutex<Instant>,
    installer: Mutex<Option<update::Installer>>,
    downloaded: Mutex<Option<update::Download>>,
    updating: AtomicBool,
    notice_generation: AtomicU64,
}
fn role(window: &WebviewWindow, allowed: &[&str]) -> Result<(), String> {
    if allowed.contains(&window.label()) {
        Ok(())
    } else {
        Err("此窗口不能执行该操作。".into())
    }
}
fn cancel_normal(state: &Runtime, invalidate: bool) {
    state.normal.lock().unwrap().cancel();
    state.generation.fetch_add(1, Ordering::SeqCst);
    if invalidate {
        state.ready.store(false, Ordering::SeqCst)
    }
}
fn provider(state: &Runtime, address: &str, model: &str, key: &str) -> Result<Provider, String> {
    Provider::new(
        state.client.clone(),
        address,
        model,
        &state.store.credential(address, key)?,
    )
}
fn active_provider(state: &Runtime) -> Result<Provider, String> {
    let config = state.config.lock().unwrap().clone();
    provider(state, &config.base_url, &config.model, "")
        .map_err(|_| "请先从托盘设置翻译接口和模型。".into())
}
fn refresh_theme(app: &AppHandle) {
    let a = app.clone();
    let _ = app.run_on_main_thread(move || {
        let s = a.state::<Runtime>();
        let id = s.prefs.lock().unwrap().theme.clone();
        let _ = theme::refresh(&a, &s.store, &id);
    });
}
fn refresh_tray_text(app: &AppHandle) {
    let preferences = app.state::<Runtime>().prefs.lock().unwrap().clone();
    if let Some(tray) = app.tray_by_id("suiyi") {
        let _ = tray.set_tooltip(Some(format!(
            "随译 · {}{}",
            if preferences.quick_mode {
                "快速模式 · "
            } else {
                ""
            },
            preferences.hotkey.display()
        )));
    }
}
fn show(app: &AppHandle, label: &str, near_cursor: bool) {
    if let Some(window) = app.get_webview_window(label) {
        if near_cursor {
            let (w, h) = match label {
                "menu" => (244, 322),
                _ => (440, 330),
            };
            let (x, y) = native::placement(w, h);
            let _ = window.set_position(tauri::PhysicalPosition::new(x, y));
        }
        let _ = window.show();
        let _ = window.set_focus();
    }
}
fn notice(app: &AppHandle, message: &str) {
    let generation = app
        .state::<Runtime>()
        .notice_generation
        .fetch_add(1, Ordering::SeqCst)
        + 1;
    if let Some(w) = app.get_webview_window("notice") {
        let _ = w.emit("notice", message);
        let _ = w.show();
        crate::logging::write("notice", "shown-no-activation", None);
        let a = app.clone();
        let message = message.to_string();
        tauri::async_runtime::spawn(async move {
            tokio::time::sleep(Duration::from_millis(100)).await;
            if let Some(w) = a.get_webview_window("notice") {
                let _ = w.emit("notice", message);
            }
            tokio::time::sleep(Duration::from_secs(3)).await;
            if a.state::<Runtime>()
                .notice_generation
                .load(Ordering::SeqCst)
                == generation
                && let Some(w) = a.get_webview_window("notice")
            {
                let _ = w.hide();
                crate::logging::write("notice", "hidden-after-3s", None);
            }
        });
    }
}
fn open_copy(app: &AppHandle) {
    let state = app.state::<Runtime>();
    if state.busy.load(Ordering::SeqCst) {
        return;
    }
    *state.target.lock().unwrap() = None;
    let context = OpenContext::default();
    *state.context.lock().unwrap() = context.clone();
    if let Some(w) = app.get_webview_window("main") {
        let _ = w.emit("open", context);
    }
    show(app, "main", true)
}
fn open_settings(app: &AppHandle) {
    let state = app.state::<Runtime>();
    if state.busy.load(Ordering::SeqCst) {
        return;
    }
    state.settings_open.store(true, Ordering::SeqCst);
    cancel_normal(&state, false);
    *state.target.lock().unwrap() = None;
    *state.settings_cancel.lock().unwrap() = CancellationToken::new();
    if let Some(w) = app.get_webview_window("main") {
        let _ = w.hide();
    }
    if let Some(w) = app.get_webview_window("settings") {
        let _ = w.emit("settings-open", ());
    }
    show(app, "settings", false)
}
fn open_update(app: &AppHandle) {
    let state = app.state::<Runtime>();
    if state.updating.load(Ordering::SeqCst) {
        show(app, "update", false);
        return;
    }
    *state.update_cancel.lock().unwrap() = CancellationToken::new();
    if let Some(w) = app.get_webview_window("update") {
        let _ = w.emit("check-update", ());
    }
    show(app, "update", false)
}
fn trigger(app: &AppHandle) {
    let state = app.state::<Runtime>();
    crate::logging::write("hotkey", "received", None);
    if state.settings_open.load(Ordering::SeqCst) {
        return;
    }
    if let Some(cancel) = state.quick.lock().unwrap().as_ref() {
        cancel.cancel();
        return;
    }
    if state.busy.swap(true, Ordering::SeqCst) {
        return;
    }
    if app
        .get_webview_window("main")
        .is_some_and(|w| w.is_focused().unwrap_or(false))
    {
        state.busy.store(false, Ordering::SeqCst);
        let _ = hide_impl(app, "main");
        return;
    }
    let prefs = state.prefs.lock().unwrap().clone();
    let cancel = CancellationToken::new();
    if prefs.quick_mode {
        *state.quick.lock().unwrap() = Some(cancel.clone());
        cancel_normal(&state, false);
        if let Some(w) = app.get_webview_window("main") {
            let _ = w.hide();
        }
    }
    let app = app.clone();
    tauri::async_runtime::spawn(async move {
        let trigger = prefs.hotkey.virtual_key;
        let token = cancel.clone();
        let capture =
            tauri::async_runtime::spawn_blocking(move || native::capture(trigger, &token)).await;
        let state = app.state::<Runtime>();
        let result = match capture {
            Ok(Ok((target, text))) => {
                *state.target.lock().unwrap() = Some(target.clone());
                if prefs.quick_mode {
                    if target.selection.is_none()
                        || text.as_ref().is_none_or(|s| s.trim().is_empty())
                    {
                        Err("请先在支持的输入框选中文字，再按快捷键。".into())
                    } else {
                        match active_provider(&state) {
                            Err(e) => Err(e),
                            Ok(provider) => match provider
                                .translate(text.as_deref().unwrap(), &cancel)
                                .await
                            {
                                Err(e) => Err(e.message),
                                Ok(translation) => {
                                    if cancel.is_cancelled() {
                                        Err("快速翻译已取消。".into())
                                    } else {
                                        let current = state.target.clone();
                                        let cancel = cancel.clone();
                                        let owner = app
                                            .get_webview_window("main")
                                            .and_then(|w| w.hwnd().ok())
                                            .map(|h| h.0 as isize)
                                            .unwrap_or(0);
                                        let receipt =
                                            tauri::async_runtime::spawn_blocking(move || {
                                                native::insert(
                                                    &target,
                                                    &translation,
                                                    false,
                                                    trigger,
                                                    &cancel,
                                                    &current,
                                                    owner,
                                                )
                                            })
                                            .await
                                            .unwrap_or_else(|_| Receipt::reject("快速翻译失败。"));
                                        if receipt.submitted() {
                                            if let Some(w) = app.get_webview_window("main") {
                                                let _ = w.emit("clear", ());
                                            }
                                            Ok(())
                                        } else {
                                            Err(receipt.message)
                                        }
                                    }
                                }
                            },
                        }
                    }
                } else {
                    let context = OpenContext {
                        selected: text,
                        target_name: native::display_name(&target.name),
                        has_target: true,
                        message: String::new(),
                    };
                    *state.context.lock().unwrap() = context.clone();
                    if let Some(w) = app.get_webview_window("main") {
                        let _ = w.emit("open", context);
                    }
                    show(&app, "main", true);
                    Ok(())
                }
            }
            Ok(Err(message)) => {
                if prefs.quick_mode {
                    Err(message)
                } else {
                    *state.target.lock().unwrap() = None;
                    let context = OpenContext {
                        message,
                        ..Default::default()
                    };
                    *state.context.lock().unwrap() = context.clone();
                    if let Some(w) = app.get_webview_window("main") {
                        let _ = w.emit("open", context);
                    }
                    show(&app, "main", true);
                    Ok(())
                }
            }
            Err(_) => Err("无法识别原输入位置。".into()),
        };
        if prefs.quick_mode {
            *state.target.lock().unwrap() = None;
            *state.quick.lock().unwrap() = None;
        }
        state.busy.store(false, Ordering::SeqCst);
        if let Err(message) = result {
            crate::logging::write("quick-translation", &message, None);
            notice(&app, &message)
        }
    });
}
#[tauri::command]
fn boot(window: WebviewWindow, state: State<Runtime>) -> Result<Value, String> {
    role(&window, &["main", "settings", "menu", "notice", "update"])?;
    let config = state.config.lock().unwrap().clone();
    let p = state.prefs.lock().unwrap().clone();
    let has_key = !config.base_url.is_empty()
        && state
            .store
            .credential(&config.base_url, "")
            .is_ok_and(|k| !k.is_empty());
    Ok(
        json!({"version":env!("SUIYI_VERSION"),"preferences":p,"colors":theme::colors(&state.prefs.lock().unwrap().theme,theme::dark()),"palettes":theme::PALETTES,"config":{"address":config.base_url,"model":config.model,"hasKey":has_key},"context":if window.label()=="main"{serde_json::to_value(&*state.context.lock().unwrap()).unwrap()}else{Value::Null}}),
    )
}
#[tauri::command]
fn cancel_translation(
    window: WebviewWindow,
    state: State<Runtime>,
    invalidate: bool,
) -> Result<(), String> {
    role(&window, &["main"])?;
    cancel_normal(&state, invalidate);
    Ok(())
}
#[tauri::command]
async fn translate(
    window: WebviewWindow,
    state: State<'_, Runtime>,
    text: String,
    serial: u64,
) -> Result<String, String> {
    role(&window, &["main"])?;
    let provider = active_provider(&state)?;
    cancel_normal(&state, true);
    let generation = state.generation.load(Ordering::SeqCst);
    state.normal_serial.store(serial, Ordering::SeqCst);
    let cancel = CancellationToken::new();
    *state.normal.lock().unwrap() = cancel.clone();
    let cached = state
        .cache
        .lock()
        .unwrap()
        .values
        .get(&text)
        .filter(|(_, time)| time.elapsed() < Duration::from_secs(600))
        .map(|(v, _)| v.clone());
    let result = if let Some(cached) = cached {
        Ok(cached)
    } else {
        let cooldown = *state.cooldown.lock().unwrap();
        if !crate::scheduler::wait(
            &state.schedule,
            cooldown,
            Duration::from_millis(500),
            &cancel,
        )
        .await
        {
            return Err("已取消。".into());
        }
        match provider.translate(&text, &cancel).await {
            Ok(v) => {
                let mut cache = state.cache.lock().unwrap();
                if cancel.is_cancelled() || state.generation.load(Ordering::SeqCst) != generation {
                    return Err("已取消。".into());
                }
                if !cache.values.contains_key(&text) {
                    cache.order.push_back(text.clone())
                }
                cache.values.insert(text, (v.clone(), Instant::now()));
                while cache.values.len() > 128 {
                    if let Some(old) = cache.order.pop_front() {
                        cache.values.remove(&old);
                    }
                }
                Ok(v)
            }
            Err(e) => {
                if let Some(wait) = e.retry_after {
                    let now = Instant::now();
                    *state.cooldown.lock().unwrap() = now
                        .checked_add(wait)
                        .unwrap_or(now + Duration::from_secs(86400));
                } else if e.message.contains("频繁") {
                    *state.cooldown.lock().unwrap() = Instant::now() + Duration::from_secs(5)
                }
                Err(e.message)
            }
        }
    };
    if cancel.is_cancelled() || state.generation.load(Ordering::SeqCst) != generation {
        return Err("已取消。".into());
    }
    state.ready.store(result.is_ok(), Ordering::SeqCst);
    result
}
#[tauri::command]
async fn output(
    window: WebviewWindow,
    state: State<'_, Runtime>,
    text: String,
    serial: u64,
    copy: bool,
) -> Result<Receipt, String> {
    role(&window, &["main"])?;
    if !state.ready.load(Ordering::SeqCst)
        || serial != state.normal_serial.load(Ordering::SeqCst)
        || text.trim().is_empty()
        || crate::store::utf16_len(&text) > 8000
    {
        return Err("请等待当前译文完成。".into());
    }
    if state.busy.swap(true, Ordering::SeqCst) {
        return Err("正在处理，请稍候。".into());
    }
    let cancel = CancellationToken::new();
    let owner = window.hwnd().map(|h| h.0 as isize).unwrap_or(0);
    let target = state.target.lock().unwrap().clone();
    let trigger = state.prefs.lock().unwrap().hotkey.virtual_key;
    let current = state.target.clone();
    let result = match target.filter(|_| !copy) {
        Some(target) => tauri::async_runtime::spawn_blocking(move || {
            Ok(native::insert(
                &target, &text, true, trigger, &cancel, &current, owner,
            ))
        })
        .await
        .unwrap_or_else(|_| Err("回填失败。".into())),
        None => tauri::async_runtime::spawn_blocking(move || {
            native::write_clipboard(&text, owner, &cancel).map(|_| Receipt {
                outcome: "copied".into(),
                message: "已复制".into(),
            })
        })
        .await
        .unwrap_or_else(|_| Err("复制失败。".into())),
    };
    state.busy.store(false, Ordering::SeqCst);
    if let Ok(receipt) = &result {
        if receipt.outcome != "copied" {
            *state.target.lock().unwrap() = None;
        }
        if receipt.submitted() {
            cancel_normal(&state, true);
            let _ = window.hide();
        } else if receipt.outcome == "uncertain" {
            let _ = window.hide();
        } else if receipt.outcome == "rejected" {
            let _ = window.show();
            let _ = window.set_focus();
        }
    }
    result
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SettingsInput {
    address: String,
    model: String,
    key: String,
    hotkey: Hotkey,
    quick_mode: bool,
}
#[tauri::command]
fn save_settings(
    window: WebviewWindow,
    app: AppHandle,
    state: State<Runtime>,
    input: SettingsInput,
) -> Result<(), String> {
    role(&window, &["settings"])?;
    if !input.hotkey.valid() {
        return Err("请使用 Ctrl 或 Alt，搭配字母、数字或 F1–F12。".into());
    }
    let old = state.prefs.lock().unwrap().clone();
    let worker = state.hotkey.lock().unwrap();
    if worker.as_ref().is_some_and(|w| !w.set(input.hotkey)) {
        return Err("快捷键已被其他程序占用，请更换。".into());
    }
    let result = (|| {
        let mut prefs = old.clone();
        prefs.hotkey = input.hotkey;
        prefs.quick_mode = input.quick_mode;
        state.store.save_preferences(&prefs)?;
        let config = match state
            .store
            .save_config(&input.address, &input.model, &input.key)
        {
            Ok(config) => config,
            Err(error) => {
                let _ = state.store.save_preferences(&old);
                return Err(error);
            }
        };
        *state.config.lock().unwrap() = config;
        *state.prefs.lock().unwrap() = prefs;
        Ok(())
    })();
    if result.is_err() {
        if let Some(w) = worker.as_ref() {
            let _ = w.set(old.hotkey);
        }
        return result;
    }
    cancel_normal(&state, true);
    {
        let mut cache = state.cache.lock().unwrap();
        cache.values.clear();
        cache.order.clear();
    }
    let _ = app.emit("config-changed", ());
    refresh_tray_text(&app);
    Ok(())
}
#[tauri::command]
fn reveal_key(
    window: WebviewWindow,
    state: State<Runtime>,
    address: String,
) -> Result<String, String> {
    role(&window, &["settings"])?;
    state.store.credential(&address, "")
}
#[tauri::command]
fn record_shortcut(
    window: WebviewWindow,
    state: State<Runtime>,
    recording: bool,
) -> Result<bool, String> {
    role(&window, &["settings"])?;
    Ok(state
        .hotkey
        .lock()
        .unwrap()
        .as_ref()
        .is_some_and(|w| w.suspend(recording)))
}
#[tauri::command]
async fn models(
    window: WebviewWindow,
    state: State<'_, Runtime>,
    address: String,
    key: String,
) -> Result<Vec<String>, String> {
    role(&window, &["settings"])?;
    let key = state.store.credential(&address, &key)?;
    let cancel = state.settings_cancel.lock().unwrap().clone();
    Provider::models(&state.client, &address, &key, &cancel).await
}
#[tauri::command]
async fn test_connection(
    window: WebviewWindow,
    state: State<'_, Runtime>,
    address: String,
    model: String,
    key: String,
) -> Result<(), String> {
    role(&window, &["settings"])?;
    let provider = provider(&state, &address, &model, &key)?;
    let cancel = state.settings_cancel.lock().unwrap().clone();
    provider
        .translate("你好，世界。", &cancel)
        .await
        .map_err(|e| e.message)?;
    provider
        .translate("第一行。\n\n第二行。", &cancel)
        .await
        .map_err(|e| e.message)?;
    Ok(())
}
fn hide_impl(app: &AppHandle, label: &str) -> Result<(), String> {
    let state = app.state::<Runtime>();
    match label {
        "main" => {
            if state.busy.load(Ordering::SeqCst) {
                return Ok(());
            }
            *state.target.lock().unwrap() = None;
            cancel_normal(&state, false)
        }
        "settings" => {
            state.settings_open.store(false, Ordering::SeqCst);
            state.settings_cancel.lock().unwrap().cancel();
            if let Some(window) = app.get_webview_window("settings") {
                let _ = window.emit("settings-closed", ());
            }
            if let Some(w) = state.hotkey.lock().unwrap().as_ref() {
                let _ = w.suspend(false);
            }
        }
        "update" => {
            state.update_cancel.lock().unwrap().cancel();
            if let Some(file) = state.downloaded.lock().unwrap().take() {
                update::discard(&file)
            }
        }
        _ => (),
    }
    if let Some(w) = app.get_webview_window(label) {
        let _ = w.hide();
    }
    Ok(())
}
#[tauri::command]
fn hide_window(window: WebviewWindow, app: AppHandle) -> Result<(), String> {
    hide_impl(&app, window.label())
}
#[tauri::command]
fn drag(window: WebviewWindow) -> Result<(), String> {
    role(&window, &["main", "settings", "update"])?;
    window.start_dragging().map_err(|_| "无法拖动窗口。".into())
}
#[tauri::command]
fn action(
    window: WebviewWindow,
    app: AppHandle,
    state: State<Runtime>,
    name: String,
    value: Option<String>,
) -> Result<(), String> {
    role(&window, &["menu", "main"])?;
    if let Some(w) = app.get_webview_window("menu") {
        let _ = w.hide();
    }
    match name.as_str() {
        "open" => open_copy(&app),
        "settings" => open_settings(&app),
        "update" => open_update(&app),
        "exit" => app.exit(0),
        "quick" | "startup" | "theme" => {
            let mut next = state.prefs.lock().unwrap().clone();
            match name.as_str() {
                "quick" => {
                    next.quick_mode = !next.quick_mode;
                    if !next.quick_mode
                        && let Some(c) = state.quick.lock().unwrap().as_ref()
                    {
                        c.cancel()
                    }
                }
                "startup" => next.auto_start = !next.auto_start,
                "theme" => {
                    let id = value.ok_or("请选择主题。")?;
                    if !theme::valid(&id) {
                        return Err("主题无效。".into());
                    }
                    next.theme = id
                }
                _ => (),
            };
            let old = state.prefs.lock().unwrap().clone();
            if name == "startup" {
                state.store.startup(next.auto_start)?;
            }
            if let Err(e) = state.store.save_preferences(&next) {
                if name == "startup" {
                    let _ = state.store.startup(old.auto_start);
                }
                return Err(e);
            }
            *state.prefs.lock().unwrap() = next;
            refresh_tray_text(&app);
            refresh_theme(&app);
            let _ = app.emit("preferences-changed", ());
        }
        _ => return Err("未知操作。".into()),
    }
    Ok(())
}
#[tauri::command]
async fn check_update(
    window: WebviewWindow,
    state: State<'_, Runtime>,
) -> Result<update::Check, String> {
    role(&window, &["update"])?;
    let cancel = state.update_cancel.lock().unwrap().clone();
    let result = update::check(&state.client, &cancel).await?;
    *state.installer.lock().unwrap() = result.installer.clone();
    Ok(result)
}
#[tauri::command]
async fn install_update(
    window: WebviewWindow,
    app: AppHandle,
    state: State<'_, Runtime>,
) -> Result<String, String> {
    role(&window, &["update"])?;
    if state.updating.swap(true, Ordering::SeqCst) {
        return Err("正在下载，请稍候。".into());
    }
    let result = async {
        let cancel = state.update_cancel.lock().unwrap().clone();
        let cached = state.downloaded.lock().unwrap().clone();
        let file = if let Some(file) = cached {
            file
        } else {
            let installer = state
                .installer
                .lock()
                .unwrap()
                .clone()
                .ok_or("请重新检查更新。")?;
            let w = window.clone();
            let file = update::download(&installer, &cancel, move |received, total| {
                let _ = w.emit(
                    "download-progress",
                    json!({"received":received,"total":total}),
                );
            })
            .await?;
            if cancel.is_cancelled() {
                update::discard(&file);
                return Err("下载已取消。".to_string());
            }
            *state.downloaded.lock().unwrap() = Some(file.clone());
            file
        };
        if cancel.is_cancelled() {
            update::discard(&file);
            state.downloaded.lock().unwrap().take();
            return Err("更新已取消。".into());
        }
        if state.busy.load(Ordering::SeqCst) || state.settings_open.load(Ordering::SeqCst) {
            return Ok("ready".to_string());
        }
        update::launch(&file)?;
        state.downloaded.lock().unwrap().take();
        app.exit(0);
        Ok("installing".into())
    }
    .await;
    state.updating.store(false, Ordering::SeqCst);
    result
}
#[tauri::command]
fn release_page(window: WebviewWindow, state: State<Runtime>) -> Result<(), String> {
    role(&window, &["update"])?;
    let _ = state;
    update::open_release("https://github.com/whoarei/suiyi/releases/latest")
}

pub fn run() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if args.len() == 2 && matches!(args[0].as_str(), "--inspect-focus" | "--inspect-selection") {
        let window = args[1].parse().unwrap_or(0);
        let observation = native::inspect_mode(window, args[0] == "--inspect-selection");
        println!("{}", serde_json::to_string(&observation).unwrap());
        return;
    }
    let profile = args
        .iter()
        .position(|a| a == "--test-profile")
        .and_then(|i| args.get(i + 1))
        .map(PathBuf::from);
    if profile.as_ref().is_some_and(|p| !p.is_absolute()) {
        return;
    }
    let Some(_instance) = native::single_instance(profile.as_deref()) else {
        return;
    };
    let store = Store::new(profile);
    crate::logging::init(store.root.clone());
    let prefs = store.preferences();
    let config = store.config();
    let runtime = Runtime {
        store: store.clone(),
        prefs: Mutex::new(prefs.clone()),
        config: Mutex::new(config),
        hotkey: Mutex::new(None),
        client: api::client(),
        target: Arc::new(Mutex::new(None)),
        context: Mutex::new(OpenContext::default()),
        busy: AtomicBool::new(false),
        settings_open: AtomicBool::new(false),
        normal: Mutex::new(CancellationToken::new()),
        quick: Mutex::new(None),
        settings_cancel: Mutex::new(CancellationToken::new()),
        update_cancel: Mutex::new(CancellationToken::new()),
        generation: AtomicU64::new(0),
        normal_serial: AtomicU64::new(0),
        ready: AtomicBool::new(false),
        cache: Mutex::new(Cache {
            values: HashMap::new(),
            order: VecDeque::new(),
        }),
        schedule: tokio::sync::Mutex::new(Instant::now() - Duration::from_secs(1)),
        cooldown: Mutex::new(Instant::now()),
        installer: Mutex::new(None),
        downloaded: Mutex::new(None),
        updating: AtomicBool::new(false),
        notice_generation: AtomicU64::new(0),
    };
    let show_main = args.contains(&"--show".into());
    let settings = args.contains(&"--settings".into());
    let updates = args.contains(&"--check-updates".into());
    let diagnostics = args.contains(&"--diagnostics".into());
    let menu = args.contains(&"--menu".into());
    let result = tauri::Builder::default()
        .manage(runtime)
        .invoke_handler(tauri::generate_handler![
            boot,
            translate,
            cancel_translation,
            output,
            save_settings,
            reveal_key,
            record_shortcut,
            models,
            test_connection,
            hide_window,
            drag,
            action,
            check_update,
            install_update,
            release_page
        ])
        .setup(move |app| {
            for (label, title, width, height) in [
                ("main", "随译", 440., 330.),
                ("settings", "随译 · 设置", 420., 392.),
                ("menu", "随译 · 托盘", 244., 322.),
                ("notice", "随译 · 提示", 360., 96.),
                ("update", "随译 · 检查更新", 420., 288.),
            ] {
                let window = tauri::WebviewWindowBuilder::new(
                    app,
                    label,
                    WebviewUrl::App(format!("index.html?view={label}").into()),
                )
                .title(title)
                .inner_size(width, height)
                .resizable(false)
                .decorations(false)
                .transparent(true)
                // DWM shadows add an extra native frame around transparent panels.
                .shadow(false)
                .visible(false)
                .skip_taskbar(!(diagnostics && label == "main"))
                .always_on_top(true)
                .focused(false)
                .build()?;
                if label == "notice" {
                    let _ = window.set_focusable(false);
                    let hwnd = window.hwnd()?.0 as isize;
                    unsafe {
                        let style = crate::ffi::GetWindowLongPtrW(hwnd, -20);
                        crate::ffi::SetWindowLongPtrW(hwnd, -20, style | 0x08000000);
                    }
                    let (x, y) = native::placement(360, 96);
                    let _ = window.set_position(tauri::PhysicalPosition::new(x, y));
                }
                let handle = app.handle().clone();
                let label = label.to_string();
                window.on_window_event(move |event| match event {
                    WindowEvent::CloseRequested { api, .. } => {
                        api.prevent_close();
                        let _ = hide_impl(&handle, &label);
                    }
                    WindowEvent::Focused(false) if label == "menu" => {
                        if let Some(w) = handle.get_webview_window("menu") {
                            let _ = w.hide();
                        }
                    }
                    WindowEvent::Focused(false) if label == "main" => {
                        let _ = hide_impl(&handle, "main");
                    }
                    WindowEvent::Destroyed => {
                        if let Some(w) = handle.get_webview_window(&label)
                            && let Ok(hwnd) = w.hwnd()
                        {
                            native::clear_shell(hwnd.0 as isize);
                        }
                    }
                    _ => (),
                });
            }
            use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
            let icon = theme::icon(&prefs.theme, theme::dark(), 32);
            TrayIconBuilder::with_id("suiyi")
                .icon(tauri::image::Image::new_owned(icon.into_raw(), 32, 32))
                .tooltip(format!("随译 · {}", prefs.hotkey.display()))
                .show_menu_on_left_click(false)
                .on_tray_icon_event(|tray, event| match event {
                    TrayIconEvent::Click {
                        button: MouseButton::Right,
                        button_state: MouseButtonState::Up,
                        ..
                    } => {
                        let app = tray.app_handle();
                        if let Some(w) = app.get_webview_window("menu") {
                            let _ = w.emit("menu-open", ());
                        }
                        show(app, "menu", true)
                    }
                    TrayIconEvent::DoubleClick {
                        button: MouseButton::Left,
                        ..
                    } => open_copy(tray.app_handle()),
                    _ => (),
                })
                .build(app)?;
            refresh_tray_text(app.handle());
            let handle = app.handle().clone();
            let (worker, registered) = HotkeyWorker::new(prefs.hotkey, move || trigger(&handle));
            *app.state::<Runtime>().hotkey.lock().unwrap() = Some(worker);
            if !registered {
                notice(app.handle(), "快捷键已被占用，请从托盘设置更换。");
            }
            let _ = store.startup(prefs.auto_start);
            native::setup_exit_watch(app.handle().clone(), store.isolated);
            refresh_theme(app.handle());
            let a = app.handle().clone();
            std::thread::spawn(move || {
                let mut previous = theme::dark();
                loop {
                    std::thread::sleep(Duration::from_secs(2));
                    let dark = theme::dark();
                    if previous != dark {
                        previous = dark;
                        refresh_theme(&a)
                    }
                }
            });
            if menu {
                show(app.handle(), "menu", true)
            } else if settings {
                open_settings(app.handle())
            } else if updates {
                open_update(app.handle())
            } else if show_main {
                open_copy(app.handle())
            }
            Ok(())
        })
        .build(tauri::generate_context!());
    match result {
        Ok(app) => app.run(|app, event| match event {
            tauri::RunEvent::ExitRequested {
                code: None, api, ..
            } => api.prevent_exit(),
            tauri::RunEvent::Exit => {
                let state = app.state::<Runtime>();
                cancel_normal(&state, true);
                if let Some(c) = state.quick.lock().unwrap().as_ref() {
                    c.cancel()
                }
                state.settings_cancel.lock().unwrap().cancel();
                state.update_cancel.lock().unwrap().cancel();
                for (_, w) in app.webview_windows() {
                    if let Ok(h) = w.hwnd() {
                        native::clear_shell(h.0 as isize);
                    }
                }
            }
            _ => (),
        }),
        Err(_) => unsafe {
            crate::ffi::MessageBoxW(0,crate::ffi::wide("随译未能启动。请确认 Microsoft WebView2 Runtime 已安装；推荐运行随译安装包，它会在缺少时自动下载安装该组件。").as_ptr(),crate::ffi::wide("随译").as_ptr(),0x10);
        },
    }
}
