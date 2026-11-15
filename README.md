# 随译 Suiyi

**简体中文** · [English](README.en.md)

<img src="src/TranslationFloat.App/Assets/Suiyi.png" alt="随译图标" width="56" height="56">

**选中一句，译文就位。**

随译是一款 Windows 翻译工具。开启快速模式，选中文字，按下 `Ctrl+Alt+T`，译文直接替换原选区，保留前后内容。

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/quick-replace-dark.gif">
  <source media="(prefers-color-scheme: light)" srcset="docs/assets/quick-replace.gif">
  <img src="docs/assets/quick-replace.gif" alt="默认橙色主题下的快速模式示意：选中中文，按 Ctrl+Alt+T，译文原位替换，称呼和署名保持不变" width="760">
</picture>

*快速模式体验示意，适用于支持选区读取的可编辑输入框。*

## 功能

- **快速替换**：选中、按快捷键、原位换成译文，全程不弹浮窗。
- **浮窗预览**：输入后自动翻译，可修改译文，再回填或复制。
- **跨应用回填**：在支持的聊天、网页和编辑框中使用，替换选区或填入光标位置。
- **通用翻译接口**：连接 OpenAI 兼容服务，自选模型，支持获取模型列表。
- **顺手常驻**：自定义快捷键、托盘切换、开机启动。
- **主题配色**：10 种颜色可选，默认橙色，浅色与深色跟随系统，窗口、任务栏和托盘图标同步换色。
- **本机加密**：密钥按 API 地址分别保存，使用当前 Windows 用户加密。
- **安装与更新**：提供安装包和便携版，支持程序内下载并安装更新。

[下载 Windows 版](https://github.com/whoarei/suiyi/releases/latest) · [使用与配置](docs/reference.md) · [安装与升级](docs/installation.md) · [支持范围](docs/compatibility.md) · [技术方案](docs/technical-design.md)
