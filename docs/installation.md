# 安装与升级

推荐从 [Release](https://github.com/whoarei/suiyi/releases) 下载 `suiyi-X.Y.Z-windows-x64-setup.exe`。安装器采用简体中文，默认安装到 `%LOCALAPPDATA%\Programs\Suiyi`，提供开始菜单和 Windows 已安装应用中的卸载入口，不需要管理员权限。

## 从便携版迁移

v0.2.0 及更早版本先从托盘退出旧版，运行新版安装包。API 地址、模型、按地址加密的密钥及快捷键等偏好保持在原用户目录和注册表中。安装后使用开始菜单的“随译”；开启的启动项会在首次运行时更新到安装位置。

便携版以后使用程序内安装更新时也会安装到这一位置。旧便携目录可在退出旧实例后自行整理，不再从旧目录启动。

## 程序内更新

右键托盘图标选择“检查更新”。发现新版时，点击“下载并安装”。程序从对应正式 Release 获取 Windows x64 安装附件，验证大小和 GitHub API 提供的 SHA-256 后才运行安装器；下载只允许 HTTPS 的官方仓库与 GitHub 附件 CDN，不携带翻译凭据。

下载时可以关闭窗口取消。网络、文件和摘要错误会显示重试入口。下载完成时若正在操作或设置仍打开，先完成操作并关闭设置，再点击“安装更新”。安装器正常关闭随译，覆盖升级后重新运行，整个过程不要求手动解压或替换文件。

没有有效安装包摘要的旧 Release 保留发布页面下载入口。程序不会自动定时检查，也不会在未点击更新时下载安装包。

## 卸载

在 Windows 已安装应用中卸载“随译”。安装器关闭运行中的随译并删除安装的程序文件，移除指向该安装位置的开机启动项，保留 `%LOCALAPPDATA%\TranslationFloat` 中的配置、加密密钥及用户偏好。

## 构建

`scripts/package.ps1 -Version X.Y.Z` 同时构建安装包、便携包和 SHA-256 文件。编译器固定为 [Inno Setup 6.7.3](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3)，在下载后校验 SHA-256，并采用其[官方便携模式](https://jrsoftware.org/inno-setup/documentation/topic_technotes.htm)。编译器和同版本中文语言文件仅存放在 `.tools`，不会进入随译安装包。

`scripts/test-installer.ps1 -AppDirectory <自包含目录>` 使用隔离身份验证首次安装、事件通知关闭持锁进程后的覆盖升级、卸载及非托管文件保留。日志和测试附件保存在 `artifacts`；不读取真实密钥、不修改真实随译启动偏好。
