#![allow(non_snake_case, dead_code)]
use std::ffi::c_void;
pub type Hwnd = isize;
#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct Point {
    pub x: i32,
    pub y: i32,
}
#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct Rect {
    pub left: i32,
    pub top: i32,
    pub right: i32,
    pub bottom: i32,
}
#[repr(C)]
#[derive(Default)]
pub struct GuiInfo {
    pub size: u32,
    pub flags: u32,
    pub active: Hwnd,
    pub focus: Hwnd,
    pub capture: Hwnd,
    pub menu: Hwnd,
    pub movesize: Hwnd,
    pub caret: Hwnd,
    pub rect: Rect,
}
#[repr(C)]
#[derive(Default)]
pub struct MonitorInfo {
    pub size: u32,
    pub monitor: Rect,
    pub work: Rect,
    pub flags: u32,
}
#[repr(C)]
#[derive(Clone, Copy)]
pub struct Keyboard {
    pub key: u16,
    pub scan: u16,
    pub flags: u32,
    pub time: u32,
    pub extra: usize,
}
#[repr(C)]
#[derive(Clone, Copy)]
pub union InputData {
    pub keyboard: Keyboard,
    pub mouse: [usize; 4],
}
#[repr(C)]
#[derive(Clone, Copy)]
pub struct Input {
    pub kind: u32,
    pub data: InputData,
}
#[repr(C)]
#[derive(Default)]
pub struct Msg {
    pub hwnd: Hwnd,
    pub message: u32,
    pub wparam: usize,
    pub lparam: isize,
    pub time: u32,
    pub point: Point,
    pub private: u32,
}
#[repr(C)]
#[derive(Default)]
pub struct Blob {
    pub len: u32,
    pub data: *mut u8,
}
#[link(name = "user32")]
unsafe extern "system" {
    pub fn MessageBoxW(hwnd: Hwnd, text: *const u16, caption: *const u16, kind: u32) -> i32;
    pub fn GetForegroundWindow() -> Hwnd;
    pub fn GetAncestor(hwnd: Hwnd, flag: u32) -> Hwnd;
    pub fn IsWindow(hwnd: Hwnd) -> i32;
    pub fn IsWindowEnabled(hwnd: Hwnd) -> i32;
    pub fn GetWindowThreadProcessId(hwnd: Hwnd, pid: *mut u32) -> u32;
    pub fn GetGUIThreadInfo(thread: u32, info: *mut GuiInfo) -> i32;
    pub fn GetClassNameW(hwnd: Hwnd, text: *mut u16, count: i32) -> i32;
    pub fn GetWindowLongPtrW(hwnd: Hwnd, index: i32) -> isize;
    pub fn SetWindowLongPtrW(hwnd: Hwnd, index: i32, value: isize) -> isize;
    pub fn GetAsyncKeyState(key: i32) -> i16;
    pub fn SetForegroundWindow(hwnd: Hwnd) -> i32;
    pub fn IsIconic(hwnd: Hwnd) -> i32;
    pub fn ShowWindowAsync(hwnd: Hwnd, command: i32) -> i32;
    pub fn SendInput(count: u32, inputs: *const Input, size: i32) -> u32;
    pub fn SendMessageTimeoutW(
        hwnd: Hwnd,
        msg: u32,
        wparam: usize,
        lparam: isize,
        flags: u32,
        timeout: u32,
        result: *mut usize,
    ) -> isize;
    pub fn OpenClipboard(hwnd: Hwnd) -> i32;
    pub fn CloseClipboard() -> i32;
    pub fn EmptyClipboard() -> i32;
    pub fn GetClipboardData(format: u32) -> isize;
    pub fn SetClipboardData(format: u32, handle: isize) -> isize;
    pub fn GetClipboardOwner() -> Hwnd;
    pub fn GetClipboardSequenceNumber() -> u32;
    pub fn GetCursorPos(point: *mut Point) -> i32;
    pub fn MonitorFromPoint(point: Point, flags: u32) -> isize;
    pub fn GetMonitorInfoW(monitor: isize, info: *mut MonitorInfo) -> i32;
    pub fn GetDpiForWindow(hwnd: Hwnd) -> u32;
    pub fn RegisterHotKey(hwnd: Hwnd, id: i32, modifiers: u32, key: u32) -> i32;
    pub fn UnregisterHotKey(hwnd: Hwnd, id: i32) -> i32;
    pub fn GetMessageW(msg: *mut Msg, hwnd: Hwnd, min: u32, max: u32) -> i32;
    pub fn PeekMessageW(msg: *mut Msg, hwnd: Hwnd, min: u32, max: u32, remove: u32) -> i32;
    pub fn PostThreadMessageW(thread: u32, msg: u32, wparam: usize, lparam: isize) -> i32;
}
#[link(name = "kernel32")]
unsafe extern "system" {
    pub fn GetLastError() -> u32;
    pub fn GetCurrentThreadId() -> u32;
    pub fn CloseHandle(handle: isize) -> i32;
    pub fn OpenProcess(access: u32, inherit: i32, pid: u32) -> isize;
    pub fn GetProcessTimes(
        process: isize,
        creation: *mut u64,
        exit: *mut u64,
        kernel: *mut u64,
        user: *mut u64,
    ) -> i32;
    pub fn QueryFullProcessImageNameW(
        process: isize,
        flags: u32,
        text: *mut u16,
        size: *mut u32,
    ) -> i32;
    pub fn GlobalAlloc(flags: u32, size: usize) -> isize;
    pub fn GlobalFree(handle: isize) -> isize;
    pub fn GlobalLock(handle: isize) -> *mut c_void;
    pub fn GlobalUnlock(handle: isize) -> i32;
    pub fn GlobalSize(handle: isize) -> usize;
    pub fn LocalFree(handle: isize) -> isize;
    pub fn CreateMutexW(attributes: *const c_void, owner: i32, name: *const u16) -> isize;
    pub fn CreateEventW(
        attributes: *const c_void,
        manual: i32,
        initial: i32,
        name: *const u16,
    ) -> isize;
    pub fn WaitForSingleObject(handle: isize, milliseconds: u32) -> u32;
}
#[link(name = "crypt32")]
unsafe extern "system" {
    pub fn CryptProtectData(
        input: *const Blob,
        description: *const u16,
        entropy: *const Blob,
        reserved: *const c_void,
        prompt: *const c_void,
        flags: u32,
        output: *mut Blob,
    ) -> i32;
    pub fn CryptUnprotectData(
        input: *const Blob,
        description: *mut *mut u16,
        entropy: *const Blob,
        reserved: *const c_void,
        prompt: *const c_void,
        flags: u32,
        output: *mut Blob,
    ) -> i32;
}
pub fn wide(value: &str) -> Vec<u16> {
    value.encode_utf16().chain(Some(0)).collect()
}
pub fn key(key: u16, up: bool) -> Input {
    Input {
        kind: 1,
        data: InputData {
            keyboard: Keyboard {
                key,
                scan: 0,
                flags: if up { 2 } else { 0 },
                time: 0,
                extra: 0,
            },
        },
    }
}
pub fn chord(keycode: u16) -> [Input; 4] {
    [
        key(0x11, false),
        key(keycode, false),
        key(keycode, true),
        key(0x11, true),
    ]
}
