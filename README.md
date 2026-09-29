# Whale Alive / DSH Interactive Pet

一个已经可以运行的 Windows 桌宠原型：鲸鱼娘走到真实桌面图标旁，抱着图标副本走过去，在放下时修改真实位置，并支持撤销。

## 启动

安装 [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) 后双击 `Start-WhaleAlive.cmd`。源码仓库不附带编译产物；脚本会在首次启动时自动构建，之后也可以直接打开生成的 `prototype/bin/Release/net9.0-windows/WhaleAlive.exe`。

启动后只显示桌宠。右键桌宠打开功能菜单，直接查看 QQ 音乐封面、歌名、播放控制，或展开待办并添加/完成事项；点「设置」才打开主窗口。关闭设置会继续陪伴，退出请用菜单「退出桌宠」。

左键单击桌宠查看 DSH 当前任务阶段，气泡显示约 6 秒，期间自动跟随状态变化。按住约 300 毫秒才会拎起，松手放下。DSH 尚未连接或超过 8 秒未收到心跳时会明确提示。现有接口仅提供思考、执行、等待确认、完成、异常等阶段，没有具体步骤总数或百分比。

待办气泡最多四条，沿桌宠左侧弧形排列，靠屏幕边缘时避让以保持可见；气泡和右键菜单只显示待办内容，点击完成后由下一条补位。创建时间、完成时间及状态集中在设置窗口的“待办”页，可筛选全部、未完成和已完成记录，时间按本地时区显示。旧版本已完成事项没有完成时间时显示“未记录”；新完成的事项会保存准确时间。

右键菜单直接展示音乐卡片与待办，设置默认显示自主设置；范围与操作权限收在「更多行为与权限」。也可用 `WhaleAlive.exe --settings` 启动并显示设置。

待机时会随机选择有足够空间的普通窗口，在上边缘随机选一点坐下或趴睡，无需先选择窗口或露出桌面图标。角色会走到落点，坐一会儿后自行起身；睡觉会先打瞌睡、打哈欠，再走到窗沿趴下。窗沿位置跟随窗口移动和缩放；最大化、最小化、关闭、边缘空间不足、DSH 开始工作或拎起桌宠时结束休息。关闭“允许打盹”后不会触发趴睡；关闭随机待机动作后也不会触发窗沿休息。

“设置 → 自主设置”可分别选择坐下时长与睡觉时长，图标和窗口边沿共用。默认坐 2 分钟、睡 5 分钟，可选 30 秒至 60 分钟；新时长在下一次坐稳或趴好后开始计时。拎起、停止、开始工作或目标消失仍可提前结束，不必等待计时结束。

当前采用 **.NET 9 WPF + Windows Shell COM**，已接入 **23 组动画、1,317 帧**。走路、抱着走路和跑步支持起步、循环、停步；待机时会找可见桌面图标坐下摆腿。动作通过本地桥接可逐个试播。主页包含桌面图标列表与待办、文件、整理、自主设置、消息与音乐五个分页；已通过配置热加载接入当前 DSH 状态与 15 个 MCP 工具。详见 [成品动画说明](FINISHED-ANIMATIONS.md)。

1. 在左侧选择桌面图标，Ctrl 可多选；右侧切换待办、文件、整理、自主设置和消息与音乐。
2. 「自主设置 → 手动操作权限」开启本次真实图标或鼠标权限。
3. 「自主设置」可以开启自由搬运、图案彩蛋和自动归位，并恢复最近一次或全部自主搬运。
4. 可拖拽桌宠；右键菜单中的「设置」打开主页。双 Esc 或「停止动作」中断当前动作。
5. 日常音乐和待办在右键菜单直接操作。

真实移动只改变图标在桌面上的位置，不会改变文件路径。预演保留原图标并显示随角色移动的视觉副本。权限每次启动重置。自动排列开启时拒绝真实移动；不会自行更改 Windows 桌面设置。若启用了对齐网格，最终坐标可能被 Windows 吸附。

## DSH 调用

### 新电脑首次接入

当前接入面向 **Windows 上从源码启动的 DSH Web**。准备好 DSH 源码、Node.js 22.19+、pnpm，以及 .NET 9 SDK（下载源码版 Whale Alive 时用于首次构建），然后在 Whale Alive 目录运行：

```powershell
.\Connect-DSH.cmd -DshRoot "你的路径\DeepSeek-Harness"
```

脚本会自动安装 Whale Alive 的 MCP 依赖、构建并启动桌宠、按本机实际路径生成 DSH overlay，最后用该 overlay 启动 DSH。移动 Whale Alive 文件夹后重新运行一次即可。以后需要同时使用 DSH 和桌宠时仍从 `Connect-DSH.cmd` 启动。若只想启动桌宠，双击 `Start-WhaleAlive.cmd`。

进入 DSH 后应能发现带 `mcp__whale_alive__` 前缀的 15 个工具。连接健康状态写入本项目的 `artifacts/dsh-connection.json`，其中 `ready` 为 `true` 且 `registered` 为 15 项即表示接入完成。真实移动图标、移动窗口和鼠标互动仍需用户在桌宠设置中手动开启权限，每次启动重新确认。

只生成 overlay、暂不启动 DSH 时可执行：

```powershell
.\Connect-DSH.cmd -DshRoot "你的路径\DeepSeek-Harness" -GenerateOnly
```

已经接入的 DSH 会话如具备终端工具，也可以直接运行本地桥接命令：

```powershell
node .\bridge\pet.mjs list
node .\bridge\pet.mjs carry Steam 右下角
node .\bridge\pet.mjs carry Steam 右下角 --real
node .\bridge\pet.mjs undo
node .\bridge\pet.mjs stop
node .\bridge\pet.mjs say 我来帮你搬！
node .\bridge\pet.mjs state think
```

桥接同时提供标准 **MCP stdio** 服务，并已使用 SDK 客户端验证发现和调用。需要手动启动时，可使用 `Connect-DSH.cmd -GenerateOnly` 输出的 overlay 路径：

```powershell
cd "你的路径\DeepSeek-Harness"
pnpm dsh web --patch "$env:LOCALAPPDATA\WhaleAlive\DSH\whale-alive.cordis.yml"
```

15 个工具包括：`desktop_list_icons`、`desktop_carry_icon`、`pet_status`、`pet_say`、`pet_state`、`pet_stop`、`pet_undo`、`cursor_grab`、`pet_companion_status`、`pet_todo`、`pet_deliver_artifact`、`pet_play`、`pet_list_windows`、`pet_window`、`pet_icon_plan`。DSH 会为它们加上 `mcp__whale_alive__` 前缀。

DSH Web 进程保留自身登录鉴权；本项目不会导出浏览器凭据或修改鉴权。overlay 中的 `dsh-plugin.mjs` 只订阅任务状态事件并转发动画阶段，不转发提示词、聊天内容、文件内容或账号信息。事件映射已有单元测试，`pet_state` 也可以显式驱动动画。

## 结构

| 位置 | 用途 |
| --- | --- |
| `Connect-DSH.cmd` / `Connect-DSH.ps1` | 按当前机器路径生成 overlay，并同时启动桌宠与 DSH |
| `Start-WhaleAlive.cmd` / `Start-WhaleAlive.ps1` | 单独构建并启动桌宠 |
| `prototype/ShellDesktop.cs` | IFolderView 枚举、图标图像、位置读写 |
| `prototype/Interaction.cs` | 搬运流程、取消、权限、落盘撤销 |
| `prototype/PetWindow.cs` | 透明置顶窗口、sprite、挂点、拖拽 |
| `prototype/CursorControl.cs` | 有界鼠标拉动与用户挣脱检测 |
| `prototype/EmergencyStop.cs` | 全局双 Esc，仅观察 Esc，不拦截输入 |
| `bridge/pet.mjs` | 同一 Windows 用户下的 named pipe 客户端 |
| `bridge/mcp-server.mjs` | 标准 MCP 服务 |

撤销历史位于 `%LOCALAPPDATA%/WhaleAlive/history.json`。移动前先持久化 pending 记录，完成后记下实际位置；异常记录保留供恢复。目标重命名/消失或用户再次移动目标时拒绝覆盖。

## 开发与验证

主页「消息与音乐」支持系统通知提醒及隐私设置、音乐控制和歌名气泡。通知需安装本机身份组件并授权，循环模式依赖播放器支持；见 [接入与使用说明](SOCIAL-MUSIC.md)。

需要 Node.js 18+、.NET 9 SDK（运行已构建程序需要 .NET 9 Desktop Runtime）。

```powershell
npm ci
npm run build
# 启动桌宠且两项真实权限关闭后：
npm test
# 创建自己的唯一临时桌面文件，验证原生位置读写并恢复、清理：
prototype/bin/Release/net9.0-windows/WhaleAlive.exe --verify-shell
prototype/bin/Release/net9.0-windows/WhaleAlive.exe --verify-interaction
```

待机坐点和散步支持主副屏（含负坐标）。新版保留推拉窗口及撤销、图标整理计划、泡泡待办、文件暂存和产物快递；完整入口和限制见 [桌宠互动](COMPANION-FEATURES.md)。

## 实现参考

- Shell 实现依据微软的 [IFolderView](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview) 和 [桌面图标位置示例](https://devblogs.microsoft.com/oldnewthing/20130318-00/?p=4933)。
