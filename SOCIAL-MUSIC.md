# 消息与音乐

启动后只显示桌宠。右键桌宠打开功能菜单，可直接查看音乐封面、歌名、歌手，并操作上一首、播放/暂停和下一首；待办事项支持展开、输入后回车添加、勾选完成。菜单还提供安静陪伴、停止动作和退出。

点击菜单「设置」才显示设置窗口，其中「消息与音乐」管理详细选项。关闭设置窗口只隐藏窗口，桌宠继续运行；退出请使用右键菜单「退出桌宠」。

## 消息

点击「开启通知访问」后，由 Windows 请求权限。默认仅显示「微信收到一条新消息」，可改为发信人或完整预览；微信、QQ 和其他系统通知可以分别过滤。连续通知合并，每次提示约 6 秒。开启之前的旧通知不会补播。

仅提示模式不提取通知标题和正文。切换隐私模式或关闭提醒会立即清除当前消息与待显示消息。消息只留在内存，不写入日志或设置，也不通过 DSH/MCP 暴露。

通知来源是 Windows 通知中心。微信/QQ 没有投递到通知中心的自绘弹窗、关闭的提醒，以及原软件隐藏的内容无法读取。本功能不读取聊天数据库或登录凭据。

## 音乐

默认只自动选择 QQ 音乐，不会控制浏览器里碰巧正在播放的音频。也可手动选择系统发现的其他播放器。播放器必须提供 Windows 媒体会话；播放一首歌后通常才会出现。

封面通过播放器发布的系统媒体缩略图读取；未提供封面时显示音符，切歌和断开连接时清除旧封面。封面只保存在内存。

支持系统媒体接口的上一首、下一首、播放/暂停、顺序、列表循环、单曲循环和随机模式。每个按钮按播放器报告的能力启用，不支持的按钮置灰。

开启「桌宠气泡显示歌名」后，悬停气泡出现播放按钮。消息优先展示，结束后恢复歌名。歌词导入、同步和时间校准功能已移除；旧配置里的歌词字段不再读取或使用。

## 本机通知组件安装

项目使用 Windows 包身份申请通知权限，二进制仍保留在原来的 Release 目录。安装包仅包含身份与能力声明。

```powershell
dotnet build prototype/WhaleAlive.csproj -c Release
./tools/build-notification-package.ps1
./tools/register-notifications.ps1 -TrustLocalCertificate
```

构建脚本创建或复用 `CN=WhaleAlive.Local` 本地代码签名证书，私钥不可导出，保存在当前用户证书库；输出仅含公钥 `.cer` 和已签名 `.msix`。注册脚本只有明确传入 `-TrustLocalCertificate` 才导入当前用户的受信任人，不修改开发者模式或通知权限。

部分 Windows 安装服务要求计算机级信任。此时需用户明确授权管理员将同一证书加入 `LocalMachine/TrustedPeople`；`tools/trust-notification-certificate.ps1` 会检查已批准的指纹，不加入受信任根。随后以原用户运行注册脚本，并重启桌宠。通知读取权限仍须用户在 Windows 提示中开启。

这是本地开发安装方式，正式分发需使用受信任的发布证书。

## 验证

`WhaleAlive.exe --verify-social-music` 检查消息隐私、通知去重、气泡优先级、设置持久化、封面与曲目切换、菜单待办、设置隐藏与重新打开；运行前退出正常桌宠以释放桥接管道。静音媒体夹具只控制自己的媒体会话，不会向联系人发消息，也不会改变其他播放器。

参考：[Windows 通知监听](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener)、[媒体会话控制](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession)、[包身份安装](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)。
