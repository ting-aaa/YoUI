# 控件 API

[API 索引](README.md) · [Element 和输入](core.md) · [使用指南](../guide.md)

命名空间：`YoUI`。源码：[Controls.cs](../../managed/YoUI/Controls.cs)、[AuthoringControls.cs](../../managed/YoUI/AuthoringControls.cs)。以下类型继承 Element（Toggle 继承 Button，PropertyGrid 继承 ScrollView），通用属性见[基类](core.md#element)。

## Label

`Label(string value = "")`；密封类，Role 为 `text`，默认不可聚焦、不可命中。

| 成员 | 默认 / 行为 |
| --- | --- |
| `string Text` | 构造参数；setter 将 null 转为空串，变化时使布局和语义失效 |
| `float FontSize` | 14；setter 要求有限非负，实际原生绘制范围为 1..512 物理像素 |
| `Color? Color` | null 时跟随 Theme.Text |
| `bool Bold` | false |
| `override string SemanticValue` | Text |
| `override Vector2 Measure(Vector2 available)` | 通过 Document.TextMeasurer 按可用宽度测量 |

没有附着文档时测量只提供占位结果，不是真实字形宽度。构造参数不会自动设置 AccessibleName；需要明确语义名称时显式赋值。

## Button

`Button(string text, bool focusable = true)`。默认 Height=42、Radius=9、HitTestVisible=true，Role 为 `button`。

| 成员 | 行为 |
| --- | --- |
| `Label Caption { get; }` | 实际文字子节点，可设置 FontSize / Bold |
| `string Text` | 更新 Caption.Text 和 AccessibleName |
| `Color? TextColor` | null 时按主题、Primary、Selected 选择 |
| `bool Selected` | 默认 false，选择样式，不是切换值 |
| `bool Primary` | 默认 false，主按钮样式 |
| `event Action? Clicked` | 指针按下后在按钮内松开，或焦点上 Enter / Space 时触发 |
| `override Measure(Vector2 available)` | 文字宽度加内边距；可用显式 Width/Height 覆盖 |

按下请求捕获，取消或失焦重置按压态。Background 的 alpha 大于零时作为常规底色；悬停、按压和焦点状态仍可改变最终绘制。Button 可派生，扩展时保留默认事件处理所需的基类调用。

## Toggle

`sealed class Toggle : Button`，构造 `Toggle(string label, bool initial = false)`，Role 为 `switch`。

- `bool Value`：变化时更新 ON/OFF 文案与 Selected，并触发 `event Action<bool>? Changed`。
- `SemanticValue` 返回 `"on"` / `"off"`。
- 点击先通过内部 Clicked 订阅切换 Value；程序赋不同 Value 也触发 Changed。
- ON/OFF 文案基于构造时的 label，后续直接改 Text 不会替换内部 label。

## Slider

`Slider()`，默认 Height=28、Value=0.5，支持命中和焦点，Role 为 `slider`。

| 成员 | 行为 |
| --- | --- |
| `float Value` | 正常有限输入钳制到 0..1；调用方应排除 NaN |
| `event Action<float>? Changed` | 实际值改变时触发，包括程序赋值 |
| `SemanticValue` | 不变区域格式的整数百分比 |

拖动改变值，左右键每次减少/增加 0.05。没有 Min/Max/Step 属性；业务范围需自行映射，例如 `volume = slider.Value * 100`。

## TextBox

`TextBox()`，默认 Height=44、ClipToBounds=true，可命中和聚焦，Role 为 `textbox`。

| 成员 | 行为 |
| --- | --- |
| `string Placeholder { get; init; }` | 默认 `"Search…"`，空文本时显示 |
| `string Text` | 默认空串；程序设置不同文本将光标移到末尾、清除预编辑和撤销/重做历史 |
| `string SelectedText { get; }` | 当前选择范围文字 |
| `event Action<string>? Changed` | 编辑、撤销、重做或程序赋不同文本时触发 |
| `SemanticValue` | Text |

| 输入 | 行为 |
| --- | --- |
| 左/右、Home/End | 按字素边界或到行首/行尾移动 |
| Shift + 移动键 | 扩展选择 |
| Backspace / Delete | 删除选区或相邻字素 |
| Ctrl+A/C/X/V | 全选、复制、剪切、粘贴；后三者依赖 Document.Clipboard |
| Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z | 撤销 / 重做，最多 100 个编辑历史 |
| TextInput / Composition | 已提交文字 / 临时预编辑文字 |
| PointerDown | 光标和选择锚点移到文本末尾 |

输入事件拒绝控制字符，粘贴会过滤控制字符；Text setter 本身不会过滤换行，应用仍应传单行文本。没有公开光标位置、密码、多行、鼠标拖选 API；字体大小目前固定在内部绘制中。真实 IME 候选定位的验收边界见[验证记录](../verification.md)。

## VirtualList

`VirtualList()`，Role 为 `listbox`，列表自身可聚焦，内部复用的按钮行不可 Tab 聚焦。

| 成员 | 默认 / 行为 |
| --- | --- |
| `float RowHeight { get; init; }` | 54；需有限，使用大于 6 的值以容纳行间距和内容 |
| `Func<int,string> ItemText` | 默认 `i => $"Item {i}"`，同步索引到文字映射 |
| `int ItemCount` | 默认 0，非负；赋值使布局失效并钳制现有选择 |
| `int SelectedIndex` | 默认 -1，表示未选择；钳制到 -1..ItemCount-1 |
| `float ScrollOffset` | 默认 0，DIP；要求有限并钳制到可滚动范围 |
| `int RealizedCount { get; }` | 当前复用行节点数量 |
| `int FirstVisibleIndex { get; }` | `floor(ScrollOffset / RowHeight)` |
| `event Action<int>? SelectionChanged` | 通过 SelectedIndex 改变选择时触发 |
| `void Refresh()` | 使布局失效以重读数据 |
| `SemanticValue` | 选择位置与总数，如 `"1 of 100"` |

布局分配约可见行数加 3 个缓冲行；第一个复用行可能在 FirstVisibleIndex 之前。RowHeight 的显式检查为至少 1，但当前内部行高度为 RowHeight−6，因此不要传小于 6 的值。

上下键改变选择，Home/End 到首尾，并保持选中项可见；直接赋 SelectedIndex 不自动滚到该行。ItemCount 收缩可直接钳制内部选择而不触发 SelectionChanged，应用更新数据时同步自己的选择状态。

数据同数量但内容变更后调用 Refresh。不要手动增删该控件的内部子节点；虚拟化池管理它们。没有自定义行模板、动态高度或稳定 ID 绑定。

## ScrollView

`class ScrollView : Element`，构造 `ScrollView(Element content)`，将 content 作为唯一内容节点附着；默认矩形裁剪和命中。

| 成员 | 行为 |
| --- | --- |
| `Element Content { get; }` | 传入的内容节点 |
| `float ScrollOffset { get; }` | 当前纵向滚动偏移 |
| `void ScrollTo(float value)` | 非有限值按 0 处理，然后钳制到内容范围 |
| `void ScrollIntoView(Element element)` | 依布局 Bounds 调整滚动；传入 Content 的后代，并先完成布局 |
| `override Measure(Vector2 available)` | 直接返回 available；本实现不按自身 Width/Height 做自动测量 |

滚动写入 Content.VisualOffset，不改变其逻辑布局。不要同时用这个 VisualOffset 做其他动画。滚轮每格 48 DIP；无横向滚动和内容虚拟化。需要限制视口时由父级给出有限最终矩形，例如自由布局容器中的 Transform。

## TreeItem 与 TreeView

`sealed record TreeItem(string Id, string Label, string Detail, bool Container, IReadOnlyList<TreeItem> Children)`。Id 应唯一；Container 表示是否可作为拖放目标。当前绘制使用 Label，Detail 保留在模型中。

`TreeView()`，Role 为 `tree`，可聚焦、命中并裁剪。

| 成员 | 行为 |
| --- | --- |
| `float RowHeight { get; init; }` | 默认 32；调用方提供有限正值，建议保留可读行高 |
| `bool AllowReparent` | 默认 false |
| `string? SelectedId` | 程序设置仅更新状态/重绘，不触发 SelectionChanged |
| `void SetItems(IReadOnlyList<TreeItem> value)` | 替换树数据并重建展平行；沿用折叠 ID 集合 |
| `event Action<string>? SelectionChanged` | 用户选择节点时触发 |
| `event Action<string,string>? ReparentRequested` | 参数为 sourceId、targetContainerId；仅发出请求 |

上下键选择，左右键折叠/展开，点击箭头切换展开，滚轮纵向滚动。控件绘制视口附近的行，展平数据仍保留在内存中。宿主负责校验重挂操作、修改模型并重新 SetItems。当前指针定位按 Bounds 计算，带额外祖先 VisualOffset 的嵌套使用需自行验证。

## PropertyEntry 与 PropertyGrid

`sealed record PropertyEntry(string Name, Func<string> Read, Action<string> Write, bool Editable = true, IReadOnlyList<string>? Choices = null)`。

`PropertyGrid()` 是基于 ScrollView 的属性表单：

| 成员 | 行为 |
| --- | --- |
| `void SetEntries(IEnumerable<PropertyEntry> entries)` | 按 Group 重建分组表单；保留组折叠状态和滚动位置；Choices 显示可展开的明确选项，false/true 显示勾选状态，其余为 TextBox |
| `string Filter` | 按分组、属性名和 Hint 过滤；搜索自动展开匹配分组，清除后恢复折叠状态 |
| `void RefreshValues()` | 重新读取模型值；不覆盖当前获得焦点的文本框 |
| `event Action<string>? ValidationFailed` | 已捕获的写入异常，以 `"属性名: 错误"` 返回 |

文本在 Enter 或失焦时调用 Write；选项点击后立即调用 Write。当前捕获 ArgumentException、InvalidDataException、FormatException、OverflowException。**Write 必须先验证再修改，或使用 EditorSession 的事务式修改**；PropertyGrid 不会替任意回调回滚已发生的写入。

PropertyEntry 还支持 `Group`、`Hint` 和 `Func<Element>? CreateEditor`。自定义编辑器实现 `IPropertyEditor.RefreshValue()` 后随模型同步；`ReferencePicker` 提供九宫格参考点及可选 Stretch 的直接选择。实现位于 `managed/YoUI/PropertyGrid.cs`。

可用的属性定义示例：

```csharp
using System.Globalization;
using YoUI;

float padding = 12;
var grid = new PropertyGrid();
grid.SetEntries([
    new PropertyEntry("Padding",
        () => padding.ToString(CultureInfo.InvariantCulture),
        value =>
        {
            float next = float.Parse(value, CultureInfo.InvariantCulture);
            if (!float.IsFinite(next) || next < 0 || next > 100)
                throw new ArgumentOutOfRangeException(nameof(value), "Use 0..100.");
            padding = next;
        })
]);
grid.ValidationFailed += Console.WriteLine;
```
