# 随译图标

玉绿色圆角方形与两条反向流动的箭头，表达原文与译文之间的转换。大尺寸使用轻微渐变与浅薄荷色；托盘和小尺寸应用图标使用同形的纯色版本，并按 16 像素网格加粗符号。

![应用与托盘图标，在浅色和深色背景下的预览](suiyi-icons.png)

源稿位于 `src/TranslationFloat.App/Assets`：

- `Suiyi.svg`：程序图标的可编辑矢量源稿。
- `Suiyi.Tray.svg`：为小尺寸绘制的矢量源稿。
- `Suiyi.png`：512 像素透明 PNG，用于浮窗标题区。
- `Suiyi.ico`：程序与窗口图标，包含 16、20、24、32、40、48、64、96、128、256 像素帧。
- `Suiyi.Tray.ico`：托盘图标，包含 16、20、24、32、40、48 像素帧。

在 Windows 下重新生成：

```powershell
powershell.exe -NoProfile -STA -File scripts/generate-icons.ps1
```

脚本读取 SVG 源稿，使用 Windows 自带的 WPF 渲染，不依赖字体、外部服务或下载的图标库；同时用 Windows 图像解码器校验 ICO 帧。生成的 ICO 和 PNG 直接入库，普通构建无需重新绘图。

应用 ICO 写入可执行文件并用作 WPF 窗口图标；托盘 ICO 作为独立资源嵌入程序集，按系统小图标尺寸选帧，并在退出时释放。便携包无需附带松散的图标文件。
