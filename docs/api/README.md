# API 文档

[文档首页](../README.md) · [快速开始](../getting-started.md) · [使用指南](../guide.md)

对应当前源码的 0.1.0 API。表格中的 `?` 表示可空；`get` 表示调用方只读，`init` 只能在初始化时设置；没有特别注明时，属性可读写。继承成员在基类页面说明。

## 模块索引

| 文档 | 命名空间 / crate | 公开类型和入口 |
| --- | --- | --- |
| [几何、布局与文档](core.md) | `YoUI` | `Rect`、`Color`、`RectTransform`、`Theme`、`DirtyFlags`、`Element`、`StackPanel`、`UiDocument`、`UiEvent`、`EventKind`、`EventPhase`、`IClipboard`、`SemanticNode` |
| [控件](controls.md) | `YoUI` | `Label`、`Button`、`Toggle`、`Slider`、`TextBox`、`VirtualList`、`ScrollView`、`TreeItem`、`TreeView`、`PropertyEntry`、`PropertyGrid` |
| [绘制与原生绑定](rendering.md) | `YoUI` | `ITextMeasurer`、`DrawCommand`、`DrawList`、`FrameStats`、`NativeRenderer`、`NativeImage` |
| [Windows 宿主](platform-windows.md) | `YoUI.Platform.Windows` | `DesktopWindow`、`FileDialogs` |
| [资源与文件格式](assets.md) | `YoUI.Assets` | `NodeKind`、`AnchorMode`、`UiNode`、`UiAsset`、`UiAssetJson`、`UiInstance`、`UiAssetRuntime` |
| [编辑器与导出](editor.md) | `YoUI.Editor` | `EditorSession`、`ExportContext`、`ExportFile`、`IUiExporter`、`ExportCatalog`、`JsonExporter`、`CSharpExporter` |
| [原生渲染与场景](native.md) | `youi_render`、`youi_scene` | `Renderer`、`Rect`、`Command`、`FrameStats`、`validate`、`Scene`、`NodeId`、`Transform2D`、`Mesh` |
| [C ABI](native.md#c-abi) | `youi.h` | `youi_create`、`youi_render`、`youi_measure`、图像、截图、销毁与错误查询 |

## 依赖与所有权

| 使用方式 | 需要引用 | 谁拥有循环和资源 |
| --- | --- | --- |
| C# Windows 窗口 | `YoUI` + `YoUI.Platform.Windows` + 原生 DLL | `DesktopWindow` 驱动消息循环，拥有 renderer 和 HWND |
| C# 离屏 UI | `YoUI` + 原生 DLL | 应用创建 renderer 与 document，主动构帧和释放 |
| 只做资源加载、编辑、格式导出 | `YoUI`；编辑/导出再加 `YoUI.Editor.Core` | 调用模型 API 无需创建 GPU 设备 |
| Rust 独立绘制 | `youi-render` | 宿主主动提交，Rust drop 释放 renderer |
| Rust 场景 | `youi-scene` + `youi-render` | Scene 缓存命令，宿主负责 render |
| C/C++ | `youi.h` + 对应原生库 | 宿主拥有窗口、帧循环、输入缓冲和句柄释放 |

当前没有统一的跨线程调用保证。C# renderer 强制创建线程调用；UI 和宿主也按单 UI 线程使用。原生 C ABI 的注册表串行化 renderer 调用，不等于为 UI 树提供线程安全。

## 通用约定

- C# Windows UI 布局单位为 DIP；独立 renderer / C ABI 使用物理像素。原点在左上，Y 向下。
- 输入颜色为非预乘 sRGB RGBA，分量通常应在 `[0,1]`；原生端进行验证和混合。
- 帧中裁剪和隔离层必须配对，绘制顺序有意义。
- 图像属于创建它的 renderer；节点属于创建它的树/场景。
- .NET 资源使用 `Dispose` / `using`；Rust renderer 用 drop；C ABI 调用 `youi_destroy`。
- 返回的借用帧、Span 和切片不能作为跨更新的永久快照。

支持范围参见[设计对照](../design-status.md)，历史测试证据参见[验证记录](../verification.md)。API 的存在不代表其他平台、输入法候选行为或平台无障碍已完成验收。
