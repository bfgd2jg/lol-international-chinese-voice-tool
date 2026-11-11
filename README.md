# LOL国际服改中文语音工具

Windows 便携工具，通过 Riot 客户端为《英雄联盟》国际服切换大陆简体中文文字和英雄语音。切换流程已在台服（TW2）验证。

[下载 Windows 版](https://github.com/bfgd2jg/lol-chinese-launcher/releases/latest)

## 使用

1. 确认电脑已通过 Riot 客户端安装国际服，并退出英雄联盟大厅或对局。
2. 解压发行版 ZIP，双击 `LOL国际服改中文语音工具.exe`，点 **切换为简中**。
3. Riot 打开后，进入英雄联盟页面。首次按提示点 **更新** 下载语言资源，然后手动点 **游玩**。工具会显示资源进度，并核对客户端启动语言。

如果 Riot 仍用原语言启动，请退出英雄联盟大厅和托盘里的 Riot 客户端，再用工具重试。工具会保存首次切换前的语言。要切回去时，退出英雄联盟大厅，点 **恢复原语言**，再到 Riot 手动点 **游玩**。

## 设置

- 首次打开跟随 Windows 的深浅色设置。点顶部的日月图标手动切换并保存偏好，右键可恢复跟随系统。
- 启动器界面可选简体中文或 English。游戏语言的切换目标为大陆简体中文。
- 游戏路径默认自动识别。若识别失败，点 **手动选择**，选择 Riot 国际服安装目录里的 `LeagueClient.exe`。
- 自动等待语言资源，确认客户端使用目标语言后完成。可随时点 **停止等待**，或退出工具。

设置会保存在当前 Windows 用户目录。点顶部齿轮打开设置，点返回箭头回到首页。

![启动器深色设置页](docs/settings-dark.png)

## English quick start

Extract the Windows release and run `LOL国际服改中文语音工具.exe`. Use the gear icon to open **Settings** and choose **English**. Choose **Switch to Chinese** to open Riot and apply Simplified Chinese. Open League’s page, click **Update** if needed, then click **Play**. The tool verifies League’s startup language.

The tool follows Windows on first launch. Click the sun or moon icon to save a light or dark preference, or right-click it to follow Windows again. The arrow returns to the home screen.

If automatic discovery fails, choose `LeagueClient.exe` in **Settings → Game installation → Choose manually**. Use the international League installation managed by Riot. Use **Restore language** to return to the game language saved on first switch.

## 编译与验证

适用 Windows 10 / 11，使用系统的 .NET Framework 编译器和 WPF。

```powershell
.\build.ps1
.\build.ps1 -Verify
```

可执行文件输出到 `artifacts/LOL国际服改中文语音工具.exe`。`-Verify` 在临时测试数据中验证安装发现、语言字段切换与恢复、旧设置读取、设置持久化，并渲染界面到 `artifacts/qa`。

启动器从 Riot 的本地安装信息发现客户端，只在切换时修改 `settings.locale`，由 Riot 下载资源并启动游戏。启动器通过本机日志检查更新进度和实际启动语言。原语言和界面设置保存在 `%LOCALAPPDATA%\LeagueChineseLocaleLauncher`。

本项目采用 [MIT 许可证](LICENSE)。这是社区工具，发行包包含启动器、说明和许可证。
