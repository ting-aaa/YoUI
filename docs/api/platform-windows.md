# Windows 宿主 API

[API 索引](README.md) · [文档和输入](core.md) · [渲染器](rendering.md)

项目：`managed/YoUI.Platform.Windows`。命名空间：`YoUI.Platform.Windows`。源码：[DesktopWindow.cs](../../managed/YoUI.Platform.Windows/DesktopWindow.cs)、[FileDialogs.cs](../../managed/YoUI.Platform.Windows/FileDialogs.cs)。

## DesktopWindow

`sealed class DesktopWindow : IDisposable`。

构造函数：

```csharp
// 签名参考
DesktopWindow(string title, Element root,
    int width = 1280, int height = 820,
    int minimumWidth = 790, int minimumHeight = 740)
```

尺寸表示逻辑客户区 DIP，不含标题栏和外框。小窗口需要同时设置 minimumWidth/minimumHeight。root 不能已附着到其他文档。

创建 Win32 窗口、NativeRenderer、UiDocument 和内部剪贴板服务；设置 DPI 感知。非 Windows 调用抛 PlatformNotSupportedException，窗口创建失败可能抛 Win32Exception。

| 属性 / 事件 | 行为 |
| --- | --- |
| `NativeRenderer Renderer { get; }` | 此窗口拥有的 renderer |
| `UiDocument Document { get; }` | 此窗口拥有的文档和输入入口 |
| `FrameStats LastFrame { get; }` | 最近 RenderNow 的统计 |
| `float Scale { get; }` | 当前 DPI / 96，用于 DIP 与物理像素换算 |
| `nint Handle { get; }` | HWND，仅在窗口生命期有效 |
| `event Action? AnimationFrame` | Win32 timer 通知；修改控件属性后文档才会需要重绘 |
| `Func<bool>? Closing` | 关闭请求回调；返回 false 阻止关闭，null 或 true 允许关闭 |

| 方法 | 行为 |
| --- | --- |
| `void Run(bool visible = true, int? frameLimit = null, Action<int>? tick = null)` | 驱动消息、tick、按需重绘；默认显示窗口并阻塞直到关闭 |
| `void PumpEvents()` | 非阻塞处理待处理消息；窗口回调异常会在此重新抛出 |
| `void RenderNow()` | BuildFrame，将命令按 Scale 转换后提交 GPU |
| `void SetAnimationActive(bool active)` | 开启/关闭约 16 ms 窗口计时器 |
| `void Dispose()` | 先释放 renderer，再销毁 HWND 和窗口类；重复调用无操作 |

Run 的 tick 参数接收从 0 开始的**循环迭代编号**，不是秒或已呈现帧数。frameLimit 也限制循环迭代次数。存在 tick 时每次循环暂停约 16 ms；没有 tick 且文档不脏时使用 WaitMessage。因此测试若需要确定次数地前进，可同时提供 tick；不要用 `Run(frameLimit: N)` 推断一定会连续绘制 N 帧。

`Run(visible: false)` 仍创建 HWND、GPU 和消息循环；真正无窗口渲染使用 `NativeRenderer(window: 0)`。

Closing 处理的是窗口关闭消息，直接 Dispose 不触发 Closing。通常使用 `using` + `Run`，在同一 UI 线程管理所有权。窗口宿主尚无公开 Close 方法或后台线程调度入口。

## 输入、DPI 和资源边界

宿主将指针坐标换为 DIP、滚轮换为步数、键盘换为 UiEvent，将 WM_CHAR/IME 消息分为提交文字和预编辑。失去捕获时取消逻辑拖动；点击打开同步文件对话框前会释放 OS 鼠标捕获。

调整窗口时同时改变原生目标尺寸和文档逻辑尺寸；文字测量使用相同 Scale。应用直接向 Document 派发输入时提供 DIP，直接向 Renderer 绘制时提供物理像素。

不要在 Dispose 之前提前销毁 Handle，不要将 Renderer 传到后台线程，不要将此宿主视为多窗口并行框架。其他平台宿主、完整输入法候选行为、多显示器切换和平台无障碍的状态见[设计对照](../design-status.md)。

## FileDialogs

静态类，使用 Windows 同步文件对话框，owner 参数通常是 `window.Handle`。

| 方法 | 参数 / 返回值 |
| --- | --- |
| `string? Open(nint owner, string title, string description = "YoUI configuration", string pattern = "*.youi.json;*.json")` | 成功返回路径，取消返回 null |
| `string? Save(nint owner, string title, string fileName, string extension = "json", string description = "YoUI configuration", string pattern = "*.youi.json;*.json")` | 选择保存路径，不实际写文件 |
| `int ConfirmUnsaved(nint owner)` | 6=保存，7=放弃，2=取消；仅返回选择，不实际保存 |

对话框失败与取消不同，失败抛 Win32Exception。默认文件名过长（≥32768 字符）抛 ArgumentException。`pattern` 可以用分号分隔多个通配模式。Save 的 extension 不带前导点。

打开 DLL 时可传 description=`"Exporter assembly"`、pattern=`"*.dll"`。导出目录选择在当前 Editor 中复用 Save 选择一个尚不存在的路径，由 ExportCatalog 创建目录；它不是目录浏览 API。
