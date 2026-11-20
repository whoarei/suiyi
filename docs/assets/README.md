# 随译图标

桌面采用 Android 启动图标的橙底白字「文 / A」，底板使用圆角以适配 Windows 图标样式。窗口、任务栏和托盘使用同一个图形，切换主题时按主题色重新着色。

![应用与托盘图标，在浅色和深色背景下的预览](suiyi-icons.png)

源稿是 Android 的 [`ic_suiyi.xml`](../../android/app/src/main/res/drawable/ic_suiyi.xml)。以下文件生成到 `src/TranslationFloat.App/Assets`。

- `Suiyi.svg` 由 Android 源稿转换而来。
- `Suiyi.png` 是用于品牌预览的 512 像素透明 PNG。
- `Suiyi.Mask.png` 是运行时换色用的 512 像素遮罩。
- `Suiyi.ico` 包含 16、20、24、32、40、48、64、96、128、256 像素的程序与窗口图标。
- `Suiyi.Tray.ico` 包含 16、20、24、32、40、48 像素的托盘图标。

在 Windows 下运行以下命令重新生成。

```powershell
powershell.exe -NoProfile -STA -File scripts/generate-icons.ps1
```

脚本读取 Android 矢量源稿，使用 Windows 自带的 WPF 渲染，并用 Windows 图像解码器校验 ICO 帧。生成文件随源码保存，普通构建直接使用。

默认橙色 ICO 写入可执行文件和安装包。Tauri 运行时按主题配色从遮罩生成多尺寸 ICO，供窗口、任务栏和托盘使用。

任务栏通过窗口图标和 AppUserModel 图标资源同步。图标文件缓存在 `%LOCALAPPDATA%\TranslationFloat\Icons`；仅刷新目标路径为当前随译程序的开始菜单和任务栏快捷方式，不修改其他应用的快捷方式。

## 快速替换动图

首页动图使用默认橙色主题，展示选中中文、按下快捷键、原位替换为英文的快速模式体验，前后的称呼与署名保持不变。此为绘制的流程示意，包含 108 帧，循环时长 7.2 秒。

- `quick-replace.gif`、`quick-replace-dark.gif`：中文标注的浅色与深色版本。
- `quick-replace.en.gif`、`quick-replace-dark.en.gif`：英文标注的浅色与深色版本。

中英文 README 通过 `<picture>` 按阅读者的深浅偏好选择对应动图。

图形使用 WPF 绘制，以 FFmpeg 编码为 GIF。重新生成需要 Windows 和可用的 `ffmpeg` 命令：

```powershell
powershell.exe -NoProfile -STA -File scripts/generate-readme-demo.ps1
powershell.exe -NoProfile -STA -File scripts/generate-readme-demo.ps1 -Mode Dark
powershell.exe -NoProfile -STA -File scripts/generate-readme-demo.ps1 -Language en
powershell.exe -NoProfile -STA -File scripts/generate-readme-demo.ps1 -Language en -Mode Dark
```
