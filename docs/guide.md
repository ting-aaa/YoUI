# 使用指南

[文档首页](README.md) · [快速开始](getting-started.md) · [API 索引](api/README.md)

以下 C# 示例使用 .NET 9 的隐式 using，并通过源码引用 YoUI；窗口示例还需要 Windows 宿主和匹配的原生 DLL。各段独立展示一个用法，按说明放入自己的应用。

## 控件树与文档

`Element` 是节点基类，`Add` 返回所添加的具体控件类型，便于继续配置和绑定事件。`UiDocument` 拥有一棵树、布局、主题、输入状态和缓存帧。`DesktopWindow` 会为传入的根节点创建文档。

同一节点只能属于一棵树。重挂子节点先调用原父节点的 `Remove`，再调用新父节点的 `Add`。同一根节点不能同时用于两个窗口，也不能先 `CreateDocument` 再交给 `DesktopWindow`；需要多实例时重新构造或重新加载。

UI 树、事件和渲染操作应留在同一 UI 线程。不要直接从后台任务调用 `NativeRenderer`；当前没有内置的 UI 线程调度器。

## 布局

### 顺序布局

下面的根节点可以直接传给 `DesktopWindow`：

```csharp
using YoUI;

var root = new StackPanel { Padding = 24, Gap = 16 };
root.Add(new Label("Account") { FontSize = 24, Bold = true });
var row = root.Add(new StackPanel { Horizontal = true, Gap = 12, Height = 44 });
row.Add(new TextBox { Placeholder = "Your name", Width = 200, Grow = 1 });
row.Add(new Button("Save") { Width = 100, Height = 44, Primary = true });
```

`StackPanel` 默认纵向排列。`Padding` 是四边统一内边距，`Gap` 是子节点之间的间距，`Grow` 按权重分配主轴的正剩余空间。未指定 `Width/Height` 时值为 `float.NaN`，由测量和父布局决定；这不是负数或零。

它不是完整的 Flexbox：空间不足时不会自动收缩子项。纵向布局的子节点通常横向填满，横向布局的子节点通常纵向填满，显式尺寸可覆盖交叉轴尺寸。

### 自由布局与锚点

需要固定位置、居中或拉伸时，用普通 `Element` 容器：

```csharp
using System.Numerics;
using YoUI;

var root = new Element();
root.Add(new Element
{
    Transform = RectTransform.Stretch(24, 24, 24, 24),
    Background = Color.Hex(0x1B293B),
    Radius = 12
});
root.Add(new Button("Centered")
{
    Transform = new RectTransform(
        new Vector2(.5f), new Vector2(.5f), new Vector2(.5f),
        Vector2.Zero, new Vector2(180, 44))
});
```

`RectTransform.Fixed(x, y, w, h)` 使用父内容区左上角坐标。`Stretch(left, top, right, bottom)` 的后两个参数也是边距。设置了 `Transform` 后，普通父节点用它解析最终矩形，不能再依靠子节点 `Width/Height` 覆盖该矩形。

**StackPanel 的直接子节点不能设置 Transform**，否则布局时抛出异常。可以在 StackPanel 中放一个普通容器，再在容器内部使用锚点。

坐标原点在左上角，X 向右、Y 向下。Windows UI 使用 DIP，宿主负责 DPI 转换；原生渲染器接收物理像素。

## 事件与输入

常见业务逻辑使用控件专属事件：

```csharp
using YoUI;

var root = new StackPanel { Padding = 24, Gap = 12 };
var name = root.Add(new TextBox { Placeholder = "Your name" });
var result = root.Add(new Label("Enter a name"));
var save = root.Add(new Button("Save") { Width = 120 });
name.Changed += value => save.Enabled = !string.IsNullOrWhiteSpace(value);
save.Enabled = false;
save.Clicked += () => result.Text = $"Saved: {name.Text}";
```

`Toggle.Changed`、`Slider.Changed` 和 `TextBox.Changed` 在程序赋值改变值时也会触发，初始化业务状态时注意订阅顺序。

需要全局快捷键时，在窗口创建后、`Run` 前订阅 `window.Document.PreviewInput`。例如处理 `KeyDown` 的 Escape（`Key == 27`），并设置 `Handled = true`。它在控件路由和默认 Tab 焦点逻辑之前运行。

`Element.Event` 按捕获、目标、冒泡阶段分发。父节点可能收到同一输入的两个阶段，处理前检查 `Phase`。`Handled` 会停止后续路由，也可阻止目标控件的默认行为。拖动控件可设置 `CapturePointer = true`，并在 `PointerUp / PointerCancel / Blur` 清理拖动状态。

`TextBox` 当前是单行输入：键盘选择、字素移动/删除、剪贴板、撤销与重做可用。鼠标点击将光标置于末尾，尚无鼠标精确定位和拖选。`DesktopWindow` 自动配置剪贴板，自定义宿主需要提供 `IClipboard`。输入法候选窗口的完整行为仍有待专门验收。

## 主题、透明度和动画

创建窗口后设置 `window.Document.Theme = Theme.Light` 即可切换主题。也可以使用 record 的复制语法，如 `Theme.Dark with { Accent = Color.Hex(0x60A5FA) }`。控件显式设置的颜色优先于主题颜色，不会随主题自动替换。

| 属性 | 含义 |
| --- | --- |
| `InheritedOpacity` | 逐图元累乘到后代；重叠的子项仍独立混合 |
| `CompositedOpacity` | 先将子树绘制为隔离层，再对整体应用透明度 |
| `ClipToBounds` | 对本节点及后代应用矩形裁剪；`Radius` 不会把它变成圆角遮罩 |
| `VisualOffset` | 平移显示和命中区域，不重做布局 |

`Color` 接收 sRGB、非预乘 RGBA；不要手动预乘。透明内容按子节点添加顺序绘制，后添加的通常覆盖在上面。

动画可以订阅 `window.AnimationFrame`，修改 `VisualOffset` 等属性，再调用 `window.SetAnimationActive(true)` 启用约 16 ms 定时器。回调应按实际经过时间计算进度；定时器不保证精确 60 FPS。停止时调用 `SetAnimationActive(false)`。无动画、无输入时窗口等待消息。

## 大列表

```csharp
using YoUI;

var records = Enumerable.Range(1, 100_000).Select(i => $"Record {i}").ToArray();
var filtered = records;
var root = new StackPanel { Padding = 24, Gap = 12 };
var search = root.Add(new TextBox { Placeholder = "Filter records" });
var list = root.Add(new VirtualList
{
    RowHeight = 54,
    Height = 300,
    Grow = 1,
    ItemText = i => filtered[i],
    ItemCount = filtered.Length
});
search.Changed += value =>
{
    filtered = records.Where(x => x.Contains(value, StringComparison.OrdinalIgnoreCase)).ToArray();
    list.ItemCount = filtered.Length;
    list.SelectedIndex = -1;
    list.ScrollOffset = 0;
    list.Refresh();
};
list.SelectionChanged += index =>
{
    if (index >= 0) Console.WriteLine(filtered[index]);
};
```

数据仍由应用管理，控件只实例化视口附近的按钮行。`ItemText` 应快速、同步返回；同样数量的数据内容变化后调用 `Refresh`。列表按索引选择，不保存业务 ID，不提供异步加载或动态行高。`RealizedCount` 需要布局后才反映已创建的行数。

普通内容滚动用 `ScrollView`；它保留完整子树。`TreeView` 绘制可见树行，并通过 `ReparentRequested` 请求应用修改模型。两者详见[控件 API](api/controls.md)。

## 模态内容

YoUI 没有独立的 `Modal` 控件类。将遮罩和面板作为根节点的最后一个子树添加，然后用 `UiDocument.SetModal` 限制输入和焦点：

```csharp
using System.Numerics;
using YoUI;
using YoUI.Platform.Windows;

var root = new Element();
var open = root.Add(new Button("Open dialog")
{
    Transform = RectTransform.Fixed(24, 24, 180, 44)
});
var overlay = root.Add(new Element
{
    Transform = RectTransform.Stretch(),
    Background = new Color(0, 0, 0, .6f),
    HitTestVisible = true,
    Visible = false
});
var card = overlay.Add(new StackPanel
{
    Transform = new RectTransform(new Vector2(.5f), new Vector2(.5f),
        new Vector2(.5f), Vector2.Zero, new Vector2(320, 180)),
    Background = Color.Hex(0x1B293B),
    Padding = 24,
    Gap = 16
});
card.Add(new Label("Confirm this action?"));
var close = card.Add(new Button("Close"));

using var window = new DesktopWindow("Dialog", root);
void Close()
{
    window.Document.SetModal(null);
    overlay.Visible = false;
}
open.Clicked += () =>
{
    overlay.Visible = true;
    window.Document.SetModal(overlay);
};
close.Clicked += Close;
window.Document.PreviewInput += e =>
{
    if (overlay.Visible && e.Kind == EventKind.KeyDown && e.Key == 27)
    {
        Close();
        e.Handled = true;
    }
};
window.Run();
```

`SetModal` 不负责显示面板，也不自动响应 Escape 或维护嵌套模态栈。关闭时显式解除输入限制并隐藏内容；可恢复此前仍有效的焦点。

## 自定义绘制

继承 `Element` 并重写 `Paint`，输出 `DrawList` 命令。下例是可直接加入控件树的类型定义：

```csharp
using YoUI;

public sealed class ProgressStrip : Element
{
    private float value;
    public ProgressStrip() => Height = 12;

    public float Value
    {
        get => value;
        set => Set(ref this.value, float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0,
            DirtyFlags.Paint);
    }

    protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme)
    {
        list.Rectangle(bounds, theme.Border.Opacity(opacity), 6);
        list.Rectangle(bounds with { Width = bounds.Width * Value },
            theme.Accent.Opacity(opacity), 6);
    }
}
```

`Paint` 接收到的矩形已包含祖先视觉偏移，`opacity` 已包含继承透明度和禁用状态。保持绘制顺序，裁剪/层调用必须配对，不在控件里创建窗口、GPU 设备或主循环。

需要测量时重写 `Measure`，设置 `DesiredSize`；自定义排列优先重写 `ArrangeChildren`。自定义交互在构造函数中设置 `HitTestVisible / Focusable / Role`，再重写 `OnEvent`。用 `Set` 或 `Invalidate` 声明属性变化，以免缓存帧不更新。

## 加载资源与生成代码

下面是完整的窗口程序；从仓库根目录运行，使用文档附带的资源：

```csharp
using YoUI;
using YoUI.Assets;
using YoUI.Platform.Windows;

var asset = UiAssetJson.Load("docs/examples/welcome.youi.json");
var ui = UiAssetRuntime.Build(asset, command =>
{
    if (command == "app.greet") Console.WriteLine("Hello from the application");
});
var heading = (Label)ui.Nodes["22222222222222222222222222222222"];
heading.Text = "Loaded from JSON";
using var window = new DesktopWindow(asset.Name, ui.Root,
    (int)asset.Root.Width, (int)asset.Root.Height, 480, 280);
window.Document.Theme = asset.DarkTheme ? Theme.Dark : Theme.Light;
window.Run();
```

`UiInstance.Nodes` 的键是资源 ID，不是 `Name`；Name 可以重复。`Build` 生成新树，运行时文本、开关等变化不会反写 `UiAsset`。`Command` 仅为业务名称，当前由 Button 和 Toggle 的点击触发，不执行脚本，也不自动绑定 Slider / TextBox。

在 Editor 选择 **C# + configuration** 后，把导出的 `Screen.g.cs` 加入应用工程，将上面的 `UiAssetRuntime.Build` 改为 `YoUI.Generated.Screen.Create(commandHandler)`。要自动采用资源尺寸和主题，可在自定义宿主中使用生成的 `Screen.CreateDocument(measurer, commandHandler)`；用 `DesktopWindow` 时传 `Create` 返回的根节点并自行设置窗口尺寸和主题。

多个导出文件都定义 `YoUI.Generated.Screen`，合并多张界面时先调整生成类的命名空间或类名，避免重名。生成代码需要 `YoUI`，显示窗口另需平台项目；无需引用 Editor 或导出插件。参见[资源 API](api/assets.md)和[导出 API](api/editor.md)。

## 离屏渲染与资源释放

不创建窗口的路径是 `NativeRenderer → UiDocument → BuildFrame → Render → SavePng`，完整可运行代码在[快速开始](getting-started.md)。也可以直接用 `DrawList` 绘制而不创建 UI 文档，见[渲染 API](api/rendering.md)。

用 `using` 显式释放 GPU 资源，顺序是图像 → 渲染器 → 原生窗口。`DesktopWindow.Dispose` 负责自己的渲染器和窗口，不要单独提前销毁它的 HWND。`UiDocument` 不实现 `IDisposable`，不会替调用者释放文字测量器或外部渲染器。

无变化的 `BuildFrame` 返回同一个缓存 `DrawList`。将它视为借用的只读帧，需保存副本时复制到自己的 `DrawList`；不要清空或追加到文档的缓存帧。
