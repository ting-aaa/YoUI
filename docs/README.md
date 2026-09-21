# YoUI 文档

[返回项目首页](../README.md)

这套文档对应仓库当前 **0.1.0** 实现。YoUI 由独立 Rust 渲染器、可选 Rust 场景树、C# 保留式 UI 和 Windows 宿主组成。目前在 Windows x64 上验证了 **DX12 / Vulkan / 原生 OpenGL**，详见[渲染后端验证](render-backends.md)；其他平台和未实现能力见[设计对照](design-status.md)。

## 从这里开始

| 目标 | 文档 |
| --- | --- |
| 安装构建环境，运行第一个 C# 窗口 | [快速开始](getting-started.md) |
| 编排布局、绑定事件、使用控件和自定义绘制 | [使用指南](guide.md) |
| 用可视化 Editor 制作界面、保存和导出 | [Editor 指南](editor.md) |
| 查询类型、成员、参数、返回值和限制 | [API 索引](api/README.md) |
| 加载 `.youi.json`，根据节点 ID 连接业务逻辑 | [资源 API 与文件格式](api/assets.md) |
| 编写导出插件或无窗口批量导出 | [编辑器与导出 API](api/editor.md) |
| 只用 Rust 或 C/C++ 渲染 | [原生 API](api/native.md) |
| 排查 DLL、布局、图集或导出错误 | [常见问题](troubleshooting.md) |
| 理解分层、颜色、绘制顺序与性能边界 | [架构约定](architecture.md) |
| 了解已经完成的验证和仍未验证的能力 | [验证记录](verification.md)、[设计对照](design-status.md) |

建议第一次使用时按“快速开始 → 使用指南 → 对应模块 API”阅读。Editor 用户可以先阅读 Editor 指南，再查资源和导出 API。

## 示例

| 示例 | 作用 |
| --- | --- |
| [HelloYoUI](examples/HelloYoUI/Program.cs) | 最小交互窗口，也支持离屏输出 PNG；[项目文件](examples/HelloYoUI/HelloYoUI.csproj) |
| [welcome.youi.json](examples/welcome.youi.json) | 可直接打开或加载的版本 1 资源 |
| [YoUI.Showcase](../samples/YoUI.Showcase/Program.cs) | 综合控件、虚拟列表、截图、窗口检查与性能探针 |
| [InventoryExporter](../samples/YoUI.Exporter.Sample/InventoryExporter.cs) | 可构建的本地导出插件 |
| [render_scene.rs](../native/youi-render/examples/render_scene.rs) | 绕过 C# UI，直接提交原生绘制命令 |
| [scene_graph.rs](../native/youi-scene/examples/scene_graph.rs) | 独立场景树、变换与共享网格 |

除非另有说明，PowerShell 命令从仓库根目录运行。`-Configuration` 用于项目脚本；`-c` 用于 `dotnet`；Rust Release 使用 `--release`。C# 命名空间与项目名不总是相同，例如 `YoUI.Editor.Core` 中的公开接口位于 `YoUI.Editor`。

## 文档维护

API 页面列出手写公开类型与成员，包含用于自定义控件的受保护扩展点；不逐项列出 record 自动生成的比较、解构等成员。每页链接到实际源码。示例应用、Editor 可执行程序内部类、P/Invoke 私有声明不是运行库 API。

变更公开接口时同步修改对应 API 页和示例；变更支持范围时同步修改设计对照。Rust 还可以从源码生成本地参考：

```powershell
cargo doc --workspace --no-deps --locked
```

输出入口为 `target/doc/youi_render/index.html` 和 `target/doc/youi_scene/index.html`，需先执行上述命令才会存在。
