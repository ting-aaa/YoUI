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

`[Flags] enum DirtyFlags`：`None=0`、`Paint=1`、`Layout=2`、`Semantics=4`、`All=7`。布局失效同时触发绘制失效。

## Element

`class Element`，默认构造函数 `Element()`。子节点按添加顺序绘制，命中测试从后向前检查。

### 身份与状态

| 属性 | 类型 / 默认值 | 说明 |
| --- | --- | --- |
| `Name` | `string init`，空串 | 应用标识；不保证唯一 |
| `Parent` / `Document` | `Element? get` / `UiDocument? get` | 当前父节点与文档 |
| `Children` | `IReadOnlyList<Element> get` | 只读列表视图；通过 Add/Remove/Clear 修改 |
| `Bounds` / `DesiredSize` | `Rect get` / `Vector2 get` | 布局结果 / 测量结果；DesiredSize 可由派生类设置 |
| `Dirty` / `LayoutRevision` | `DirtyFlags get` / `long get` | 节点脏标记 / 每次 Arrange 增加的计数 |
| `Visible` / `Enabled` | `bool`，均为 true | 隐藏则不布局/绘制/命中；禁用子树不接收输入，绘制时降低透明度 |
| `HitTestVisible` | `bool`，false | 本节点可否成为指针目标；false 不阻止后代命中 |
| `Focusable` | `bool get`，false | 派生类可设置；决定能否获得焦点 |
| `IsHovered` / `IsFocused` | `bool get` | 当前悬停 / 焦点状态 |
| `Role` | `string get`，`"group"` | 派生类可设置的语义角色 |
| `AccessibleName` | `string`，空串 | 语义名称 |
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
| `VisualOffset`（Vector2） | 零 | 视觉与命中平移，仅触发绘制；调用者提供有限值 |
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
| `virtual void OnEvent(UiEvent e)` | 目标阶段且未 Handled 时执行默认交互 |

`event Action<UiEvent>? Event` 在该节点默认 OnEvent 之前触发。自定义绘制示例见[使用指南](../guide.md#自定义绘制)。

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
| `Handled` | `bool` | 停止后续传播或默认处理 |
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
| `Theme Theme` | 默认 Theme.Dark；赋值使绘制失效 |
| `bool NeedsRender { get; }` | 任意文档脏标记存在时为 true |
| `int LayoutPasses / PaintPasses { get; }` | 实际布局 / 重建绘制命令的次数 |
| `event Action<UiEvent>? PreviewInput` | 常规输入路由之前执行；不是平台 OS 消息事件 |

| 方法 | 行为 |
| --- | --- |
| `void Resize(float width, float height)` | 更新文档尺寸并使全部失效；不调整 GPU 目标尺寸 |
| `void EnsureLayout()` | 有 Layout 脏标记时测量并排列 |
| `DrawList BuildFrame()` | 必要时布局/重绘，清除文档脏标记，返回复用的帧 |
| `void Dispatch(UiEvent e)` | 先布局，再 PreviewInput，再默认 Tab / 命中 / 路由 |
| `void Focus(Element? element)` | null 清除焦点；非法、隐藏、禁用或非可聚焦目标不生效 |
| `void SetModal(Element? element)` | 限制命中与焦点遍历范围；null 解除并尝试恢复原焦点 |
| `void CancelPointer()` | 向被捕获控件发送 PointerCancel 并解除捕获 |
| `IReadOnlyList<SemanticNode> Semantics()` | 返回当前可见且启用范围中的可聚焦节点与文字节点 |

BuildFrame 首先绘制主题背景，然后按树顺序绘制。返回值是缓存对象；调用方不应修改它。尺寸变化时，自定义宿主必须同时调整 renderer 和 document。

SetModal 要求目标属于本文档；它不改变 Visible、不创建遮罩、不管理模态栈。Focus/Blur 是直接发送到节点的目标阶段通知，不经完整 PreviewInput / 捕获 / 冒泡路径。

## IClipboard 与 SemanticNode

`IClipboard` 包含 `string GetText()` 和 `void SetText(string text)`。Windows 宿主自动注入内部实现；纯逻辑测试可注入内存实现。

`sealed record SemanticNode(string Name, string Role, string Value, Rect Bounds, bool Enabled, bool Focused)` 是语义快照。这里的 Name 来源于 `AccessibleName`，Bounds 包含视觉偏移。当前输出为平面列表，不含父子结构或平台辅助技术桥接。
