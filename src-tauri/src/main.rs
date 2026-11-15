#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
mod api;
mod app;
mod ffi;
mod i18n;
mod language;
mod logging;
mod native;
#[cfg(test)]
mod network_tests;
mod scheduler;
mod store;
mod theme;
mod update;
fn main() {
    app::run()
}
