# 随译 Suiyi

一个面向聊天和日常写作的 Windows 翻译浮窗：唤出浮窗，输入中文并预览译文，再把译文填入原输入框。

当前已接入 DeepSeek，支持跨应用回填，不再限制进程名单。已实测 WPF 编辑框、Edge 网页多行及富文本输入框；此前也已在 Windows 微信 4.2.0.13 的文件传输助手中跑通完整翻译流程。

- 交互方案：紧凑浮窗，中文原文与译文上下排列。
- 默认快捷键：`Ctrl+Alt+T` 唤出，`Ctrl+Enter` 回填，`Esc` 收起并保留草稿。
- 技术方向：C#、WPF、.NET 10，优先支持 Windows 11 x64。
- 翻译服务：DeepSeek 官方 API，默认 `deepseek-flash`，关闭思考模式；支持设置为 `deepseek-v4-pro`。
- [完整技术方案](docs/translation-floating-window-plan.md)
- [实测记录和当前限制](docs/compatibility.md)

## 运行

运行 `artifacts\TranslationFloat\TranslationFloat.exe`，程序常驻托盘。

1. 首次使用，从托盘菜单或浮窗右上角打开“DeepSeek 设置”，填写 API Key，测试连接并保存。
2. 在准备填写的输入框中放好光标，按 `Ctrl+Alt+T`。
3. 在上方输入中文，停顿约 450 毫秒后自动翻译。下方的完整译文可以手动修改。
4. 按 `Ctrl+Enter` 或点击“填入原输入框”。有效译文按当前光标或选区的粘贴规则回填。
5. 在目标软件中检查文字，再自行决定是否发送或提交。程序仅执行粘贴。

`Esc` 收起并保留本次进程的原文和译文；中文选词时先取消组合输入。托盘菜单可以打开复制模式、设置或退出。回填会把译文留在剪贴板。不要在不准备替换的文字上保留选区。

支持范围是常见的可编辑文本区域，不能保证所有软件的每一个输入框。标准 Edit/RichEdit 检查原生控件状态；其他控件用隔离的 UI Automation 辅助进程识别，回填前复核具体控件身份。已识别的密码、只读、不可用和非输入控件禁止回填。

部分自绘应用只暴露整个窗口，程序无法区分其中的具体字段，会提示用户确认光标；这种路径只保留窗口与原生焦点检查。辅助识别不可用时也可能进入此路径，不保证识别所有自绘密码框。无法恢复目标、控件已变化或权限不足时请复制后手动粘贴。应用名称仅用于显示，不是白名单或兼容性承诺。

原文或模型配置改变后，旧译文立即失效，更新期间不能复制或回填。手动改译文不会被迟到的响应覆盖。多行原文使用一次结构化翻译请求，并校验行数与空行后重建换行；模型返回不完整或格式错误时提供重试。

翻译只发送浮窗中的原文，不采集聊天记录。请求超时为 15 秒；认证、余额、限流、网络和服务端错误分别提示。失败后不自动切换服务商，也不持续自动重试。

## 密钥保存

普通设置位于 `%LOCALAPPDATA%\TranslationFloat\settings.json`；密钥单独保存在同目录的 `deepseek.key`，使用 Windows DPAPI 当前用户范围加密。重新打开设置时，密钥框留空会保留已有密钥。

也可从程序启动环境中的 `DEEPSEEK_API_KEY` 读取密钥。环境变量只作为没有本机已保存密钥时的后备来源。运行包不包含用户配置或密钥。

API 地址固定为 `https://api.deepseek.com/chat/completions`，不将密钥发送到自定义代理地址。单行请求只返回译文，多行请求使用 JSON 行数组；接口依据 [DeepSeek 官方文档](https://api-docs.deepseek.com/api/create-chat-completion/)。

程序只运行一个实例；已经在托盘运行时，再次打开会直接退出新实例，请使用快捷键唤出原实例。

## 构建与检查

需要 Windows x64 和 .NET SDK 10.0.404。此工作区的 SDK 保存在 `.tools\dotnet`，没有修改系统 PATH。

```powershell
# 运行回填、翻译协议、调度、原生输入和密钥保存检查，生成自包含运行目录
.\scripts\build.ps1

# 启动到托盘
.\scripts\run.ps1

# 显示初始复制窗口；诊断模式会让浮窗出现在任务栏，便于 UI 测试
.\scripts\run.ps1 -Show -Diagnostics

# 打开 DeepSeek 设置
.\scripts\run.ps1 -Settings
```

自包含目录内的文件需要一起复制。可选 `build.ps1 -FrameworkDependent` 生成依赖已安装 .NET 10 Desktop Runtime 的版本，输出到单独目录。

## 自动发布 Windows 便携版

推送 `vX.Y.Z` 格式的 tag 后，[GitHub Actions](https://github.com/whoarei/suiyi/actions/workflows/release.yml) 自动运行全部检查、构建 Windows x64 自包含便携包，并发布到对应的 [Release](https://github.com/whoarei/suiyi/releases)。例如：

```powershell
git tag -a v0.1.0 -m "随译 v0.1.0"
git push origin v0.1.0
```

Release 包含 `suiyi-0.1.0-windows-x64-portable.zip` 和同名 `.sha256` 校验文件。下载后完整解压，运行 `Suiyi\TranslationFloat.exe`，无需另装 .NET。API Key 仍由使用者在本机设置，构建不需要 DeepSeek 凭据。

`v0.1.0-rc.1` 等带后缀的 tag 自动标为预发布。Actions 页面也支持手动运行，仅生成可下载的构建产物，不创建 Release。失败时可查看运行日志；重新发布已有版本前请先确认现有 Release，流程不会覆盖已有附件。

本地可以用同一个打包脚本生成版本化便携包，输出到 `artifacts\release`：

```powershell
.\scripts\package.ps1 -Version 0.1.0
```

自动检查共 68 项，不调用真实翻译服务，也不读取生产密钥；原生检查不向桌面发送输入。真实联调另使用用户在本机保存的凭据，验证 DeepSeek 翻译及跨应用回填。微信测试文字保留在文件传输助手的草稿框，未点击发送；浏览器测试页没有提交功能。

诊断日志位于 `%LOCALAPPDATA%\TranslationFloat\m0.log`，仅记录操作阶段、结果、进程名和窗口句柄，不记录输入文本或聊天内容。

UI Automation 辅助进程仅查询控件类型、启用/只读/密码状态和运行时身份，不读取控件名称、值或正文；每次最多等待 900 毫秒，超时终止辅助进程。
