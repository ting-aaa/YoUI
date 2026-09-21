# 资源 API 与文件格式

[API 索引](README.md) · [Editor 指南](../editor.md) · [编辑器与导出 API](editor.md)

项目：`managed/YoUI`，命名空间：`YoUI.Assets`。源码：[UiAsset.cs](../../managed/YoUI/Assets/UiAsset.cs)。资源解析与控件树构造不创建 GPU 设备，运行时无需引用 Editor。

## UiAsset

`sealed class UiAsset`，使用默认构造函数和可读写属性：

| 属性 | 类型 / 默认值 | JSON 名称 |
| --- | --- | --- |
| `Version` | int，1 | `version`，必填 |
| `Name` | string，`"Untitled"` | `name` |
| `DarkTheme` | bool，true | `darkTheme` |
| `Root` | UiNode，默认 Canvas | `root`，必填 |

默认 Root 名为 Artboard，宽高 640×480，Fill=`"#111827"`，Clip=true。资源只保存制作数据，不含解析后的 Bounds、真实控件实例、选择状态、历史记录或 GPU 资源。

## NodeKind 和 AnchorMode

| NodeKind | 运行时控件 | 可有资源子节点 |
| --- | --- | --- |
| `Canvas` | Element（仅允许根节点） | 是 |
| `Panel` | Element | 是 |
| `Text` | Label | 否 |
| `Button` | Button | 否 |
| `Toggle` | Toggle | 否 |
| `Slider` | Slider | 否 |
| `TextBox` | TextBox | 否 |
| `VerticalStack` | StackPanel | 是 |
| `HorizontalStack` | StackPanel { Horizontal=true } | 是 |

Button 的 Caption 是运行时内部节点，不写入资源 Children。TreeView、VirtualList、ScrollView、PropertyGrid、自定义控件和图像尚无此格式的 NodeKind。

`AnchorMode`：

| 值 | X / Y | Width / Height |
| --- | --- | --- |
| `TopLeft`（默认） | 相对父内容区左上角位置 | 固定宽高 |
| `Center` | 相对父内容区中心的偏移 | 固定宽高，轴心在中心 |
| `TopCenter`、`TopRight`、`CenterLeft`、`CenterRight`、`BottomLeft`、`BottomCenter`、`BottomRight` | 相对父内容区对应参考点的偏移 | 固定宽高；默认轴心与参考点对应，可独立覆盖 |
| `Stretch` | 左 / 上边距 | **右 / 下边距** |

Stack 的直接子节点忽略 Anchor/X/Y，以 Width/Height/Grow 和兄弟顺序布局。Canvas 根必须为 TopLeft；显示时根大小由文档/窗口决定，因此宿主应使用资源 Root.Width/Height 初始化尺寸。

## UiNode

`sealed class UiNode`。以下属性全部可读写；JSON 使用 camelCase：

| C# 属性 | 类型 / 默认值 | 用途 |
| --- | --- | --- |
| `Id` | string，自动生成 GUID N 格式 | 稳定节点 ID；反序列化必填 |
| `Kind` | NodeKind.Panel | 类型；反序列化必填 |
| `Name` | string，`"Node"` | 编辑名称；TextBox 也将其用作 Placeholder |
| `Text` | string，空串 | Text/Button/Toggle/TextBox 的文字 |
| `X`、`Y` | float，0 | 位置或边距 |
| `Width`、`Height` | float，160 / 48 | 尺寸或 Stretch 的右/下边距 |
| `Anchor` | AnchorMode.TopLeft | 锚点模式 |
| `PivotX`、`PivotY` | float?，null | 独立定位点，范围 0..1；null 按 Anchor 推导，兼容已有版本 1 文件；Stretch 时忽略 |
| `Fill` | string，`"#00000000"` | 基础背景；并非所有派生控件都绘制 Background |
| `Foreground` | string，空串 | Text/Button/Toggle 的文字色；空串跟随主题 |
| `Radius` | float，8 | 基础背景圆角 |
| `FontSize` | float，16 | Text/Button/Toggle 的字号 |
| `Opacity` | float，1 | 映射到 InheritedOpacity，不是 CompositedOpacity |
| `Padding` | float，0 | 内边距 |
| `Gap` | float，12 | Stack 间距 |
| `Grow` | float，0 | Stack 子项伸展权重 |
| `Bold` | bool，false | Text/Button/Toggle 粗体 |
| `Primary` | bool，false | Button/Toggle 主按钮样式 |
| `Visible`、`Enabled` | bool，true | 可见和启用状态 |
| `Clip` | bool，false | 矩形裁剪 |
| `Checked` | bool，false | Toggle 初始值 |
| `Value` | float，0.5 | Slider 初始值 |
| `Command` | string，空串 | Button/Toggle 点击时传递的业务名称 |
| `Children` | List<UiNode>，空列表 | 按绘制/布局顺序排列的子节点 |

派生只读成员不参与 JSON：

- `bool IsContainer`：Canvas / Panel / VerticalStack / HorizontalStack。
- `bool IsStack`：VerticalStack / HorizontalStack。
- `RectTransform Transform`：根据 Anchor 计算变换参数，尚未解析为 Bounds。
- `IEnumerable<UiNode> DescendantsAndSelf()`：深度优先遍历自身及后代。

属性通用存储不意味着每个控件都实现该样式：例如 TextBox 的字号和前景色由当前控件实现决定，Slider 也使用自身绘制。Node 上没有数据绑定表达式或事件脚本。

## UiAssetJson

静态方法：

| 方法 | 行为 / 返回值 |
| --- | --- |
| `string Serialize(UiAsset asset)` | 先验证，再生成缩进 JSON，最后检查字符串长度 |
| `UiAsset Parse(string json)` | 检查长度，严格反序列化并验证 |
| `UiAsset Load(string path)` | 检查文件字节数，读取文本，再调用 Parse |
| `void Validate(UiAsset asset)` | 验证当前对象模型；不创建控件和 GPU |
| `Color ParseColor(string value)` | 接受 `#RRGGBB` 或 `#RRGGBBAA`，最后两位为 alpha |

JSON 属性名为 camelCase，枚举写为名称，如 `"VerticalStack"`；不接受整数枚举或未知字段。`version/root` 和每个节点的 `id/kind` 是必填字段，其余缺失时保留模型默认值。

验证失败通常抛 InvalidDataException；JSON 结构、必填项、未知字段或枚举解析也可能抛 JsonException；文件系统错误保留对应 IOException / UnauthorizedAccessException 等。调用方不要把所有加载失败都当作文件不存在。

### 版本 1 校验规则

| 项目 | 规则 |
| --- | --- |
| 版本与根 | Version=1；唯一根必须是 Canvas；子节点不能是 Canvas |
| ID | 每个节点都是不带连字符的 32 位 GUID（N 格式），整个资源中唯一 |
| 数量 / 深度 | 最多 1000 个节点；根深度=0，节点深度不得超过 24 |
| 画板 | Width/Height 在 64..4096；TopLeft、Visible=true、Enabled=true |
| 浮点数 | X/Y/Width/Height/Radius/FontSize/Opacity/Padding/Gap/Grow/Value 均有限，绝对值 ≤32768 |
| 非负值 | Radius/Padding/Gap/Grow 非负；固定尺寸和 Stack 子节点 Width/Height 非负；Stretch 的右/下边距可为负 |
| 定位点 | PivotX/PivotY 为 null 或 0..1 的有限数 |
| 字号 | FontSize 在 1..256；这是资源格式限制，独立原生渲染范围不同 |
| 归一化值 | Opacity 和 Value 在 0..1 |
| 字符串 | 资源名、节点 Name、Command 最长 128；Text 最长 16384；不允许 null 或 NUL |
| 颜色 | Fill 必须为合法颜色；Foreground 可空或合法颜色 |
| 子树 | Children 不能为 null，只有容器可带孩子 |
| 大小 | Load 文件上限 4 MiB；Parse/Serialize 实际按 .NET string.Length 上限 4×1024×1024 检查 |

最后一项区分字节与 UTF-16 长度：大量非 ASCII 字符生成的 JSON 可能通过 Serialize 的长度检查，却在写出后超过 Load 的文件字节限制。交换文件时同时控制 UTF-8 文件大小。Validate 只校验模型结构，不检查序列化后的大小。

## 最小资源

[完整可打开示例](../examples/welcome.youi.json)包含一个标题和按钮。以下是仅带一个按钮的合法最小示例：

```json
{
  "version": 1,
  "name": "Example",
  "root": {
    "id": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    "kind": "Canvas",
    "width": 640,
    "height": 360,
    "children": [
      {
        "id": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        "kind": "Button",
        "name": "Continue",
        "text": "Continue",
        "x": 24,
        "y": 24,
        "width": 180,
        "height": 44,
        "primary": true,
        "command": "app.continue"
      }
    ]
  }
}
```

## UiAssetRuntime

`UiInstance Build(UiAsset asset, Action<string>? command = null)`：

1. 验证模型。
2. 构造独立控件树并应用初始属性。
3. 将资源 ID 映射到控件。
4. 为非空 Command 的 Button/Toggle 连接点击回调。

command 可空；为空时控件仍能交互，但没有应用命令处理。它不捕获业务回调异常。Build 不把 DarkTheme 自动保存在 UiInstance 中，宿主须显式选择主题。

`void Apply(Element e, UiNode n, bool stackChild, bool root = false)` 是低层属性映射：设置背景、圆角、内边距、继承透明度、裁剪、可见/启用和 Grow；Stack 子项设置宽高；非根自由节点设置 Transform。它不负责创建类型、递归孩子、绑定命令或独立校验完整资源，也不是能清除旧布局状态的通用热更新函数。通常使用 Build。

## UiInstance

`sealed record UiInstance(Element Root, IReadOnlyDictionary<string,Element> Nodes)`。

`UiDocument CreateDocument(ITextMeasurer measurer, float width, float height, bool darkTheme = true)` 将现有 Root 附着到新文档并选择主题。它不是克隆：同一 UiInstance 不能连续附着到多个文档。每个窗口调用一次 Build 或生成代码的 Create。

通过 Nodes 的资源 ID 找到类型并订阅 Changed，或更新 Text 等业务状态。运行时控件变化不会同步回 UiAsset；需要保存制作数据时使用 EditorSession 修改资源模型。

完整加载窗口程序见[使用指南](../guide.md#加载资源与生成代码)。
