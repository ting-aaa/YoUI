# Beta 组件与框架使用指南

[开发与验收计划](beta-development.md) · [控件 API](api/controls.md) · [编辑器](editor.md)

Beta 在 Alpha 渲染基础上扩充原生 C# 控件和应用机制。入口：

```powershell
./scripts/build.ps1 -Configuration Debug
dotnet run --project samples/YoUI.Showcase -c Debug --no-build -- --beta
# 离屏真实 GPU 及由 UiDocument 分发的交互检查
dotnet run --project samples/YoUI.Showcase -c Debug --no-build -- --beta --headless
```

`--beta --smoke` 使用 Windows 原生窗口、PostMessage 及显式模型/焦点更新，输出
`beta-window.json`；它不代表物理键鼠或真实 IME。图片遵循 `YOUI_ARTIFACT_DIR`。

## 组件

| 类型 | 公共入口与行为 |
| --- | --- |
| `CheckBox` | `Label`、`Value`、`Changed`；指针和 Space/Enter 切换 |
| `RadioGroup` / `RadioButton` | 显式组所有权、`Selected` / `Value`、变更事件；方向键组内选择；先提交互斥再通知 |
| `ComboBox` | `SetItems`、`SelectedIndex`（-1 表示无选择）、`SelectionChanged`、`SetOpen` / `IsOpen`；弹层、方向键和 Escape |
| `NumericUpDown` | 有限 `Minimum` / `Maximum` / 正 `Step` / `Value`、`ValueChanged`、`Input`；输入提交、上下键、步进与钳制 |
| `ProgressBar` | `Value` 为 0–1；拒绝非有限值；只读进度与语义 |
| `TabControl` | `AddPage`、`SelectedIndex`、`PageCount`、`SelectionChanged`；保留页面实例，非选中页不参与布局/输入/绘制；方向键/Home/End |
| `Separator` | 分隔图元；支持显式尺寸和颜色 |
| `DataGrid` | `SetData`、`Sort`、`Filter`、`SelectedKey`、`SelectionChanged`、`RowCount` / `VisibleRowCount`、`ScrollOffset`；固定行高、只读、稳定行键；只绘制视口行 |

DataGrid 不提供单元格编辑、可变行高或异步数据源。控件发出的语义数据仍未接入平台辅助技术。
长生命周期 RadioGroup 的成员退出时设置 `Group = null`，释放组对该成员的所有权。

## 内容和集合组合

`ContentControl` / `ContentPresenter` 提供可替换内容槽。`Button.Content` 允许复合视觉内容，
`Caption` 和 `Text` 入口继续可用。内容必须先从旧父节点脱离，不能同时属于两个文档。

`ItemsControl<T>` 使用 `Key`、`ItemTemplate`、`BindItem`、`ReleaseItem` 和 `SetItems`。
稳定键重排保留视图实例、文档附着和焦点；删除项或失败的候选视图释放应用拥有的订阅。
这是非虚拟化组合容器，大数据继续使用 `VirtualList`。应用回调对业务对象的副作用不回滚。

## 布局

`Element.Layout` 接受不可变 `BoxLayout`，与 `RectTransform` 互斥。它表达 Auto/Fixed/Percent、
四边 Margin、Min/Max、水平/垂直 Stretch/Start/Center/End。百分比基于父内容区扣除边距后的
可用尺寸；流式主轴只解析一次百分比。数值有限、非负、有界，非法定义明确拒绝。

```csharp
var grid = new Grid {
    Rows = [GridTrack.Auto, GridTrack.Star()],
    Columns = [GridTrack.Fixed(120), GridTrack.Star(2)], Gap = 8
};
var title = grid.Add(new Label("Settings")); grid.SetCell(title, 0, 0, 1, 2);
var input = grid.Add(new TextBox()); grid.SetCell(input, 1, 1);
input.Layout = new() { MinWidth = 80, Margin = new Thickness(4) };
```

Grid 的 Auto 为内容预留空间，Star 按权重分配剩余空间，支持行列跨度。子项移除后重新加入时
保留仍然有效的单元格设置；若期间行列缩减使旧位置或跨度越界，则回到默认的首行首列单格。
已附着子项占用的行列不能被直接删减。WrapPanel 按有限
可用宽度换行，隐藏项不占位。容器拥有子项位置时应去除子项 RectTransform。旧 StackPanel
与锚点入口保留；本轮未实现通用布局依赖图或动态高度虚拟列表。

## 绑定、命令与校验

绑定在创建 UI 的线程上运行，业务拥有 `BindingScope`，退出页面/窗口时显式 Dispose。
每个绑定还返回独立 `IDisposable`。作用域释放尝试所有清理；初始同步失败回滚订阅，
重入更新有界，后台结果必须调度回 UI 线程。

```csharp
using var bindings = new BindingScope();
var name = new ObservableValue<string>("Aurora");
var input = new TextBox();
bindings.BindText(input, name, value => string.IsNullOrWhiteSpace(value) ? "Required" : null);
var save = new UiCommand(_ => Save(name.Value), _ => name.Value.Length > 0);
bindings.BindCommand(new Button("Save"), save);
// 影响 CanExecute 的业务状态变化后调用 save.Refresh()。
```

支持单向 Observe/Bind、双向文本/Toggle/CheckBox/数值/选择绑定。验证失败保留编辑文本并
阻止写入模型；模型驱动更新和业务自己的校验通知是显式契约。没有反射路径绑定、
跨线程自动同步或文件中的委托序列化。
模型到控件的成功写入会按控件的最终有效值规范化模型，例如数字范围、单行文本和
VirtualList 选择范围；回调写入更新的模型值时保留更新。ComboBox 越界索引仍明确拒绝。

## 弹层

`UiDocument.ShowPopup(owner, content, preferredSize, closed)` 返回 `PopupHandle`。
内容是文档独立拥有的 overlay，绘制和命中位于根树之后，脱离父裁剪并限制在视口中。
外部点击/Escape/Dispose 关闭、恢复仍有效的原焦点；所有者脱离、隐藏、禁用或模态作用域
变化会清理弹层。关闭和错误路径尝试释放所有关联状态。模态节点仍必须来自文档根树。

## UI 文件与原生制作

v1 文件继续加载并保持原绘制。新增类型、`beta` 设置或 `layout` 使用 v2；在编辑器中添加
新组件会在同一可撤销事务中升级版本。v1 不接受混入的 Beta 数据，未知字段和无效数据
继续拒绝，保留 4 MiB / 1000 节点 / 24 层等预算。

新增 CheckBox、RadioButton、ComboBox、NumericUpDown、ProgressBar、TabControl、Separator、
DataGrid、Content、Grid、Wrap，以及既有 VirtualList/TreeView/ScrollView 的声明式节点。
v2 Button 允许一个内容子节点，制作复合按钮；添加内容和 v1→v2 升级可一并撤销。
组件搜索与可滚动面板让所有类型可创建；属性表提供内容、选择、数值、Grid 跨格与 BoxLayout。
复杂集合采用受验证的 JSON 属性，尚不是完整资产导入或可视化表格编辑器。

文件与生成的 C# 共用布局和组件配置契约。导出直接构造运行时控件，不依赖 Editor 或
插件程序集；回归测试实际编译生成代码并比较完整绘制命令、UTF-8 文本和命令行为。
绑定委托、业务对象和 `ItemsControl<T>` 模板由消费者代码提供。

本轮验收范围与实际结果分别见 [Beta 计划](beta-development.md) 和验收记录；动画、完整
Editor/Prefab/时间轴、其他平台、真实 IME、无障碍和长期稳定性不从这些检查推导。
