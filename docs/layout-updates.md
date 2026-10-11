# A09：布局失效与更新边界

这一层保留现有 `Measure / Arrange / ArrangeChildren` 扩展点和整棵可见树布局。
它提供明确的更新阶段、有限次布局收敛、可观察的待处理状态与失败重试；不是子树布局缓存、四阶段依赖图、Grid 或后台调度器。

## 更新契约

- `Element.Invalidate(Layout)` 同时请求 `Paint | Semantics`，因为布局会改变绘制和语义矩形。
- 节点及祖先聚合脏标记，所属文档只接收一次通知。连续修改会合并到下一次阶段执行；属性有效值相同不会请求新工作。
- `UiDocument.PendingUpdates` 表示文档尚未消费的阶段。`NeedsRender` 只检查 Layout/Paint；仅改无障碍名称不应让宿主重建绘制命令。
- `EnsureLayout()` 最多执行一次布局。`BuildFrame()` 和 `Semantics()` 也保留最多一次前置布局的行为；如果回调继续使布局失效，调用返回后仍可能存在 Layout 位。
- `UpdateLayout(int maxPasses = 8)` 只执行布局，直到稳定或达到预算。返回 true 表示没有待处理 Layout，false 表示仍需后续处理；不会清除剩余工作或偷偷增加预算。参数必须大于零。计数只统计成功完成的布局；回调异常仍向上传播。
- `BuildFrame()` 消费 Paint，复用同一个 DrawList。`Semantics()` 消费 Semantics 并重新导出快照；BuildFrame 不再清除 Semantics。语义消费者应看 PendingUpdates，而不是 NeedsRender。
- 在同一文档的布局、绘制或语义回调内调用 EnsureLayout、UpdateLayout、BuildFrame、Semantics 或 Dispatch 会抛出 `InvalidOperationException`，即使当前没有相应脏位。允许回调修改属性/调用 Invalidate；独立文档可单独更新。

自定义宿主可在需要稳定几何时显式收敛，例如：

```csharp
// 在 UI 线程、更新回调之外调用。应用选择如何处理尚未收敛的情况。
if (!document.UpdateLayout(maxPasses: 8))
{
    Console.WriteLine("布局仍在变化；稍后重试，不在本次继续循环。");
    return;
}
if (document.NeedsRender)
{
    DrawList frame = document.BuildFrame();
    // 同步提交 frame；不要修改或跨后续 BuildFrame 保存其内容快照。
}
if ((document.PendingUpdates & DirtyFlags.Semantics) != 0)
{
    IReadOnlyList<SemanticNode> snapshot = document.Semantics();
    // 交给应用自己的语义消费者；这里没有平台辅助技术适配。
}
```

这不保证随后 Paint / SemanticValue 回调不再更改布局。每次调用后仍可检查 PendingUpdates。
框架不自动反复更新到固定点，避免不收敛的自定义控件卡住 UI 线程。

## 消费、异常和树变化

每个阶段在**任何用户回调之前**消费当前树对应的节点脏位和文档脏位。Measure 中、覆写 Arrange 调用 base 之前、较早兄弟的 Paint 中引发的新失效都保留到后续更新；不会因后来访问该节点而丢失。

阶段失败时恢复已消费的节点工作并重新请求文档阶段，保留其他新失效。恢复按节点**当前**的父链和所属文档传播：回调中移走的节点不会错误绑定到旧文档。恢复工作采用可复用列表，并在成功和异常出口清除节点引用。此前已完成的阶段不回滚，属性、树、Bounds 和部分绘制列表也不回滚；修正回调后重试。只有成功返回的 BuildFrame 结果可提交。

隐藏子树的旧脏标记也会被阶段消费，但不执行隐藏节点的布局或绘制。重新显示或添加子树会请求完整布局。已分离节点上的修改只在其当前父链累计，不唤醒原文档；加入新文档后走正常更新。

这不是任意树修改的事务保障。回调直接修改正在枚举的 Children 可能令当前阶段失败；该阶段仍可重试。树结构、属性和更新必须由同一个 UI 线程操作。

`Element.Dirty` 是阶段待处理诊断，不是缓存有效性保证或“这个节点一定尚未绘制”的声明。独立调用 Measure/Arrange 不消费文档阶段；LayoutRevision 继续统计基类 Arrange 的调用。文档级 Theme/Resize/模态变化可直接使文档失效，因此文档的 PendingUpdates 是宿主调度的依据。

## 回归与范围

逻辑回归覆盖连续百次编辑合并、无操作稳定性、颜色与语义更新不重排、有限次收敛和不收敛、Measure/Arrange/兄弟 Paint 的再次失效、全部阶段的嵌套更新拒绝与失败重试、语义快照中的再次失效、隐藏/显示、分离/跨文档重挂，以及失败回调中跨文档移动后的工作恢复。

完整门禁入口仍为 `scripts/verify.ps1 -Configuration Debug/Release -Layer Logic/Gpu`。软件 GPU、硬件 GPU 和 Windows 原生窗口必须分别报告；本功能没有改变原生 ABI，也没有引入 A06 平台宿主依赖。
