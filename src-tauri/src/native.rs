#![allow(unsafe_op_in_unsafe_fn)]
use crate::{
    ffi::*,
    store::{Hotkey, utf16_len},
};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    io::Read,
    os::windows::process::CommandExt,
    path::{Path, PathBuf},
    process::{Command, Stdio},
    sync::{Arc, Mutex, mpsc},
    thread,
    time::{Duration, Instant},
};
use tokio_util::sync::CancellationToken;
use windows::{
    Win32::{
        Foundation::{HWND, PROPERTYKEY},
        System::{Com::*, Ole::*},
        UI::{Accessibility::*, Shell::PropertiesSystem::*, Shell::*},
    },
    core::{Interface, PCWSTR},
};

#[derive(Clone, Serialize, Deserialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Selection {
    pub method: String,
    pub fingerprint: String,
    pub start: u32,
    pub end: u32,
}
#[derive(Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct Observation {
    pub available: bool,
    pub belongs: bool,
    pub enabled: bool,
    pub password: bool,
    pub read_only: Option<bool>,
    pub editable: Option<bool>,
    pub identity: Option<Vec<i32>>,
    pub state: String,
    pub text: Option<String>,
    pub selection: Option<Selection>,
}
#[derive(Clone)]
pub struct Target {
    pub id: String,
    pub window: isize,
    pub focus: isize,
    pub pid: u32,
    pub creation: u64,
    pub name: String,
    pub identity: Option<Vec<i32>>,
    pub selection: Option<Selection>,
}
pub fn display_name(process: &str) -> String {
    match process.to_lowercase().as_str() {
        "weixin" | "wechat" => "微信",
        "notepad" => "记事本",
        "chrome" => "Google Chrome",
        "msedge" => "Microsoft Edge",
        "firefox" => "Firefox",
        "code" => "Visual Studio Code",
        "winword" => "Word",
        "excel" => "Excel",
        "translationfloat.testtarget" => "测试编辑器",
        _ => process,
    }
    .into()
}
#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Receipt {
    pub outcome: String,
    pub message: String,
}
impl Receipt {
    pub fn reject(message: &str) -> Self {
        Self {
            outcome: "rejected".into(),
            message: message.into(),
        }
    }
    pub fn submitted(&self) -> bool {
        self.outcome == "submitted"
    }
}
fn root() -> isize {
    unsafe { GetAncestor(GetForegroundWindow(), 2) }
}
fn focus(window: isize) -> isize {
    unsafe {
        let mut pid = 0;
        let tid = GetWindowThreadProcessId(window, &mut pid);
        let mut info = GuiInfo {
            size: size_of::<GuiInfo>() as u32,
            ..Default::default()
        };
        if GetGUIThreadInfo(tid, &mut info) != 0 {
            info.focus
        } else {
            0
        }
    }
}
fn native_edit(hwnd: isize) -> bool {
    unsafe {
        let mut class = [0u16; 256];
        let n = GetClassNameW(hwnd, class.as_mut_ptr(), 256);
        let class = String::from_utf16_lossy(&class[..n.max(0) as usize]).to_lowercase();
        class.starts_with("edit")
            || class.starts_with("richedit")
            || class.starts_with("windowsforms10.edit.")
            || class.starts_with("windowsforms10.richedit")
    }
}
fn editable(hwnd: isize) -> bool {
    unsafe {
        IsWindowEnabled(hwnd) != 0
            && (!native_edit(hwnd) || (GetWindowLongPtrW(hwnd, -16) & (0x20 | 0x800)) == 0)
    }
}
fn identity(window: isize) -> Option<(u32, u64, String)> {
    unsafe {
        let mut pid = 0;
        GetWindowThreadProcessId(window, &mut pid);
        let process = OpenProcess(0x1000, 0, pid);
        if process == 0 {
            return None;
        }
        let (mut creation, mut exit, mut kernel, mut user) = (0, 0, 0, 0);
        let ok = GetProcessTimes(process, &mut creation, &mut exit, &mut kernel, &mut user);
        let mut name = vec![0u16; 32768];
        let mut len = name.len() as u32;
        QueryFullProcessImageNameW(process, 0, name.as_mut_ptr(), &mut len);
        CloseHandle(process);
        if ok == 0 {
            return None;
        }
        let name = PathBuf::from(String::from_utf16_lossy(&name[..len as usize]))
            .file_stem()?
            .to_string_lossy()
            .into_owned();
        Some((pid, creation, name))
    }
}
fn same_focus(t: &Target) -> bool {
    root() == t.window && (t.focus == 0 || focus(t.window) == t.focus)
}
fn alive(t: &Target) -> bool {
    (unsafe { IsWindow(t.window) != 0 && (t.focus == 0 || IsWindow(t.focus) != 0) })
        && identity(t.window)
            .is_some_and(|(pid, creation, _)| pid == t.pid && creation == t.creation)
}
fn wait_keys(trigger: u32, cancel: &CancellationToken) -> bool {
    let start = Instant::now();
    while start.elapsed() < Duration::from_millis(600) {
        if cancel.is_cancelled() {
            return false;
        }
        let down = [0x10, 0x11, 0x12, 0x0d, 0x5b, 0x5c, trigger as i32, 0x43]
            .iter()
            .any(|key| unsafe { GetAsyncKeyState(*key) as u16 & 0x8000 != 0 });
        if !down {
            return true;
        }
        thread::sleep(Duration::from_millis(15))
    }
    false
}
fn native_range(hwnd: isize) -> Option<(u32, u32)> {
    unsafe {
        let (mut start, mut end, mut result) = (0u32, 0u32, 0usize);
        if SendMessageTimeoutW(
            hwnd,
            0xb0,
            &mut start as *mut _ as usize,
            &mut end as *mut _ as isize,
            2,
            100,
            &mut result,
        ) == 0
        {
            None
        } else {
            Some((start, end))
        }
    }
}
pub fn sequence() -> u32 {
    unsafe { GetClipboardSequenceNumber() }
}
fn snapshot(text: &str, method: &str, location: &str, start: u32, end: u32) -> Selection {
    Selection {
        method: method.into(),
        fingerprint: format!(
            "{:x}",
            Sha256::digest(format!("{text}\0{location}").as_bytes())
        ),
        start,
        end,
    }
}
fn native_selection(
    t: &Target,
    trigger: u32,
    cancel: &CancellationToken,
) -> Result<(Option<Selection>, Option<String>), String> {
    unsafe {
        if !wait_keys(trigger, cancel) || !same_focus(t) || !editable(t.focus) {
            return Err("输入焦点已变化，请重新选择。".into());
        }
        let (start, end) = native_range(t.focus).ok_or("无法读取原选区。")?;
        if start == end {
            return Ok((None, None));
        }
        if end < start || end - start > 2000 {
            return Err("选区超过 2000 个 UTF-16 代码单元，请缩小选区。".into());
        }
        let before = sequence();
        let inputs = chord(0x43);
        let submitted = SendInput(4, inputs.as_ptr(), size_of::<Input>() as i32);
        if submitted != 4 {
            if submitted > 0 {
                let release = [key(0x43, true), key(0x11, true)];
                SendInput(2, release.as_ptr(), size_of::<Input>() as i32);
            }
            return Err("未能读取选中文字。".into());
        }
        let start_time = Instant::now();
        while start_time.elapsed() < Duration::from_millis(350) {
            if cancel.is_cancelled() || !same_focus(t) {
                return Err("读取选区已取消或焦点已变化。".into());
            }
            let current = sequence();
            if current != 0 && current != before {
                let mut owner = 0;
                GetWindowThreadProcessId(GetClipboardOwner(), &mut owner);
                if owner != t.pid {
                    return Err("剪贴板已被其他程序修改。".into());
                }
                if let Some(text) = read_unicode(current) {
                    if utf16_len(&text) > 2000 {
                        return Err("选中文字过长。".into());
                    }
                    if native_range(t.focus) == Some((start, end)) && same_focus(t) {
                        return Ok((
                            Some(snapshot(
                                &text,
                                "native",
                                &format!("{start}:{end}"),
                                start,
                                end,
                            )),
                            Some(text),
                        ));
                    }
                }
            }
            thread::sleep(Duration::from_millis(15))
        }
        Err("未能读取原选区，请重新选择或手动输入。".into())
    }
}
fn read_unicode(expected: u32) -> Option<String> {
    unsafe {
        if OpenClipboard(0) == 0 {
            return None;
        }
        let result = (|| {
            if sequence() != expected {
                return None;
            }
            let handle = GetClipboardData(13);
            let ptr = GlobalLock(handle) as *const u16;
            if ptr.is_null() {
                return None;
            }
            let capacity = (GlobalSize(handle) / 2).min(2001);
            let data = std::slice::from_raw_parts(ptr, capacity);
            let length = data.iter().position(|c| *c == 0).unwrap_or(capacity);
            let text = String::from_utf16_lossy(&data[..length]);
            GlobalUnlock(handle);
            if sequence() != expected {
                None
            } else {
                Some(text)
            }
        })();
        CloseClipboard();
        result
    }
}
pub fn write_clipboard(
    text: &str,
    owner: isize,
    cancel: &CancellationToken,
) -> Result<u32, String> {
    unsafe {
        for _ in 0..4 {
            if cancel.is_cancelled() {
                return Err("已取消。".into());
            }
            if OpenClipboard(owner) != 0 {
                let result = (|| {
                    let data = wide(text);
                    let handle = GlobalAlloc(2, data.len() * 2);
                    if handle == 0 {
                        return Err("剪贴板内存不足。".into());
                    }
                    let ptr = GlobalLock(handle) as *mut u16;
                    if ptr.is_null() {
                        GlobalFree(handle);
                        return Err("无法写入剪贴板。".into());
                    }
                    std::ptr::copy_nonoverlapping(data.as_ptr(), ptr, data.len());
                    GlobalUnlock(handle);
                    if EmptyClipboard() == 0 || SetClipboardData(13, handle) == 0 {
                        GlobalFree(handle);
                        return Err("无法写入剪贴板。".into());
                    }
                    Ok(sequence())
                })();
                CloseClipboard();
                // Windows may finalize synthesized formats while closing the clipboard.
                // Capture the committed sequence only after CloseClipboard, as WPF did.
                return result.map(|_| sequence());
            }
            thread::sleep(Duration::from_millis(35))
        }
        Err("剪贴板被占用，请重试。".into())
    }
}
pub fn probe(window: isize, selection: bool) -> Observation {
    let result = (|| -> Option<Observation> {
        let mut child = Command::new(std::env::current_exe().ok()?)
            .args([
                if selection {
                    "--inspect-selection"
                } else {
                    "--inspect-focus"
                },
                &window.to_string(),
            ])
            .env_remove("DEEPSEEK_API_KEY")
            .creation_flags(0x08000000)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()
            .ok()?;
        let output = child.stdout.take()?;
        let (tx, rx) = mpsc::channel();
        thread::spawn(move || {
            let mut data = vec![];
            let _ = output.take(32769).read_to_end(&mut data);
            let _ = tx.send(data);
        });
        let start = Instant::now();
        loop {
            if let Ok(Some(status)) = child.try_wait() {
                if !status.success() {
                    return None;
                }
                let data = rx.recv_timeout(Duration::from_millis(100)).ok()?;
                return if data.len() <= 32768 {
                    serde_json::from_slice(&data).ok()
                } else {
                    None
                };
            }
            if start.elapsed() >= Duration::from_millis(900) {
                let _ = child.kill();
                let _ = child.wait();
                return None;
            }
            thread::sleep(Duration::from_millis(10))
        }
    })();
    result.unwrap_or_default()
}
pub fn capture(
    trigger: u32,
    cancel: &CancellationToken,
) -> Result<(Target, Option<String>), String> {
    unsafe {
        let window = root();
        let (pid, creation, name) = identity(window).ok_or("未找到原输入位置。")?;
        if pid == std::process::id() {
            return Err("请先在目标输入框选中文字，再按快捷键。".into());
        }
        let focus = focus(window);
        if focus != 0 && (GetAncestor(focus, 2) != window || !editable(focus)) {
            return Err("此控件为密码、只读或不可用区域。".into());
        }
        let observation = if focus != 0 && native_edit(focus) {
            Observation {
                available: true,
                belongs: true,
                enabled: true,
                editable: Some(true),
                ..Default::default()
            }
        } else {
            probe(window, true)
        };
        if observation.available
            && (!observation.belongs
                || !observation.enabled
                || observation.password
                || observation.read_only == Some(true)
                || observation.editable == Some(false))
        {
            return Err("当前位置不是可用的可编辑输入框。".into());
        }
        let identity = if observation.editable == Some(true) {
            observation.identity
        } else {
            None
        };
        if focus == 0 && identity.is_none() {
            return Err("无法确认原输入位置，可以使用复制模式。".into());
        }
        if matches!(observation.state.as_str(), "tooLong" | "multiple") {
            return Err("选区过长或包含多个范围，请只选择一段不超过 2000 的文字。".into());
        }
        let mut target = Target {
            id: uuid::Uuid::new_v4().to_string(),
            window,
            focus,
            pid,
            creation,
            name,
            identity,
            selection: observation.selection,
        };
        let mut text = observation.text;
        if focus != 0 && native_edit(focus) {
            (target.selection, text) = native_selection(&target, trigger, cancel)?
        }
        if !same_focus(&target) || cancel.is_cancelled() {
            return Err("输入焦点已改变，请重新唤出。".into());
        }
        Ok((target, text))
    }
}
fn verify(t: &Target) -> bool {
    if !alive(t) || !same_focus(t) || (t.focus != 0 && !editable(t.focus)) {
        crate::logging::write(
            "verify",
            &format!(
                "alive={} focus={} editable={}",
                alive(t),
                same_focus(t),
                t.focus == 0 || editable(t.focus)
            ),
            Some(&t.name),
        );
        return false;
    }
    if let Some(identity) = &t.identity {
        let o = probe(
            t.window,
            t.selection
                .as_ref()
                .is_some_and(|s| s.method == "automation"),
        );
        if !o.available
            || !o.belongs
            || !o.enabled
            || o.password
            || o.read_only == Some(true)
            || o.editable != Some(true)
            || o.identity.as_ref() != Some(identity)
        {
            crate::logging::write(
                "verify-uia",
                &format!(
                    "available={} belongs={} enabled={} password={} readonly={:?} editable={:?} identity={}",
                    o.available,
                    o.belongs,
                    o.enabled,
                    o.password,
                    o.read_only,
                    o.editable,
                    o.identity.as_ref() == Some(identity)
                ),
                Some(&t.name),
            );
            return false;
        }
        if t.selection
            .as_ref()
            .is_some_and(|s| s.method == "automation")
            && o.selection != t.selection
        {
            crate::logging::write("verify-selection", "changed", Some(&t.name));
            return false;
        }
    }
    if let Some(s) = &t.selection
        && s.method == "native"
        && native_range(t.focus) != Some((s.start, s.end))
    {
        return false;
    }
    alive(t) && same_focus(t)
}
pub fn insert(
    t: &Target,
    text: &str,
    restore: bool,
    trigger: u32,
    cancel: &CancellationToken,
    current: &Arc<Mutex<Option<Target>>>,
    owner: isize,
) -> Receipt {
    let valid = || {
        !cancel.is_cancelled()
            && current
                .lock()
                .unwrap()
                .as_ref()
                .is_some_and(|v| v.id == t.id)
            && alive(t)
    };
    if text.trim().is_empty() || !valid() {
        return Receipt::reject("原输入位置已失效，请重新唤出。");
    }
    if !wait_keys(trigger, cancel) {
        crate::logging::write("insert", "keys-not-released", Some(&t.name));
        return Receipt::reject("请松开快捷键，再试一次。");
    }
    if restore {
        unsafe {
            if IsIconic(t.window) != 0 {
                ShowWindowAsync(t.window, 9);
            }
            SetForegroundWindow(t.window);
        }
        for _ in 0..20 {
            if same_focus(t) || !valid() {
                break;
            }
            thread::sleep(Duration::from_millis(20))
        }
    }
    if !valid() || !verify(t) {
        crate::logging::write("insert", "restore-or-verify-rejected", Some(&t.name));
        return Receipt::reject("原窗口、输入焦点或选区已改变，未替换。");
    }
    let selection_valid = if t.selection.as_ref().is_some_and(|s| s.method == "native") {
        native_selection(t, trigger, cancel).is_ok_and(|(selection, _)| selection == t.selection)
    } else {
        true
    };
    let sequence = match write_clipboard(text, owner, cancel) {
        Ok(s) => s,
        Err(e) => return Receipt::reject(&e),
    };
    if !selection_valid || !valid() || !verify(t) {
        return Receipt::reject("原选区或焦点已改变，译文已留在剪贴板。");
    }
    if sequence == 0 || self::sequence() != sequence {
        return Receipt::reject("剪贴板已改变，已停止回填。");
    }
    if !valid() {
        return Receipt::reject("回填已取消。");
    }
    *current.lock().unwrap() = None;
    unsafe {
        let inputs = chord(0x56);
        let submitted = SendInput(4, inputs.as_ptr(), size_of::<Input>() as i32);
        if submitted == 4 {
            Receipt {
                outcome: "submitted".into(),
                message: "已执行粘贴，请确认输入框内容。".into(),
            }
        } else if submitted > 0 {
            let release = [key(0x56, true), key(0x11, true)];
            SendInput(2, release.as_ptr(), size_of::<Input>() as i32);
            Receipt {
                outcome: "uncertain".into(),
                message: "粘贴结果不确定，请检查输入框；不会自动重试。".into(),
            }
        } else {
            Receipt::reject("系统未接受粘贴，请检查权限或手动粘贴。")
        }
    }
}
pub fn placement(width: i32, height: i32) -> (i32, i32) {
    unsafe {
        let scale = GetDpiForWindow(GetForegroundWindow()).max(96) as f64 / 96.;
        placement_physical(
            (width as f64 * scale) as i32,
            (height as f64 * scale) as i32,
        )
    }
}
fn constrain_position(x: i32, y: i32, width: i32, height: i32, work: Rect) -> (i32, i32) {
    (
        x.clamp(work.left, work.right.saturating_sub(width).max(work.left)),
        y.clamp(work.top, work.bottom.saturating_sub(height).max(work.top)),
    )
}
pub fn keep_on_screen(x: i32, y: i32, width: u32, height: u32) -> (i32, i32) {
    unsafe {
        let monitor = MonitorFromPoint(Point { x, y }, 2);
        let mut info = MonitorInfo {
            size: size_of::<MonitorInfo>() as u32,
            ..Default::default()
        };
        if GetMonitorInfoW(monitor, &mut info) == 0 {
            return (x, y);
        }
        constrain_position(
            x,
            y,
            width.min(i32::MAX as u32) as i32,
            height.min(i32::MAX as u32) as i32,
            info.work,
        )
    }
}
pub fn placement_physical(width: i32, height: i32) -> (i32, i32) {
    unsafe {
        let mut cursor = Point::default();
        GetCursorPos(&mut cursor);
        let monitor = MonitorFromPoint(cursor, 2);
        let mut info = MonitorInfo {
            size: size_of::<MonitorInfo>() as u32,
            ..Default::default()
        };
        GetMonitorInfoW(monitor, &mut info);
        constrain_position(cursor.x + 14, cursor.y + 14, width, height, info.work)
    }
}

// UIA runs before Tauri initialization, in a short-lived, killable subprocess.
pub fn inspect_mode(window: isize, read_selection: bool) -> Observation {
    unsafe {
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
        let result = (|| -> windows::core::Result<Observation> {
            let uia: IUIAutomation = CoCreateInstance(&CUIAutomation, None, CLSCTX_INPROC_SERVER)?;
            let focused = uia.GetFocusedElement()?;
            let mut ancestor = Some(focused.clone());
            let mut belongs = false;
            let walker = uia.RawViewWalker()?;
            for _ in 0..32 {
                let Some(element) = ancestor else { break };
                let hwnd = element.CurrentNativeWindowHandle()?;
                if hwnd.0 as isize != 0 && GetAncestor(hwnd.0 as isize, 2) == window {
                    belongs = true;
                    break;
                }
                ancestor = walker.GetParentElement(&element).ok()
            }
            let mut read_only = None;
            if let Ok(value) =
                focused.GetCurrentPatternAs::<IUIAutomationValuePattern>(UIA_ValuePatternId)
            {
                read_only = Some(value.CurrentIsReadOnly()?.as_bool())
            } else if let Ok(text) =
                focused.GetCurrentPatternAs::<IUIAutomationTextPattern>(UIA_TextPatternId)
            {
                let v = text
                    .DocumentRange()?
                    .GetAttributeValue(UIA_IsReadOnlyAttributeId)?;
                read_only = bool::try_from(&v).ok()
            }
            let control = focused.CurrentControlType()?.0;
            let editable = read_only.map(|v| !v).or(
                if matches!(control, 50004 | 50030 | 50025 | 50033 | 50032 | 50003) {
                    None
                } else {
                    Some(false)
                },
            );
            let identity = safe_array::<i32>(focused.GetRuntimeId()?)
                .filter(|v| !v.is_empty() && v.len() <= 64);
            let mut o = Observation {
                available: true,
                belongs,
                enabled: focused.CurrentIsEnabled()?.as_bool(),
                password: focused.CurrentIsPassword()?.as_bool(),
                read_only,
                editable,
                identity,
                state: "unavailable".into(),
                ..Default::default()
            };
            if read_selection
                && o.belongs
                && o.enabled
                && !o.password
                && o.editable == Some(true)
                && o.identity.is_some()
                && let Ok(pattern) =
                    focused.GetCurrentPatternAs::<IUIAutomationTextPattern>(UIA_TextPatternId)
            {
                let ranges = pattern.GetSelection()?;
                let count = ranges.Length()?;
                if count == 0 {
                    o.state = "none".into()
                } else if count != 1 {
                    o.state = "multiple".into()
                } else {
                    let range = ranges.GetElement(0)?;
                    if range.CompareEndpoints(
                        TextPatternRangeEndpoint_Start,
                        &range,
                        TextPatternRangeEndpoint_End,
                    )? == 0
                    {
                        o.state = "none".into()
                    } else {
                        let text = range.GetText(2001)?.to_string();
                        if utf16_len(&text) > 2000 {
                            o.state = "tooLong".into()
                        } else if let Some(rects) =
                            safe_array::<f64>(range.GetBoundingRectangles()?)
                            && !rects.is_empty()
                        {
                            let location = rects
                                .iter()
                                .map(|n| format!("{n:.1}"))
                                .collect::<Vec<_>>()
                                .join(",");
                            o.selection = Some(snapshot(&text, "automation", &location, 0, 0));
                            o.text = Some(text);
                            o.state = "selected".into()
                        }
                    }
                }
            }
            Ok(o)
        })();
        CoUninitialize();
        result.unwrap_or_default()
    }
}
unsafe fn safe_array<T: Copy>(array: *mut SAFEARRAY) -> Option<Vec<T>> {
    if array.is_null() {
        return None;
    }
    let result = (|| {
        let l = SafeArrayGetLBound(array, 1).ok()?;
        let u = SafeArrayGetUBound(array, 1).ok()?;
        if u < l || u - l > 20000 {
            return None;
        }
        let mut data = std::ptr::null_mut();
        SafeArrayAccessData(array, &mut data).ok()?;
        let values = std::slice::from_raw_parts(data as *const T, (u - l + 1) as usize).to_vec();
        let _ = SafeArrayUnaccessData(array);
        Some(values)
    })();
    let _ = SafeArrayDestroy(array);
    result
}

enum HotkeyCommand {
    Set(Hotkey, mpsc::Sender<bool>),
    Suspend(bool, mpsc::Sender<bool>),
}
pub struct HotkeyWorker {
    thread: u32,
    send: mpsc::Sender<HotkeyCommand>,
}
impl Drop for HotkeyWorker {
    fn drop(&mut self) {
        unsafe {
            PostThreadMessageW(self.thread, 0x0012, 0, 0);
        }
    }
}
impl HotkeyWorker {
    pub fn new(initial: Hotkey, callback: impl Fn() + Send + 'static) -> (Self, bool) {
        let (tx, rx) = mpsc::channel();
        let (ready, receive) = mpsc::channel();
        thread::spawn(move || unsafe {
            let mut msg = Msg::default();
            PeekMessageW(&mut msg, 0, 0, 0, 0);
            let thread = GetCurrentThreadId();
            let mut hotkey = initial;
            let mut current_id = 1;
            let mut registered =
                RegisterHotKey(0, 1, hotkey.modifiers | 0x4000, hotkey.virtual_key) != 0;
            let mut suspended = false;
            let _ = ready.send((thread, registered));
            while GetMessageW(&mut msg, 0, 0, 0) > 0 {
                if msg.message == 0x0312 {
                    callback()
                }
                if msg.message == 0x8001 {
                    while let Ok(command) = rx.try_recv() {
                        match command {
                            HotkeyCommand::Set(next, reply) => {
                                let candidate_id = if current_id == 1 { 2 } else { 1 };
                                let success = if next == hotkey && (registered || suspended) {
                                    true
                                } else if RegisterHotKey(
                                    0,
                                    candidate_id,
                                    next.modifiers | 0x4000,
                                    next.virtual_key,
                                ) != 0
                                {
                                    if registered {
                                        UnregisterHotKey(0, current_id);
                                    }
                                    hotkey = next;
                                    current_id = candidate_id;
                                    if suspended {
                                        UnregisterHotKey(0, current_id);
                                    }
                                    registered = !suspended;
                                    suspended || registered
                                } else {
                                    false
                                };
                                let _ = reply.send(success);
                            }
                            HotkeyCommand::Suspend(value, reply) => {
                                suspended = value;
                                if value {
                                    if registered {
                                        UnregisterHotKey(0, current_id);
                                    }
                                    registered = false;
                                    let _ = reply.send(true);
                                } else {
                                    registered = registered
                                        || RegisterHotKey(
                                            0,
                                            current_id,
                                            hotkey.modifiers | 0x4000,
                                            hotkey.virtual_key,
                                        ) != 0;
                                    let _ = reply.send(registered);
                                }
                            }
                        }
                    }
                }
            }
            if registered {
                UnregisterHotKey(0, current_id);
            }
        });
        let (thread, registered) = receive.recv().unwrap();
        (Self { thread, send: tx }, registered)
    }
    pub fn set(&self, key: Hotkey) -> bool {
        if !key.valid() {
            return false;
        }
        let (tx, rx) = mpsc::channel();
        if self.send.send(HotkeyCommand::Set(key, tx)).is_err() {
            return false;
        }
        unsafe {
            PostThreadMessageW(self.thread, 0x8001, 0, 0);
        }
        rx.recv_timeout(Duration::from_secs(2)).unwrap_or(false)
    }
    pub fn suspend(&self, value: bool) -> bool {
        let (tx, rx) = mpsc::channel();
        let _ = self.send.send(HotkeyCommand::Suspend(value, tx));
        unsafe {
            PostThreadMessageW(self.thread, 0x8001, 0, 0);
        }
        rx.recv_timeout(Duration::from_secs(2)).unwrap_or(false)
    }
}
pub struct Instance(pub isize);
impl Drop for Instance {
    fn drop(&mut self) {
        unsafe {
            CloseHandle(self.0);
        }
    }
}
pub fn single_instance(isolated: Option<&Path>) -> Option<Instance> {
    let name = if let Some(path) = isolated {
        format!(
            "Local\\Suiyi.Test.{:x}",
            Sha256::digest(path.to_string_lossy().as_bytes())
        )
    } else {
        "Local\\TranslationFloat.M0".into()
    };
    unsafe {
        let handle = CreateMutexW(std::ptr::null(), 1, wide(&name).as_ptr());
        if handle == 0 {
            return None;
        }
        if GetLastError() == 183 {
            CloseHandle(handle);
            None
        } else {
            Some(Instance(handle))
        }
    }
}
pub fn setup_exit_watch(app: tauri::AppHandle, isolated: bool) {
    if isolated {
        return;
    }
    thread::spawn(move || unsafe {
        let handle = CreateEventW(
            std::ptr::null(),
            0,
            0,
            wide("Local\\Suiyi.ExitForSetup").as_ptr(),
        );
        if handle != 0 {
            if WaitForSingleObject(handle, u32::MAX) == 0 {
                app.exit(0)
            }
            CloseHandle(handle);
        }
    });
}

pub fn shell_icon(hwnd: isize, path: &Path) {
    unsafe {
        if let Ok(store) = SHGetPropertyStoreForWindow::<IPropertyStore>(HWND(hwnd as *mut _)) {
            use windows::Win32::System::Com::StructuredStorage::PROPVARIANT;
            let group = windows::core::GUID::from_u128(0x9f4c28559f794b39a8d0e1d42de1d5f3);
            for (id, text) in [
                (
                    2,
                    format!(
                        "\"{}\"",
                        std::env::current_exe().unwrap_or_default().display()
                    ),
                ),
                (3, format!("{},0", path.display())),
                (4, "随译".into()),
                (5, "Suiyi.Desktop".into()),
            ] {
                let key = PROPERTYKEY {
                    fmtid: group,
                    pid: id,
                };
                let value = PROPVARIANT::from(text.as_str());
                let _ = store.SetValue(&key, &value);
            }
        }
    }
}
pub fn clear_shell(hwnd: isize) {
    unsafe {
        if let Ok(store) = SHGetPropertyStoreForWindow::<IPropertyStore>(HWND(hwnd as *mut _)) {
            for pid in 2..=5 {
                let key = PROPERTYKEY {
                    fmtid: windows::core::GUID::from_u128(0x9f4c28559f794b39a8d0e1d42de1d5f3),
                    pid,
                };
                let _ = store.SetValue(&key, &Default::default());
            }
        }
    }
}
pub fn refresh_shortcuts(icon: &Path) {
    unsafe {
        let appdata = PathBuf::from(std::env::var_os("APPDATA").unwrap_or_default());
        let mut files = vec![appdata.join("Microsoft/Windows/Start Menu/Programs/随译.lnk")];
        if let Ok(entries) = std::fs::read_dir(
            appdata.join("Microsoft/Internet Explorer/Quick Launch/User Pinned/TaskBar"),
        ) {
            files.extend(
                entries
                    .flatten()
                    .map(|e| e.path())
                    .filter(|p| p.extension().is_some_and(|s| s == "lnk")),
            )
        }
        let exe = std::env::current_exe().ok();
        for file in files {
            let result = (|| -> windows::core::Result<()> {
                let link: IShellLinkW = CoCreateInstance(&ShellLink, None, CLSCTX_INPROC_SERVER)?;
                let persist: IPersistFile = link.cast()?;
                persist.Load(
                    PCWSTR(wide(&file.to_string_lossy()).as_ptr()),
                    STGM_READWRITE,
                )?;
                let mut buffer = vec![0u16; 32768];
                link.GetPath(&mut buffer, std::ptr::null_mut(), SLGP_RAWPATH.0 as u32)?;
                let length = buffer.iter().position(|v| *v == 0).unwrap_or(buffer.len());
                let target = PathBuf::from(String::from_utf16_lossy(&buffer[..length]));
                if !exe
                    .as_ref()
                    .is_some_and(|exe| same_local_executable(&target, exe))
                {
                    return Ok(());
                }
                link.SetIconLocation(PCWSTR(wide(&icon.to_string_lossy()).as_ptr()), 0)?;
                persist.Save(PCWSTR(wide(&file.to_string_lossy()).as_ptr()), true)?;
                SHChangeNotify(
                    SHCNE_UPDATEITEM,
                    SHCNF_PATHW | SHCNF_FLUSH,
                    Some(wide(&file.to_string_lossy()).as_ptr() as *const _),
                    None,
                );
                Ok(())
            })();
            let _ = result;
        }
    }
}

fn same_local_executable(target: &Path, exe: &Path) -> bool {
    let raw = target.to_string_lossy();
    if raw.starts_with("\\\\") || !target.is_absolute() {
        return false;
    }
    let target_drive = raw.as_bytes().get(..2);
    let exe_text = exe.to_string_lossy();
    if !target_drive
        .zip(exe_text.as_bytes().get(..2))
        .is_some_and(|(a, b)| a.eq_ignore_ascii_case(b))
    {
        return false;
    }
    let Some(name) = target.file_name().and_then(|v| v.to_str()) else {
        return false;
    };
    if !name.contains('~')
        && !exe
            .file_name()
            .is_some_and(|v| v.to_string_lossy().eq_ignore_ascii_case(name))
    {
        return false;
    }
    match (target.canonicalize(), exe.canonicalize()) {
        (Ok(a), Ok(b)) => a
            .to_string_lossy()
            .eq_ignore_ascii_case(&b.to_string_lossy()),
        _ => false,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn menu_growth_stays_inside_negative_coordinate_monitor_work_area() {
        let work = Rect {
            left: -1920,
            top: 0,
            right: 0,
            bottom: 1040,
        };
        assert_eq!(constrain_position(-200, 900, 244, 380, work), (-244, 660));
        assert_eq!(constrain_position(-2100, -100, 244, 380, work), (-1920, 0));
        assert_eq!(constrain_position(-1500, 200, 244, 380, work), (-1500, 200));
        assert_eq!(constrain_position(-200, 900, 3000, 1200, work), (-1920, 0));
    }
    #[test]
    fn native_layout() {
        assert_eq!(size_of::<Input>(), 40);
        assert_eq!(size_of::<GuiInfo>(), 72);
        assert_eq!(chord(0x56).len(), 4)
    }
    #[test]
    fn selection_binds_location() {
        assert_ne!(
            snapshot("same", "native", "1:5", 1, 5),
            snapshot("same", "native", "6:10", 6, 10)
        );
        assert_ne!(
            snapshot("same", "automation", "1,2", 0, 0).fingerprint,
            snapshot("changed", "automation", "1,2", 0, 0).fingerprint
        )
    }
    #[test]
    fn reject_without_target() {
        let t = Target {
            id: "test".into(),
            window: 0,
            focus: 0,
            pid: 0,
            creation: 0,
            name: "fixture".into(),
            identity: None,
            selection: None,
        };
        assert!(
            !insert(
                &t,
                "text",
                false,
                0x54,
                &CancellationToken::new(),
                &Arc::new(Mutex::new(None)),
                0
            )
            .submitted()
        )
    }
    #[test]
    fn shortcut_scope_avoids_other_files_and_network() {
        let own = std::env::current_exe().unwrap();
        assert!(!same_local_executable(
            Path::new(r"\\server\share\TranslationFloat.exe"),
            &own
        ));
        assert!(!same_local_executable(Path::new("relative.exe"), &own));
        assert!(!same_local_executable(
            Path::new(r"C:\other\not-suiyi.exe"),
            &own
        ));
        assert!(same_local_executable(&own, &own));
    }
    #[test]
    fn shortcut_registration_survives_recording_and_save() {
        let count = Arc::new(std::sync::atomic::AtomicU32::new(0));
        let hits = count.clone();
        let key = Hotkey {
            modifiers: 7,
            virtual_key: 0x7b,
        };
        let (worker, registered) = HotkeyWorker::new(key, move || {
            hits.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        });
        if !registered {
            return;
        } // Never displace another application's registration.
        assert!(worker.suspend(true));
        assert!(worker.suspend(false));
        assert!(worker.suspend(false));
        assert!(worker.set(key));
        unsafe {
            PostThreadMessageW(worker.thread, 0x0312, 1, 0);
        }
        let start = Instant::now();
        while count.load(std::sync::atomic::Ordering::SeqCst) == 0
            && start.elapsed() < Duration::from_secs(1)
        {
            thread::sleep(Duration::from_millis(5));
        }
        assert_eq!(count.load(std::sync::atomic::Ordering::SeqCst), 1);
    }
}
