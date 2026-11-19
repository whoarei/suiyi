# 随译 Android

## 翻译和回填

- **在原应用划词**。长按选中文字，从划词菜单选择「随译」。菜单折叠时，先点「更多」。浮窗默认显示可编辑的译文，点「原文」可展开对照。可编辑选区可通过「替换」图标回填，只读选区可复制译文。
- **直接替换**。在设置开启「直接替换可编辑选区」并保存。随译翻译成功后把译文交回原应用，翻译失败或切走时不回传译文。原应用负责应用返回结果并决定划词菜单提供哪些操作。
- **分享或粘贴**。如果划词菜单没有「随译」，可从其他应用分享纯文本到随译，或在主界面手动输入、粘贴，再点「翻译」。译文可以编辑和复制。

首次启动启用本地演示。原文为空时，点「试译」可演示「你好，世界。」的翻译。也可在其他应用选择「你好」、`Hello`、`Hello, world.` 或「你好，世界。」试用划词。

## 配置翻译服务

在设置关闭「使用本地演示」，填入 OpenAI 兼容的 HTTPS 地址（例如 `https://example.com/v1`）、所需的 API Key 和模型。「获取模型」可列出服务返回的模型，也可以直接手填。「测试连接」发送固定的单行和多行样例，验证两个翻译方向与换行格式。保存后应用到主界面和划词。

主要语言和常用外语可以从英语、简体中文、繁体中文、日语、韩语、法语、德语、西班牙语、葡萄牙语、俄语、意大利语、阿拉伯语中选择。与主要语言相同的原文译成常用外语，其他原文译成主要语言。上限为 2000 个 UTF-16 代码单元。

翻译请求仅向所配置的服务发送本次原文，应用不保存翻译历史。API Key 使用 Android Keystore 加密，留空保留同一地址的 Key，改地址时不会将原有 Key 发到新地址。界面跟随系统深浅模式，中文系统显示中文，其他系统显示英文。

## 应用内更新

从 Android 1.0.3 起，可在设置底部点「检查更新」。发现新版后选择「下载并安装」，随译从 GitHub 正式 Release 下载并校验 APK。系统若要求授权，请允许随译安装来自此来源的应用，返回后在系统界面确认安装。使用相同签名覆盖安装会保留已保存的应用设置。

从 1.0.2 或更早版本首次升级时，请从 [GitHub 发布页](https://github.com/whoarei/suiyi/releases/latest) 下载 APK 手动安装。

## 构建和安装

需要 JDK 17 或 21、Android SDK 36 与 Build Tools 36.0.0。以下命令在仓库的 `android` 目录执行。

```powershell
$env:ANDROID_HOME = 'E:\Android\SDK'
.\gradlew.bat :app:assembleDebug :test-host:assembleDebug
```

正式 APK 使用独立的发布签名。首次创建本机的随译签名密钥，后续构建复用同一份密钥。

```powershell
.\package.ps1 -InitializeLocalKey
# 后续构建
.\package.ps1
```

正式 APK 与 SHA-256 校验文件输出到仓库的 `artifacts/android`。签名密钥与当前 Windows 用户加密的密码文件保存在 `%LOCALAPPDATA%\Suiyi\AndroidSigning`。Android 更新依赖原签名，发布前请保管原密钥及可在其他机器恢复的密码备份。旧试用 APK 使用调试签名，安装正式版时 Android 会将它判定为不同签名。

## GitHub Actions 签名与发布

在仓库 [Settings → Secrets and variables → Actions](https://github.com/whoarei/suiyi/settings/secrets/actions) 添加以下配置。

| 名称 | 类型 | 值 |
| --- | --- | --- |
| `ANDROID_KEYSTORE_BASE64` | 必填 Secret | 完整签名密钥库文件的 Base64 文本 |
| `ANDROID_KEYSTORE_PASSWORD` | 必填 Secret | 密钥库密码 |
| `ANDROID_KEY_ALIAS` | 可选 Secret 或 Variable | 私钥别名，优先读取 Secret，默认 `suiyi-android` |
| `ANDROID_KEY_PASSWORD` | 可选 Secret | 私钥使用独立密码时填写，默认使用密钥库密码 |

要让 CI 生成的 APK 能覆盖安装现有正式版，请使用同一个签名密钥库。沿用本机密钥时，可以在创建它的 Windows 用户下运行以下命令，直接通过 GitHub CLI 写入两个 Secret。

```powershell
$signingDir = Join-Path $env:LOCALAPPDATA 'Suiyi/AndroidSigning'
$keystore = Join-Path $signingDir 'suiyi-release.p12'
[Convert]::ToBase64String([IO.File]::ReadAllBytes($keystore)) |
    gh secret set ANDROID_KEYSTORE_BASE64 --repo whoarei/suiyi

$encrypted = (Get-Content -LiteralPath (Join-Path $signingDir 'password.dpapi') -Raw).Trim()
$credential = [pscredential]::new('suiyi-android', (ConvertTo-SecureString $encrypted))
$credential.GetNetworkCredential().Password |
    gh secret set ANDROID_KEYSTORE_PASSWORD --repo whoarei/suiyi
```

手动运行 [Windows and Android release](https://github.com/whoarei/suiyi/actions/workflows/release.yml)，可从运行记录下载 `android` 构建产物。推送 `vX.Y.Z` 或预发布标签时，Windows 和 Android 构建均通过后，GitHub Release 会附上两端产物。Android APK 文件名和可升级版本分别由 `android/app/build.gradle` 里的 `versionName` 和 `versionCode` 决定。

## 自动化测试

测试应用提供原生输入框、只读文本和 WebView 输入框。运行测试需选定可清理的测试设备，测试会修改随译设置和剪贴板。普通真机试用可直接安装正式 APK。

Android 16 的 HyperOS 真机已手动验证原生输入框原位替换与小米便签分享翻译。该机上的便签可以显示随译预览，点「替换」后正文保持原样，可用「复制译文」再粘贴。系统也可能要求确认测试应用打开随译，可按提示选择「本次允许」。

```powershell
$env:ANDROID_SERIAL = 'emulator-5562'
.\gradlew.bat :app:lintDebug :app:installDebug
.\gradlew.bat :test-host:connectedDebugAndroidTest
```

要额外验证模型获取、连接测试、API 回填、服务失败和迟到响应，在仓库根目录运行 `node tools/mock-translation-server.mjs`。在另一个终端进入 `android` 目录并执行以下命令，将模拟器序列号替换为实际值。调试版允许通过 ADB 反向端口访问本地测试接口。

```powershell
$env:ANDROID_SERIAL = 'emulator-5562'
& "$env:ANDROID_HOME/platform-tools/adb.exe" reverse tcp:18765 tcp:18765
.\gradlew.bat :app:installDebug
.\gradlew.bat '-Pandroid.testInstrumentationRunnerArguments.fixtureEndpoint=http://127.0.0.1:18765/v1' :test-host:connectedDebugAndroidTest
```
