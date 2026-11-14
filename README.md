# LOL国际服改中文语音工具

Windows 工具，通过 Riot 客户端为《英雄联盟》国际服切换大陆简体中文文字和英雄语音。切换流程已在台服（TW2）验证。

[下载 Windows 安装器](https://github.com/bfgd2jg/lol-international-chinese-voice-tool/releases/latest/download/LOL-Chinese-Voice-Tool-Setup.exe) · [便携 ZIP 与发行记录](https://github.com/bfgd2jg/lol-international-chinese-voice-tool/releases/latest)

## 使用

1. 确认电脑已通过 Riot 客户端安装国际服，并退出英雄联盟大厅或对局。
2. 运行安装器后，从开始菜单打开工具。也可以解压便携 ZIP，双击其中的 `LOL国际服改中文语音工具.exe`。点 **简中启动 LOL** 应用简中并打开 Riot 的英雄联盟页面。
3. Riot 的「游玩」可用后，工具会自动启动 LOL，并核对客户端启动语言。首次下载资源或 Riot 要求登录、更新时，按 Riot 提示处理即可。

如果 Riot 仍用原语言启动，请退出英雄联盟大厅和托盘里的 Riot 客户端，再用工具重试。工具会保存首次切换前的语言。要切回去时，退出英雄联盟大厅，点 **恢复原语言**，再到 Riot 手动点 **游玩**。

## 工具更新

工具启动时会从 GitHub 检查新版，自动下载并校验安装包。确认更新后，安装器会升级工具并重新打开它。顶部的下载图标可手动检查或安装已下载的版本。便携版确认更新后会安装到当前用户目录，以后从开始菜单打开。设置和原语言备份沿用已有数据。

## 设置

- 首次打开跟随 Windows 的深浅色设置。点顶部的日月图标手动切换并保存偏好，右键可恢复跟随系统。
- 点右上角语言图标切换启动器的中英文界面。中文显示「文」，英文显示「A」。游戏语言的切换目标为大陆简体中文。
- 游戏路径默认自动识别。点安装位置右侧的文件夹图标选择 `LeagueClient.exe` 后，工具会保存并使用该路径。右键点文件夹可恢复自动识别。
- 自动等待语言资源，确认客户端使用目标语言后完成。可随时点 **停止等待**，或退出工具。

设置会保存在当前 Windows 用户目录。

![启动器深色界面](docs/settings-dark.png)

## 编译与验证

适用 Windows 10 / 11，使用系统的 .NET Framework 编译器和 WPF。

```powershell
.\build.ps1
.\build.ps1 -Verify
.\build.ps1 -InstallerCompiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

可执行文件输出到 `artifacts/LOL国际服改中文语音工具.exe`。`-Verify` 在临时测试数据中验证安装发现、语言字段切换与恢复、旧设置读取、设置持久化，并渲染界面到 `artifacts/qa`。

Windows 安装器使用 Inno Setup 6.7.4 构建。发行时将 `artifacts/installer` 中的 `LOL-Chinese-Voice-Tool-Setup.exe` 和 `update.json` 上传到同一个版本标签下，保留这两个文件名。

启动器从 Riot 的本地安装信息发现客户端，只在切换时修改 `settings.locale`，由 Riot 下载资源并启动游戏。启动器通过本机日志检查更新进度和实际启动语言。原语言和界面设置保存在 `%LOCALAPPDATA%\LeagueChineseLocaleLauncher`。

本项目采用 [MIT 许可证](LICENSE)。这是社区工具，发行页提供安装器和便携 ZIP。
