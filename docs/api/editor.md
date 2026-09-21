# 编辑器与导出 API

[API 索引](README.md) · [Editor 操作指南](../editor.md) · [资源 API](assets.md)

项目：`managed/YoUI.Editor.Core`，命名空间：`YoUI.Editor`。源码：[EditorSession.cs](../../managed/YoUI.Editor.Core/EditorSession.cs)、[Exporting.cs](../../managed/YoUI.Editor.Core/Exporting.cs)。该层的模型编辑、保存和导出无需创建原生窗口或 GPU。

## EditorSession

`EditorSession(UiAsset? asset = null)`。null 使用 Starter；传入资源先校验并记录初始已保存状态。管理单文档和单选，不自动弹出未保存确认框。

| 属性 / 事件 | 语义 |
| --- | --- |
| `UiAsset Asset { get; }` | 当前可变资源对象；业务修改应走 Edit |
| `string SelectedId { get; }` | 当前选中节点 ID，初始为根 |
| `UiNode Selected { get; }` | 根据当前 SelectedId 查询节点 |
| `string? FilePath { get; }` | 打开/保存后的绝对路径，初始 null |
| `bool IsDirty { get; }` | 当前序列化结果与保存基线比较；读取会序列化，不是常数时间字段 |
| `bool CanUndo / CanRedo / InTransaction { get; }` | 历史与事务状态 |
| `event Action<bool>? Changed` | true 表示模型改变；false 为选择/保存等状态通知 |

### 编辑与历史

| 方法 | 语义 |
| --- | --- |
| `UiNode Find(string id)` | 找到当前资源节点；不存在抛 InvalidOperationException |
| `UiNode? ParentOf(string id)` | 找到直接父节点；根或不存在的 ID 返回 null |
| `void Select(string id)` | 校验存在后选择，变化时 Changed(false) |
| `void Edit(Action<UiAsset> change)` | 在深拷贝上执行修改、验证；成功且内容变化后替换 Asset 并记录历史 |
| `void UpdateSelected(Action<UiNode> change)` | 使用 Edit 修改所选节点 |
| `void SetAnchor(string id, AnchorMode anchor, Rect parentContent)` | 切换父锚点，保留显示矩形，原子补偿位置和拉伸边距 |
| `void SetPivot(string id, Vector2 pivot, Rect parentContent)` | 切换自身定位点，保留显示矩形并补偿 X/Y；Stretch 禁用 |
| `void BeginTransaction()` | 开始合并多个编辑；不允许嵌套 |
| `void EndTransaction(bool cancel = false)` | 提交为一个历史步骤；cancel=true 恢复开始快照；无事务时无操作 |
| `void Undo() / Redo()` | 先结束当前事务，再恢复快照；无历史时无操作 |

历史栈各保留最多 100 步。编辑失败不会替换活动资源，但回调对文件或其他外部状态产生的副作用不在回滚范围内。不要直接更改 Asset、Selected 或 Find 返回的节点，否则会绕过历史、校验和通知。

每次编辑和恢复都可能替换模型对象，不长期缓存 UiNode 引用；保存 ID，使用时重新 Find。

SetAnchor/SetPivot 的 parentContent 必须是同一时刻运行时解析的父 Bounds.Inset(Padding)，使用画板坐标而非编辑器缩放后的窗口坐标。它们拒绝根节点和 Stack 直接子节点。直接通过 Edit 修改 Anchor/Pivot 不会自动补偿坐标，交互编辑应使用上述方法。

### 层级操作

| 方法 | 语义 |
| --- | --- |
| `void Add(NodeKind kind)` | 添加到选中容器或选中非容器的父级；选中新节点；拒绝 Canvas |
| `void Delete()` | 删除选中子树并选中父节点；根无操作 |
| `void Duplicate()` | 深拷贝选中子树，生成新 ID；插入其后，非 Stack 子项位置增加 16；根无操作 |
| `void Move(int direction)` | 按参数符号移动一个兄弟位置，到边界停止；0 不移动 |
| `void Reparent(string id, string parentId)` | 移到新容器末尾，保留局部布局参数；拒绝环、根移动和非容器目标 |

Reparent 不保证屏幕位置不变；锚点参数在新父级重新解析。同一个父级无操作；与 Rust Scene.reparent 的行为不同。

### 文件与初始资源

| 方法 | 语义 |
| --- | --- |
| `void New()` | 替换为 Starter，清空历史、路径和脏状态 |
| `void Open(string path)` | 先加载验证，成功后替换资源并重置历史、选择与保存基线 |
| `void Save(string? path = null)` | 先结束事务；使用 path 或 FilePath；均无则抛 InvalidOperationException |
| `static UiAsset Starter()` | 返回内置欢迎界面的新资源 |

New/Open 不提示未保存，调用方应结合 IsDirty 决定流程。Save 在同目录创建临时文件，写完后替换目标；父目录必须存在。文件 I/O 失败以异常返回；保存成功才更新 FilePath 和保存基线。

### 事务式编辑示例

完整无窗口程序，引用 YoUI.Editor.Core：

```csharp
using YoUI.Assets;
using YoUI.Editor;

var session = new EditorSession(UiAssetJson.Load("docs/examples/welcome.youi.json"));
session.Select("33333333333333333333333333333333");
session.BeginTransaction();
try
{
    session.UpdateSelected(node => node.X += 8);
    session.UpdateSelected(node => node.Text = "Continue");
    session.EndTransaction();
}
catch
{
    session.EndTransaction(cancel: true);
    throw;
}
Directory.CreateDirectory("artifacts");
session.Save("artifacts/edited-welcome.youi.json");
Console.WriteLine($"Saved {session.FilePath}; can undo: {session.CanUndo}");
```

## 导出契约

| 类型 | 成员 |
| --- | --- |
| `sealed record ExportContext(UiAsset Asset)` | 当前导出的资源快照 |
| `sealed record ExportFile(string RelativePath, string Content)` | 一个相对路径的 UTF-8 文本产物 |
| `interface IUiExporter` | `string Id { get; }`、`string DisplayName { get; }`、`IReadOnlyList<ExportFile> Export(ExportContext context)` |

导出器返回文件内容，由宿主验证和落盘。契约没有二进制文件、异步调用、取消或进度接口。插件是进程内代码，宿主提供的资源副本仅避免意外改动当前模型，不是安全沙箱。

## ExportCatalog

`new ExportCatalog()` 默认注册 JsonExporter 和 CSharpExporter。

| 成员 | 语义 |
| --- | --- |
| `IReadOnlyList<IUiExporter> Exporters { get; }` | 当前导出器只读视图 |
| `IReadOnlyList<IUiExporter> LoadPlugin(string assemblyPath)` | 加载本地程序集，实例化公开的非抽象 IUiExporter 类型；返回新导出器 |
| `static void Write(IUiExporter exporter, UiAsset asset, string directory)` | 克隆资源、调用 Export、验证文件、暂存并提交到新目录 |

LoadPlugin 要求公开无参构造函数。Id 和 DisplayName 非空，ID 在同一个 Catalog 内忽略大小写必须唯一。没有自动发现、私有依赖解析器、热卸载或隔离运行；加载/构造异常会传回调用方。

Write 要求：

- directory 尚不存在，已有文件或目录均拒绝，抛 IOException。
- 产物数量为 1..64，总 UTF-8 字节数 ≤8 MiB。
- 相对路径非空、无重复（忽略大小写并统一斜线），不得为绝对路径或含冒号。
- 路径段不得是空串、`.`、`..`，不得以空格或点结尾；解析后必须仍在输出根目录内。
- 内容非 null，最终按 UTF-8 无 BOM 写出。操作系统仍可因无效文件名、权限等拒绝写入。

宿主先写同级暂存目录，再重命名为目标目录。插件抛异常或返回非法产物时不提交目标目录。直接调用 exporter.Export 不执行目录、安全边界和总产物大小检查；通常使用 Write。

## 内置导出器与生成接口

| 导出器 | Id | 文件 |
| --- | --- | --- |
| `JsonExporter` | `youi.json` | `screen.youi.json` |
| `CSharpExporter` | `youi.csharp` | `Screen.g.cs`、`screen.youi.json`、`README.md` |

两者均实现 IUiExporter，并提供无参构造函数。生成代码直接实例化控件、设置属性、连接命令。其入口固定为：

```text
YoUI.Generated.Screen.Create(Action<string>? command = null) -> UiInstance
YoUI.Generated.Screen.CreateDocument(ITextMeasurer measurer,
    Action<string>? command = null) -> UiDocument
```

CreateDocument 使用导出时的画板宽高和 DarkTheme。Create 每次返回独立树；同一树不能重复附着。生成文件不包含应用入口或项目文件，需要加入引用 YoUI 的工程。导出多张界面到同一项目时处理固定类名冲突。

## 无窗口命令行

先执行 `./scripts/build.ps1 -Configuration Release`。下面创建唯一的新导出目录，可重复运行：

```powershell
$exportPath = Join-Path 'artifacts' ('export-' + [guid]::NewGuid().ToString('N'))
dotnet run --project tools/YoUI.Editor -c Release --no-build -- --open docs/examples/welcome.youi.json --exporter youi.csharp --export $exportPath
```

| 参数 | 意义 |
| --- | --- |
| `--open <file>` | 先加载资源；省略时用内置欢迎界面 |
| `--plugin <dll>` | 加载一个本地导出插件程序集 |
| `--exporter <id>` | 选择导出格式，默认 youi.json |
| `--export <new-directory>` | 写入新目录后结束，不创建 GPU 或窗口 |
| `--headless` | 没有 --export 时执行 Editor GPU 截图流程 |
| `--smoke` | 没有 --export/--headless 时执行原生窗口输入检查 |

当前 CLI 每个命名参数只读取第一次出现的值；没有 --help 或多插件列表参数。常规桌面脚本提供 `-Open` 和 `-Plugin`；导出等高级开关传给上面的可执行程序。

## 插件示例

类库引用 `managed/YoUI.Editor.Core/YoUI.Editor.Core.csproj`，目标 net9.0：

```csharp
using YoUI.Editor;

public sealed class NameExporter : IUiExporter
{
    public string Id => "example.name";
    public string DisplayName => "Document name";

    public IReadOnlyList<ExportFile> Export(ExportContext context)
        => [new ExportFile("name.txt", context.Asset.Name)];
}
```

完整项目见 [YoUI.Exporter.Sample](../../samples/YoUI.Exporter.Sample/YoUI.Exporter.Sample.csproj)。使用它：

```powershell
$exportPath = Join-Path 'artifacts' ('inventory-' + [guid]::NewGuid().ToString('N'))
dotnet run --project tools/YoUI.Editor -c Release --no-build -- --open docs/examples/welcome.youi.json --plugin samples/YoUI.Exporter.Sample/bin/Release/net9.0/YoUI.Exporter.Sample.dll --exporter sample.inventory --export $exportPath
```

此插件扩展文件格式，不注册新控件类型、Inspector 面板或动画运行时。
