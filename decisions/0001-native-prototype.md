# 0001: Windows 原型优先验证真实桌面交互

使用机器上已经安装的 .NET 9 SDK / WPF 构建透明桌宠。角色立绘与成品序列帧由本项目基于用户提供的角色基准图重新设计、生成和处理；运行时仅加载 `prototype/Assets/Finished/manifest.json` 及其对应帧。

Shell 使用官方 `IFolderView`，不用 Explorer 跨进程内存读写或模拟拖拽。视觉副本在动画期间挂在角色上，真实操作仅在 drop 时提交。实测 Explorer 会异步吸附网格，因此提交后等待至少 600ms 且连续多次坐标稳定，再完成 journal。提交前取消保持原位；提交后取消只停动画，保留已完成状态与撤销。

DSH 通过同一用户范围 named pipe 调用；MCP 使用官方 TypeScript SDK，不开放 TCP 控制端口。原生程序是权限与撤销的唯一权威。自动状态转发只传状态名，既不依赖浏览器凭据，也不转发会话文本。

下一阶段可将原生 provider 与状态机迁移到 Rust/Tauri；应以原生往返测试、取消测试、跨重启撤销与 MCP 契约作为兼容要求。
