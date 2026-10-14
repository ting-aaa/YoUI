# 几何、布局与文档 API

[API 索引](README.md) · [控件](controls.md) · [绘制](rendering.md)

命名空间：`YoUI`。源码：[Geometry.cs](../../managed/YoUI/Geometry.cs)、[Element.cs](../../managed/YoUI/Element.cs)、[UiDocument.cs](../../managed/YoUI/UiDocument.cs)。

## Rect

`readonly record struct Rect(float X, float Y, float Width, float Height)`。

| 成员 | 返回值 / 语义 |
| --- | --- |
| `Right`、`Bottom` | `float`，分别为 X+Width、Y+Height |
| `Contains(Vector2 p)` | `bool`，包含左/上边界，不包含右/下边界 |
| `Intersect(Rect b)` | 交集矩形，不相交时宽/高取零 |
| `Inset(float p)` | 四边内缩，宽/高最小为零 |
| `Translate(Vector2 v)` | 平移后的新矩形 |

这些计算不自动验证所有输入；提交原生绘制时普通矩形需有限且宽高非负。

## Color

`readonly record struct Color(float R, float G, float B, float A = 1)`。

| 成员 | 语义 |
| --- | --- |
| `Color.Hex(uint rgb, float alpha = 1)` | 从 `0xRRGGBB` 与单独的 alpha 构造颜色 |
| `Opacity(float value)` | 返回 A 乘以 value 后的颜色，RGB 不变；不自动钳制 |
| `Color.Transparent` | `(0,0,0,0)` |

输入为非预乘 sRGB。注意 `default(Color)` / 默认字段是透明黑，而 `new Color(r,g,b)` 的 alpha 是 1。

## RectTransform

`readonly record struct RectTransform(Vector2 AnchorMin, Vector2 AnchorMax, Vector2 Pivot, Vector2 Position, Vector2 SizeDelta)`。

| 成员 | 语义 |
| --- | --- |
| `Resolve(Rect parent)` | 返回相对于父内容区的最终 `Rect` |
| `Fixed(float x, float y, float w, float h)` | 左上锚点与轴心，固定位置和尺寸 |
| `Stretch(float left = 0, float top = 0, float right = 0, float bottom = 0)` | 双轴拉伸，保留四边边距 |

解析公式（向量乘法逐分量计算）：

```text
extent = (parent.Width, parent.Height)
min    = (parent.X, parent.Y) + extent * AnchorMin
span   = extent * (AnchorMax - AnchorMin)
size   = max((0,0), span + SizeDelta)
origin = min + span * Pivot + Position - size * Pivot
```

通常锚点和轴心取 `0..1`；当前结构本身不限制此范围。父节点为普通 `Element` 时使用子节点 Transform；StackPanel 子节点必须不设置 Transform。

## Theme

`sealed record Theme(Color Background, Color Surface, Color SurfaceRaised, Color Text, Color Muted, Color Accent, Color Border)`。

`Theme.Dark` / `Theme.Light` 是内置主题。`OnAccent` 根据 Accent 亮度选择前景色。使用 `with` 构造修改后的主题，再赋给 `UiDocument.Theme`。

## DirtyFlags

`[Flags] enum DirtyFlags`：`None=0`、`Paint=1`、`Layout=2`、`Semantics=4`、`All=7`。布局失效同时触发绘制和语义失效。

## Element

`class Element`，默认构造函数 `Element()`。子节点按添加顺序绘制，命中测试从后向前检查。

### 身份与状态

| 属性 | 类型 / 默认值 | 说明 |
| --- | --- | --- |
| `Name` | `string init`，空串 | 应用标识；不保证唯一 |
| `Parent` / `Document` | `Element? get` / `UiDocument? get` | 当前父节点与文档 |
| `Children` | `IReadOnlyList<Element> get` | 只读列表视图；通过 Add/Remove/Clear 修改 |
| `Bounds` / `DesiredSize` | `Rect get` / `Vector2 get` | 布局结果 / 测量结果；DesiredSize 可由派生类设置 |
| `Dirty` / `LayoutRevision` | `DirtyFlags get` / `long get` | 文档阶段待处理标记 / 每次基类 Arrange 增加的计数 |
| `Visible` / `Enabled` | `bool`，均为 true | 隐藏则不布局/绘制/命中；禁用子树不接收输入，绘制时降低透明度 |
| `HitTestVisible` | `bool`，false | 本节点可否成为指针目标；false 不阻止后代命中 |
| `Focusable` | `bool get`，false | 派生类可设置；决定能否获得焦点 |
| `IsHovered` / `IsFocused` | `bool get` | 当前悬停 / 焦点状态 |
| `Role` | `string get`，`"group"` | 派生类可设置的语义角色 |
| `AccessibleName` | `string`，空串 | 语义名称；变化时请求 Semantics |
| `SemanticValue` | `virtual string get`，空串 | 控件语义值 |

隐藏/禁用不会立即完成所有焦点与捕获清理；后续输入分发会检查有效性，移除节点会触发清理。禁用透明度按层级相乘，因此多层禁用可能进一步变暗。

### 布局与绘制属性

| 属性 | 默认值 | 语义 |
| --- | --- | --- |
| `Width`、`Height`（float） | `NaN` | 自动尺寸；显式值须有限且非负 |
| `Grow`（float） | 0 | StackPanel 主轴剩余空间权重，有限且非负 |
| `Padding`（float） | 0 | 四边同值，有限且非负 |
| `Transform`（RectTransform?） | null | 自由布局参数 |
| `Background`（Color） | 透明黑 | 基类 Paint 绘制的背景色；派生控件可覆盖其行为 |
| `Radius`（float） | 0 | 背景圆角，有限且非负，不定义裁剪形状 |
| `ClipToBounds`（bool） | false | 对本节点和后代矩形裁剪 |
| `InheritedOpacity`（float） | 1 | 累乘到后代图元；有限值钳制为 0..1 |
| `CompositedOpacity`（float） | 1 | 小于 1 时创建隔离层并整体合成；有限值钳制为 0..1 |
| `VisualOffset`（Vector2） | 零 | 视觉与命中平移，触发绘制和语义更新；调用者提供有限值 |
| `VisualBounds`（Rect get） | 计算值 | Bounds 加上本节点及全部祖先 VisualOffset |

### 树和布局方法

| 方法 | 返回值 / 行为 |
| --- | --- |
| `T Add<T>(T child) where T : Element` | 添加并返回 child；拒绝环、已有父级或已附着文档的节点 |
| `bool Remove(Element child)` | 移除成功返回 true；解除文档关联并清理焦点/捕获等 |
| `void Clear()` | 移除全部直接子节点及其关联 |
| `void Invalidate(DirtyFlags flags = DirtyFlags.Paint)` | 使本节点、祖先和文档失效 |
| `virtual Vector2 Measure(Vector2 available)` | 设置并返回 DesiredSize |
| `virtual void Arrange(Rect bounds)` | 写入 Bounds、增加 LayoutRevision、排列子节点 |

`Measure / Arrange` 通常由文档调用。普通 Element 的自动尺寸是可见子节点最大需求加 Padding；它不是顺序容器。当前 Layout 失效最终会重排可见树，不是局部子树增量布局。

### 派生扩展点

| 成员（protected） | 用途 |
| --- | --- |
| `bool Set<T>(ref T field, T value, DirtyFlags flags)` | 值相同时返回 false；否则赋值、失效并返回 true |
| `static float NonNegative(float value)` | 验证有限非负数；非法时抛出 ArgumentOutOfRangeException |
| `virtual void ArrangeChildren(Rect content)` | 在已扣除 Padding 的内容区排列孩子 |
| `virtual void Paint(DrawList list, Rect bounds, float opacity, Theme theme)` | 按顺序输出绘制命令；默认绘制有 alpha 的背景 |
| `virtual void OnEvent(UiEvent e)` | 常规事件在目标阶段且未 Handled 时执行默认交互；PointerCancel / Blur 的目标清理先于观察回调且不可拦截 |

`event Action<UiEvent>? Event` 在常规事件的默认 OnEvent 之前触发。PointerCancel / Blur 目标事件先执行 OnEvent 清理，再通知 Event；两者即使抛错仍分别尝试并保留首个异常，避免旧交互状态跨重挂保留。同一目标的本次清理不会在其观察回调之后覆盖该回调中新建的手势；多个目标或嵌套取消不承诺事务或新手势保留。自定义绘制示例见[使用指南](../guide.md#自定义绘制)。

## StackPanel

`sealed class StackPanel : Element`。

| 成员 | 说明 |
| --- | --- |
| `bool Horizontal { get; init; }` | 默认 false（纵向） |
| `float Gap` | 默认 0，有限且非负 |
| `override Measure(Vector2 available)` | 先测量各子项；子项有 Transform 时抛 InvalidOperationException |
| `override ArrangeChildren(Rect content)` | 按顺序排布，并按 Grow 分配正剩余空间 |

不实现自动收缩、换行或 Grid。`Horizontal` 不能在初始化后切换。

## UiEvent、EventKind 与 EventPhase

`new UiEvent(EventKind kind)`。事件字段：

| 成员 | 类型 | 说明 |
| --- | --- | --- |
| `Kind` | `EventKind get` | 事件类型 |
| `Position` | `Vector2 init` | 文档坐标；Windows 宿主已转换为 DIP |
| `Delta` | `float init` | 滚轮步数；Windows 一格为 ±1 |
| `Key` | `int init` | 当前采用 Win32 虚拟键值，如 Tab=9、Enter=13、Escape=27、Space=32 |
| `Shift`、`Control` | `bool init` | 修饰键状态 |
| `Text` | `string init` | 提交文字或预编辑文字；默认空串 |
| `Phase` | `EventPhase get` | 由路由器设置 |
| `Target` | `Element? get` | 当前路由目标 |
| `Handled` | `bool` | 停止常规事件的后续传播或默认处理；不阻止 PointerCancel / Blur 的目标清理 |
| `CapturePointer` | `bool` | 请求目标接收后续指针事件 |

`EventKind` 完整取值：`PointerMove`、`PointerDown`、`PointerUp`、`PointerCancel`、`Scroll`、`KeyDown`、`TextInput`、`Composition`、`Focus`、`Blur`。没有 KeyUp、双击或多指针 ID。`EventPhase` 为 `Capture`、`Target`、`Bubble`。

路径在分发前冻结，可以在回调内移除节点。滚轮使用当前命中节点，不跟随逻辑指针捕获。指针抬起释放捕获；自定义宿主丢失 OS 捕获时调用 `CancelPointer`。

## UiDocument

`UiDocument(Element root, ITextMeasurer textMeasurer, float width, float height)`。宽高须有限且大于零；root 必须尚未附着。文档不拥有 measurer 的释放职责。

| 属性 / 事件 | 说明 |
| --- | --- |
| `Element Root { get; }` | 根节点 |
| `Element? Focused { get; }` | 当前焦点 |
| `ITextMeasurer TextMeasurer { get; }` | 布局使用的文字测量器 |
| `IClipboard? Clipboard` | 默认 null，自定义宿主可注入 |
| `Theme Theme` | 默认 Theme.Dark；不同有效值使绘制失效 |
| `DirtyFlags PendingUpdates { get; }` | 尚未消费的文档阶段 |
| `bool NeedsRender { get; }` | Layout 或 Paint 存在时为 true；不包含单独的 Semantics |
| `int LayoutPasses / PaintPasses { get; }` | 实际布局 / 重建绘制命令的次数 |
| `event Action<UiEvent>? PreviewInput` | 常规输入路由之前执行；不是平台 OS 消息事件 |

| 方法 | 行为 |
| --- | --- |
| `void Resize(float width, float height)` | 更新文档尺寸并使全部失效；不调整 GPU 目标尺寸 |
| `void EnsureLayout()` | 有 Layout 脏标记时最多测量并排列一次 |
| `bool UpdateLayout(int maxPasses = 8)` | 最多执行指定次数布局；true 表示稳定，false 保留未完成工作；不绘制 |
| `DrawList BuildFrame()` | 最多一次前置布局，必要时重绘并消费 Paint；保留 Semantics 和回调中的新失效，返回复用的帧 |
| `void Dispatch(UiEvent e)` | 先布局，再 PreviewInput，再默认 Tab / 命中 / 路由 |
| `void Focus(Element? element)` | null 清除焦点；非法、隐藏、禁用或非可聚焦目标不生效 |
| `void SetModal(Element? element)` | 限制命中与焦点遍历范围；null 解除并尝试恢复原焦点 |
| `void CancelPointer()` | 向被捕获控件发送 PointerCancel 并解除捕获 |
| `IReadOnlyList<SemanticNode> Semantics()` | 最多一次前置布局，消费 Semantics 并返回当前可见且启用范围中的可聚焦节点与文字节点 |

BuildFrame 首先绘制主题背景，然后按树顺序绘制。返回值是缓存对象；调用方不应修改它。尺寸变化时，自定义宿主必须同时调整 renderer 和 document。

SetModal 要求目标属于本文档；它不改变 Visible、不创建遮罩、不管理模态栈。Focus/Blur 是直接发送到节点的目标阶段通知，不经完整 PreviewInput / 捕获 / 冒泡路径。

## IClipboard 与 SemanticNode

`IClipboard` 包含 `string GetText()` 和 `void SetText(string text)`。Windows 宿主自动注入内部实现；纯逻辑测试可注入内存实现。

`sealed record SemanticNode(string Name, string Role, string Value, Rect Bounds, bool Enabled, bool Focused)` 是语义快照。这里的 Name 来源于 `AccessibleName`，Bounds 包含视觉偏移。当前输出为平面列表，不含父子结构或平台辅助技术桥接。

## 最小字段属性描述器（A04）

`UiProperty<TOwner, TValue>` 为字段提供不可变元数据：`Name`、`DefaultValue`、`Invalidation`，以及可选的规范化/验证函数和类型安全变更回调。控件继续公开普通 C# 属性；没有反射、属性字典、绑定表达式或值优先级系统。

派生节点的 setter 调用 `SetProperty(this, ref field, value, descriptor)`：先规范化（验证失败时抛出且不写入），再用 `EqualityComparer<T>.Default` 比较；只有有效值变化时才写入、失效并通知。字段需显式使用描述器默认值初始化。变更回调同步执行，发生在失效之后；回调异常不回滚已经写入的值。回调可以引发后续变更，不提供批处理或重入队列。

本轮仅迁移 `Toggle.Value` 和 `Slider.Value`，沿用各自的类型化 `Changed` 事件。Slider 保留原有 `Math.Clamp(value, 0, 1)` 语义：有限越界值/正负无穷钳到端点，NaN 保留且重复 NaN 不再通知。此兼容性约定不表示 NaN 可用于原生绘制；应用应提供有限绘制数据。Toggle 的字幕 ON/OFF 改变仍可通过 Label 请求布局；独立 Slider 值变化只有 Paint/Semantics。

`Element.OnDocumentChanged(previous, current)` 在子树文档关联完成后调用。示例在加入文档时订阅，在移出时退订，并在重新加入时应用最新状态。它不是窗口生命周期或文档 Dispose API。

文档在布局/绘制/语义回调之前消费整棵树对应失效位，保留回调期间的新失效。每次实际布局都请求绘制和语义更新；失败阶段恢复待处理工作。同一文档的更新回调中不能嵌套更新。完整的有限次收敛、重入、隐藏节点和重挂契约见 [A09 布局更新](../layout-updates.md)。

### Rebinding a text provider

`UiDocument.RebindTextMeasurer(ITextMeasurer)` replaces the host-owned provider
between updates on the document's creating thread, clears cached commands and
invalidates layout, paint and semantics. It does not dispose either provider or
reupload images. See [renderer reconstruction](../renderer-recovery.md).
