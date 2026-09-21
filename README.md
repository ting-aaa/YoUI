# YoUI

**Rust 原生 2D 渲染引擎 + C# 保留式 UI 框架。**

当前为可运行、带验证的 **0.1 桌面基础版本**。它实现了从 C# 控件树到 GPU 输出的真实链路，包含独立 Rust 场景示例、交互窗口和使用同一 UI 库自举的基础 UI Editor。全平台发布、动画 SDK 和高级编辑功能仍需继续实现；具体边界见 [设计对照](docs/design-status.md)。

![YoUI 深色主题](artifacts/showcase-dark.png)

## 文档

- [文档首页](docs/README.md)：按任务查找指南和接口。
- [快速开始](docs/getting-started.md)：环境准备、构建、运行第一个窗口和离屏截图。
- [使用指南](docs/guide.md)：布局、控件事件、主题、列表、模态、自定义绘制和资源加载。
- [API 文档](docs/api/README.md)：C# UI、渲染、Windows 宿主、资源、编辑器、Rust 与 C ABI。
- [Editor 使用与导出](docs/editor.md)：可视化编辑、配置和 C# 导出、插件开发。
- [常见问题](docs/troubleshooting.md)：构建、DLL、布局、资源和导出问题。
- [渲染后端验证](docs/render-backends.md)：DX12、Vulkan、原生 OpenGL 的画面、窗口及复现脚本。

## 运行

本次已验证环境：Windows x64、Rust 1.96、.NET SDK 9.0.318；NVIDIA RTX 5060 Laptop 的 DX12/Vulkan 与 AMD 860M 的原生 OpenGL。默认使用 DX12，可通过 `YOUI_BACKEND` 选择；详见后端验证文档。Rust MSVC 工具链需要 Visual Studio C++ Build Tools；首次构建会获取 Cargo 依赖。

```powershell
./scripts/run.ps1
```

优化版本：

```powershell
./scripts/run.ps1 -Configuration Release
```

界面支持搜索十万条记录、滚轮和键盘选择、深浅主题、透明度滑块、动画开关、父级裁剪、确认弹窗。Tab / Shift+Tab 切换焦点，Enter / Space 激活按钮；列表使用上下键及 Home / End；文本输入支持组合字符删除、Shift 选择、Ctrl+A/C/X/V、Ctrl+Z/Y 与 IME 预编辑消息。

## 工程结构

UI Editor 启动：`./scripts/editor.ps1`。支持层级编辑、画布移动/缩放、属性检查、撤销重做、实时交互预览、配置与 C# 代码导出，以及本地 DLL 导出插件。详见 [Editor 使用和插件开发](docs/editor.md)。

```text
native/youi-render        wgpu、Render IR、字形/图像图集、合成、C ABI
native/youi-scene         可选场景树、代际节点 ID、仿射变换、三角网格
managed/YoUI              C# 节点、布局、控件、输入、主题、虚拟列表
managed/YoUI.Editor.Core  编辑事务、资源持久化、配置/代码/插件导出
managed/YoUI.Platform.Windows  Win32 窗口、消息、DPI、IME
tools/YoUI.Editor         使用 YoUI 控件自举的原生编辑器
samples/YoUI.Exporter.Sample   可加载的导出插件示例
samples/YoUI.Showcase     交互式示例、截图、窗口冒烟检查、性能测量
tests/YoUI.Tests          无 GPU 的 C# 行为验证
tests/YoUI.Editor.Tests   资源、控件、编辑事务、代码编译和插件验收
scripts                  构建、运行、验证
docs                     使用指南、API 参考、可运行入门示例、架构与验证结果
```

渲染引擎不依赖场景或控件。C# 每帧通过一个命令数组和一个 UTF-8 缓冲区提交，原生端不会回调读取 UI 树。帧循环由宿主驱动。

## C# 使用示例

```csharp
using YoUI;
using YoUI.Platform.Windows;

var root = new StackPanel { Padding = 24, Gap = 16 };
root.Add(new Label("Hello, YoUI") { FontSize = 28, Bold = true });
var count = root.Add(new Label("Clicked 0 times"));
var button = root.Add(new Button("Click me") { Width = 160, Primary = true });
int clicks = 0;
button.Clicked += () => count.Text = $"Clicked {++clicks} times";

using var window = new DesktopWindow("My application", root);
window.Run();
```

应用需引用 `YoUI` 和 `YoUI.Platform.Windows`，并将对应配置的 `youi_render.dll` 复制至输出目录。示例项目的构建目标演示了这一过程。使用 `using` 按“图像资源 → 渲染器 → 窗口”的顺序释放；`DesktopWindow` 会自动先释放渲染器，再销毁窗口。

## 独立使用 Rust 引擎

```powershell
cargo run -p youi-render --example render_scene -- artifacts/render-scene.png
cargo run -p youi-scene --example scene_graph -- artifacts/scene-graph.png
```

第一个示例直接提交图元和文字，第二个使用独立场景树的旋转、父级变换及共享网格；两者都不加载 C# UI 或创建窗口。C/C++ 宿主可以使用 [youi.h](native/youi-render/include/youi.h)。

## 验证与性能

```powershell
./scripts/verify.ps1 -Configuration Release
```

该脚本依次执行构建、格式检查、Rust 静态检查、原生与 C# 测试、真实 GPU 像素检查、截图输出、Win32 消息交互检查、性能测量及独立示例。GPU 测试缺少适配器时失败，不会悄悄跳过。

截图和机器测量结果写入 `artifacts/`。性能报告区分 UI 构帧、原生准备、CPU 提交；不将 CPU 时间冒充 GPU 时间或 FPS。设计采用连续兼容绘制合批、实例缓冲复用、字形与排版缓存、脏标记，以及仅创建可见行的虚拟列表。无动画、无输入时窗口等待消息，不持续重绘。

查看 [验证结果](docs/verification.md)、[架构约定](docs/architecture.md) 和 [设计对照](docs/design-status.md)。Python 工具如需使用，统一通过 `uv` 管理环境。
