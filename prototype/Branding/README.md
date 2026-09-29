# Whale Alive 图标

蓝色小鲸鱼最初在清透玻璃界面的 `GlassAppearance.CreateLogo` 中通过 WPF 几何路径绘制，没有引用外部图标文件。现在将相同路径提取到 `WhaleLogo.xaml`，作为标题栏、右键菜单及 Windows 图标的共同源。

更新矢量后执行：

```powershell
powershell.exe -NoProfile -STA -File tools/export-app-icons.ps1
dotnet build prototype/WhaleAlive.csproj -c Release
./tools/register-start-menu.ps1
```

- `WhaleAlive.ico`：16、20、24、32、40、48、64、128、256 像素，编入 EXE 和窗口资源。
- `../Assets/Brand/WhaleAlive-*.png`：透明方形 PNG，供 Windows 包身份图标和预览使用。
- 当前用户的开始菜单快捷方式直接引用正式 Release EXE 的图标，更新程序后继续使用。
- 身份包清单使用同一套 PNG；构建包时复制资源。图标更新不改变系统证书信任或通知权限。
