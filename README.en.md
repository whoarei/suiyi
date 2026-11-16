# Suiyi 随译

[简体中文](README.md) · **English**

<img src="src/TranslationFloat.App/Assets/Suiyi.png" alt="Suiyi icon" width="56" height="56">

**Select a sentence. Translate in place.**

Suiyi translates text in your Windows input fields. Enable Quick Mode, select text, and press `Ctrl+Alt+T`. The translation replaces your selection and keeps the surrounding text intact.

With no selection or usable input field, the same shortcut opens the translation window for manual input. Turn off Quick Mode to always preview translations first.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/quick-replace-dark.en.gif">
  <source media="(prefers-color-scheme: light)" srcset="docs/assets/quick-replace.en.gif">
  <img src="docs/assets/quick-replace.en.gif" alt="Quick Mode demo in the default orange theme: select Chinese text, press Ctrl+Alt+T, and replace it with English while keeping the greeting and signature" width="760">
</picture>

*Illustrated Quick Mode demo. Requires an editable input field that supports selection capture.*

## Features

- **Quick replacement**: translate and replace your selection with one shortcut.
- **Translation popup**: translate as you type, edit the result, then insert or copy it.
- **Across applications**: insert at the cursor or replace a selection in supported chat apps, browsers, and editors.
- **Flexible translation APIs**: use an OpenAI-compatible Chat Completions service, choose your model, and fetch available model IDs.
- **Languages**: automatic source detection and 12 target languages, switchable from the tray, with English as the default. The Chinese or English interface follows Windows automatically.
- **Ready when you are**: customizable shortcut, tray controls, and startup at login.
- **Color themes**: 10 accent colors, orange by default. Light and dark modes follow Windows, with matching window, taskbar, and tray icons.
- **Local encryption**: API keys are encrypted for your Windows user and stored separately for each API endpoint.
- **Install and update**: installer and portable builds, with in-app updates.
- **Lightweight runtime**: Tauri + Rust with an installer of about 3.7 MiB. Uses shared WebView2, installed automatically when missing.

[Download for Windows](https://github.com/whoarei/suiyi/releases/latest) · [User guide (Chinese)](docs/reference.md) · [Install and update (Chinese)](docs/installation.md) · [Compatibility (Chinese)](docs/compatibility.md) · [Technical design (Chinese)](docs/technical-design.md)
