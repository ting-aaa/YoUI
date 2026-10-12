# A10：外部程序集自定义控件契约

`Element` 的 public/protected API 已支持外部程序集继承和组合。这里用实际独立 DLL 与独立消费者验证这个边界，不引入另一个组件基类、属性存储、事件路由器或插件注册系统。

## 可运行示例

- [`YoUI.Components.Sample`](../samples/YoUI.Components.Sample/RatingControl.cs) 编译为独立类库，只引用 `YoUI`。`RatingControl` 是 0–5 的交互评分条：指针拖动、方向键/Home/End、焦点、语义快照和外部模型同步。
- [`YoUI.Component.Tests`](../tests/YoUI.Component.Tests/Program.cs) 是另一个程序集，通过项目引用消费该 DLL。测试确认三个程序集互异，核心没有 `InternalsVisibleTo`，不复制源码或用反射调用内部成员。
- 两个项目跟随仓库固定工具链和 Debug/Release 配置。组件本身不引用平台项目、原生渲染器或编辑器。测试程序的 GPU 模式通过公共宿主 API 提交组件生成的 IR。

在仓库根目录，先构建匹配配置的原生库，再运行：

```sh
cargo build --workspace --locked --target-dir target
dotnet run --project tests/YoUI.Component.Tests -c Debug
dotnet run --project tests/YoUI.Component.Tests -c Debug -- --gpu
```

Release 对应 `cargo build ... --release` 和 `-c Release`。普通模式仅运行 CPU 契约测试，不创建渲染器；GPU 模式创建离屏渲染器并写 `artifacts/custom-rating.png`。它是绘制提交 smoke，不是逐像素测试或原生窗口验收。完整入口 `scripts/verify.ps1 -Configuration Debug/Release -Layer Logic/Gpu` 已包含这些检查；Linux 逻辑入口也包含独立组件测试。

使用已有宿主时，像普通控件一样组合：

```csharp
var state = new RatingState { Value = 3 }; // 应用所有
var root = new Element();
var rating = root.Add(new RatingControl(state) {
    Transform = RectTransform.Fixed(20, 30, 260, 40)
});
var document = new UiDocument(root, textMeasurer, 300, 100);
// 宿主调用 document.Dispatch、BuildFrame、Semantics。
// 关闭这个视图前：
root.Remove(rating); // 释放模型订阅；state 仍可复用。
```

## 属性与失效

可用 `Set(ref field, value, flags)`，或定义 `static readonly UiProperty<TOwner,TValue>` 并通过 protected `SetProperty` 写字段。必须从描述器的 `DefaultValue` 初始化字段；元数据不会自动分配存储或赋初值。

顺序为规范化/验证 → 比较有效值 → 存储 → 失效 → 同步 changed 回调。同一有效值无通知、无失效。规范化抛出时不修改字段；changed 抛出时已写入的值和待处理工作不回滚。属性回调可再次改值，因此避免循环写入；示例两端都用有效值相等抑制反馈。

- 尺寸、内容测量变化：`Layout`，框架自动扩展为 `Paint | Semantics`。
- 纯颜色/装饰：`Paint`。
- 语义名称/值：`Semantics`；不需要额外重绘。
- 评分值只改变填充比例和语义，使用 `Paint | Semantics`。

描述器不等于编辑器属性注册或 JSON 序列化 schema，也不承诺绑定、动画优先级、二进制稳定性。完整更新与异常重试见 [A09](layout-updates.md)。

## 布局与绘制

布局坐标原点在左上，Y 向下，均为文档逻辑单位。`Measure(available)` 返回期望尺寸并设置 `DesiredSize`；`available` 是约束输入，不是自动裁剪命令。控件决定固有尺寸和换行策略，显式 Width/Height（NaN 表示自动）应优先。Padding 也由控件的测量/绘制实现一致处理。

`Arrange(bounds)` 的基类实现负责保存 Bounds、增加 LayoutRevision，再调用 `ArrangeChildren(content)`。自定义容器优先覆写 `ArrangeChildren`；若覆写 Arrange，必须调用 base 才能保留框架几何。父布局分配的 Bounds 是最终结果。StackPanel 拥有子元素位置与尺寸，不允许同时设置子 RectTransform。宽度影响高度时，容器应在实际宽度确定后重新 Measure。

`Paint(list, bounds, opacity, theme)` 接到的是**已应用祖先及自身 VisualOffset 的绘制矩形**和继承透明度（含 disabled 系数）。通过 `Color.Opacity(opacity)` 应用透明度，不再次偏移。框架处理可见性、父子顺序、ClipToBounds、CompositedOpacity 和子树遍历；覆写 Paint 只画自身，必要时调用 base 绘制 Background。额外 PushClip/BeginLayer 必须成对结束。不要 Clear/保留/异步修改宿主 DrawList，不改变透明绘制顺序。只提交成功返回的 BuildFrame；其命令区跨后续构帧复用。

`VisualBounds` 用于文档坐标的输入；布局 Bounds 不含视觉偏移。资源句柄必须来自实际提交此帧的 renderer，不能跨 renderer 共享；Paint 不应创建/销毁渲染器或拥有主循环。

## 输入与语义

`HitTestVisible` 决定是否作为指针目标；Focusable 决定焦点候选；父不可见/禁用的子树不参与输入。命中按前后遮挡和裁剪处理。框架维护焦点、hover、单指针 capture、Capture/Target/Bubble 路由。

公开 Event 先于 protected OnEvent；OnEvent 只在 Target 阶段且尚未 Handled 时执行。设 Handled 阻止后续路由。PointerDown 中设置 CapturePointer 后，后续移动可在边界之外送达；按控件自身规则处理 PointerUp、PointerCancel、Blur，并清除临时交互状态。需要消费的按键设置 Handled。Dispatch 使用冻结路径，事件期间移除节点不截断已经建立的路由路径；不要假设 OnEvent 时仍附属于原文档。

Hover 在移除时清除，重新加入不继承旧 hover。禁用/隐藏后输入活跃性由后续 Dispatch 校正，不是属性 setter 同步发 Blur 的保证。平台键盘/IME/触摸的支持范围不因自定义控件扩大。

通过 Role、AccessibleName、SemanticValue 提供语义。当前 Semantics 导出可见且启用子树中 Focusable 或 Role=`text` 的元素；任意非可聚焦角色不会自动导出。快照包含视觉坐标矩形，不负责平台辅助技术、语义动作调用或裁剪可见区域。动态改变 protected Role/Focusable 时派生类要主动请求相应语义/绘制更新；它们不是带自动失效的属性描述器。

## 生命周期、所有权与限制

树和属性只在一个 UI 线程操作。Add 不接受已属于父节点或文档的节点，也拒绝祖先循环。跨文档移动必须先 Remove 再 Add。Remove/Clear 是**可复用的分离**，不会 Dispose 组件、外部模型、共享 renderer、纹理或任意 IDisposable 子对象。

`OnDocumentChanged(previous,current)` 是订阅外部事件的扩展点：Document 已更新，正常路径先递归通知子节点再通知父节点。加入未附着的树不会触发文档变更；文档根附着时才触发。分离期间清除焦点/capture，然后节点所属文档转为 null。回调应短小、同步、无异常，不在其中再次改树；这是通知，不是带回滚的事务。用户回调抛出或重入树变化不承诺原子附着/分离、自动重试或精确一次通知。

示例在附着时双向订阅，再以模型当前值初始化；同步回调读取最新有效值，不回放较早订阅者已修正的旧参数。分离时解绑两端并清除交互状态。分离后的控件编辑不会修改模型，重新附着时模型胜出。对象不是一次性视图，不能把 Remove 当作永久销毁。测试多次跨文档重挂和外部模型存活时的视图 GC。

当前 Element 和 UiDocument 没有通用 Dispose/根解除挂接 API。应用应使用一个普通宿主 root，关闭视图时 Remove/Clear 订阅型子树，再按自己的所有权顺序释放 native resources。若把订阅型控件直接作为文档根，其订阅生命周期需要应用自己额外管理；本示例刻意使用普通根容器。自定义组件可以实现显式 IDisposable，但框架不会自动调用，也不会拒绝已被自定义 Dispose 的对象再次加入；这部分必须由组件自身约定。借用的模型/渲染器不得在分离时擅自释放。

## 范围

A10 无新增公共 API，仅修正移除控件时遗留 IsHovered 的输入状态，并以外部编译与契约测试固定已有扩展边界。没有控件 DLL 动态加载、编辑器自定义组件面板、资产类型注册、插件沙箱/热卸载、原生 ABI 变化或 A06 平台依赖。软件 GPU、硬件 GPU、Windows 原生窗口必须分别报告。
