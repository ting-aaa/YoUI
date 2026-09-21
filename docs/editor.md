# YoUI Editor

[文档首页](README.md) · [快速开始](getting-started.md) · [资源格式与 API](api/assets.md) · [编辑器与导出 API](api/editor.md)

Editor 按“先 UI 库，再自举”的顺序实现：树视图、滚动容器、属性表单、按钮和输入框都来自 `managed/YoUI`，Editor 自身也是一棵 YoUI 控件树。Windows 层只提供窗口、输入和文件对话框，图形仍由独立 Rust 渲染器完成。

## 启动与操作

```powershell
./scripts/editor.ps1
./scripts/editor.ps1 -Open ./docs/examples/welcome.youi.json
./scripts/editor.ps1 -Plugin ./samples/YoUI.Exporter.Sample/bin/Release/net9.0/YoUI.Exporter.Sample.dll
```

脚本构建后启动。再次构建前关闭对应配置的应用窗口，避免 Windows 锁定输出 DLL。也可以直接启动 `tools/YoUI.Editor/bin/Release/net9.0/YoUI.Editor.exe`。

| 区域 | 操作 |
| --- | --- |
| Components | 搜索并添加 Panel、Text、Button、Toggle、Slider、Input、V Stack、H Stack；选中容器时添加到容器内部，否则添加到同级 |
| Layers | 单击选中，箭头展开/折叠；拖到容器行重新挂接；底部 ↑/↓ 改变绘制/布局顺序，Dup 复制子树，Del 删除 |
| Canvas | 单击选中，拖动移动，右下角手柄缩放；空白区拖动平移；滚轮或 +/- 缩放，Fit 复位；Snap 切换 8 像素吸附 |
| Project | 浏览当前目录的 `.youi.json` 文件，搜索后选中文件并点击 Open selected；打开已有文件后浏览其所在目录；刷新按钮重新扫描；打开前检查未保存内容 |
| Resources | 显示当前文档的 Colors、Fonts 和 Commands 引用；展开后点击组件名称定位到使用者 |
| Inspector | 名称/数值同行，按 Layout、Content、Appearance、Typography、Behavior、Visibility、Node 分组；点击组标题折叠，搜索框过滤属性；布尔值显示勾选状态；Enter 或失焦提交，非法输入在状态栏显示原因 |
| Preview | 运行真实控件树，支持按钮事件、开关、滑块、文本、剪贴板和 Tab；退出后清除交互产生的临时状态 |
| Save / Open | 保存和打开 `.youi.json`；新建、打开和关闭前检查未保存变更；无效文件不会替换当前文档 |
| Export | 先点击右下角导出目标切换格式，再点击顶部 Export，输入一个**尚不存在的输出文件夹名称**；导出不会覆盖既有文件夹 |
| Load plugin | 选择已构建的本地导出器 DLL，加载后加入导出目标列表 |

快捷键：Ctrl+N/O/S，新建/打开/保存；Ctrl+Shift+S 另存；Ctrl+Z/Y 或 Ctrl+Shift+Z 撤销/重做；Ctrl+D 复制；Delete 删除；F5 切换预览；Esc 退出预览或取消拖动。画布获得焦点时方向键移动 1 像素，Shift+方向键移动 8 像素。文本框内的撤销优先由文本编辑处理。

检视布局参考 [Godot Inspector](https://docs.godotengine.org/en/stable/tutorials/editor/inspector_dock.html) 的分组、折叠、搜索和属性行设计。Parent anchor 与 Local pivot 为两个独立九宫格：前者选择父内容区参考点，后者选择组件自身定位点。点击任一预设会保持当前显示矩形不变，自动补偿 X/Y，并作为一次可撤销操作保存。例如父锚点为左上、组件 `(X,Y)=(0,0)`，将 Local pivot 从左上改为左下后得到 `(0,Height)`，显示位置不动。

Stretch 的 X/Y 为左/上边距，Width/Height 为右/下边距，允许负边距以保留超出父级的组件。切换固定锚点与 Stretch 同样保留显示位置和尺寸；Stretch 下 Local pivot 不参与边距布局并禁用。Stack 控制子节点的位置，Inspector 禁用其锚点和定位点，允许编辑尺寸、Grow 和顺序。Stretch 和 Stack 子节点通过属性调整布局。重新挂接仍保留局部布局参数，不保证显示位置不变。

Project 是本地 UI 文件浏览器，最多扫描 6 层、500 个目录和 500 个 UI 文件，跳过构建目录和目录链接；Resources 是当前文档中已有样式/命令的引用视图，尚不提供图片、字体文件导入或项目清单管理。窗口最小支持 1100×760。

## 分层与运行时资源

```text
managed/YoUI
  Element / layout / controls / TreeView / ScrollView / PropertyGrid
  Assets: UiAsset, UiNode, UiAssetJson, UiAssetRuntime, UiInstance
         ↑
managed/YoUI.Editor.Core
  EditorSession: 事务、历史、层级操作、原子保存
  IUiExporter / ExportCatalog / JsonExporter / CSharpExporter
         ↑
tools/YoUI.Editor
  EditorShell / EditorCanvas: 工作区、视口变换、输入路由
  Preview → UiAssetRuntime → UiDocument → DrawList
         ↓
managed/YoUI.Platform.Windows → Rust C ABI → wgpu DX12 / Vulkan / OpenGL
```

资源格式版本为 1，包含稳定节点 ID、类型、布局、样式、初始值和命令名称。控件实例、解析后的 Bounds、选择状态、视口、撤销历史与 GPU 资源不序列化。加载器拒绝缺失版本/根节点、未知版本/字段/枚举、重复 ID、无效结构/颜色/数值以及过大的资源。

当前限制：加载文件最多 4 MiB、1000 节点、最大深度 24（根深度为 0）、64–4096 逻辑单位画板。内存字符串长度与文件字节数的区别见[资源校验规则](api/assets.md#版本-1-校验规则)。历史最多 100 步，一次拖动为一步。保存采用同目录临时文件再替换；导出采用新暂存目录写完后重命名为目标目录。导出器收到文档副本，不能通过修改副本改变活动文档。

业务程序只引用 UI 运行库即可加载配置：

```csharp
using YoUI;
using YoUI.Assets;
using YoUI.Platform.Windows;

var asset = UiAssetJson.Load("screen.youi.json");
var ui = UiAssetRuntime.Build(asset, command =>
{
    if (command == "project.create") Console.WriteLine("Create requested");
});
using var window = new DesktopWindow(asset.Name, ui.Root,
    (int)asset.Root.Width, (int)asset.Root.Height);
window.Document.Theme = asset.DarkTheme ? Theme.Dark : Theme.Light;
window.Run();
```

原生 DLL 的构建、复制与发布要求和 Showcase 相同。`UiInstance.Nodes` 通过稳定 ID 查找实际控件，也可以遍历控件的 Name 对接业务逻辑。配置中的 Command 仅为名称，不执行脚本。

## 配置与代码导出

- **UI configuration**：`screen.youi.json`，可直接交给 `UiAssetJson.Load` 和 `UiAssetRuntime.Build`。
- **C# + configuration**：`Screen.g.cs`、同一配置和使用说明。生成代码直接创建 Label/Button/StackPanel 等实际控件，设置布局/样式并连接命令回调；没有嵌入 JSON 解释器或 Editor 依赖。
- **Plugin · Component inventory**：示例插件导出 `inventory.md`，列出节点类型与业务命令。

生成代码的入口是 `YoUI.Generated.Screen.Create(commandHandler)`，返回 `UiInstance`；或使用 `CreateDocument(measurer, commandHandler)`。每次 Create 生成独立控件树，可分别附着到不同窗口。重新导出会生成一个新目录，可由应用工程选择合并文件。

命令行导出不创建窗口或 GPU 设备，适合构建流水线：

```powershell
dotnet run --project tools/YoUI.Editor -c Release --no-build -- --open ./docs/examples/welcome.youi.json --exporter youi.csharp --export ./artifacts/export-csharp
dotnet run --project tools/YoUI.Editor -c Release --no-build -- --plugin ./samples/YoUI.Exporter.Sample/bin/Release/net9.0/YoUI.Exporter.Sample.dll --exporter sample.inventory --export ./artifacts/export-inventory
```

## 编写导出插件

插件是 .NET 9 类库，引用 `managed/YoUI.Editor.Core/YoUI.Editor.Core.csproj`，包含一个公开、非抽象、具有无参构造函数的 `IUiExporter` 实现。完整可构建示例见 `samples/YoUI.Exporter.Sample`。

```csharp
using YoUI.Editor;

public sealed class MyExporter : IUiExporter
{
    public string Id => "mycompany.layout";
    public string DisplayName => "My layout format";

    public IReadOnlyList<ExportFile> Export(ExportContext context)
        => [new("layout.txt", context.Asset.Name)];
}
```

ID 在同一个 ExportCatalog 中忽略大小写必须唯一。插件返回 1–64 个 UTF-8 文本文件，总大小最多 8 MiB；文件名必须是相对路径，不得越过目标目录、重复或覆盖既有导出目录。界面和命令行使用同一个导出接口。

当前插件在进程内执行，加载的是受信任本地代码，不提供沙箱、超时隔离、自动发现、热卸载或私有依赖解析器。示例仅依赖宿主共享契约。更复杂插件需与宿主依赖版本兼容；后续可扩展为独立进程导出服务。导出插件扩展的是**格式**，当前不注册新的控件类型或 Inspector 工具。

## 验收与边界

`scripts/verify.ps1` 包含 Editor 核心测试、真实 GPU 截图与 Win32 输入验收。测试还生成 C# 源码、创建只引用运行库的临时工程、编译运行，并比较配置和生成代码的完整绘制命令、UTF-8 内容以及事件绑定。

当前提供单文档、单选、基础矩形/文字/控件编辑。未实现多选、吸附参考线、任意旋转、图片资产面板、动画时间轴、控件模板/组件实例、声明式数据绑定、插件面板或 macOS/Linux 宿主。编辑变更会重建预览控件树；空闲帧缓存且窗口等待消息，但大型文档编辑尚无帧率承诺。中文直接输入/粘贴已在窗口验证，真实输入法候选窗口定位及高 DPI 多显示器仍需专门验收。
