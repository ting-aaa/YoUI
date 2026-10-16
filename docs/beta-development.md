# Beta 开发与验收计划

基线：本地 `origin/main` 的 Alpha 修复提交 `e336d6e6ec8881764d5b3a8053e152f791ddb581`。
日期：2026-10-17。远端刷新失败，最新远端状态未确认。本文是开发合约，不是发行声明。

## 阶段定位

沿用 [Alpha 合约](alpha-acceptance.md)：Alpha 完成渲染能力与少量基础组件；
Beta 扩充组件数量与框架能力；1.0 再完成更广泛的 UI 能力。
本轮以 Windows 桌面为主要验收环境，保留独立 Rust/wgpu 渲染、C# 控件层、
宿主驱动循环、同库原生编辑器和透明绘制顺序。

## 工作包与完成定义

| ID | 工作包 | 交付与验收 |
| --- | --- | --- |
| B01 | 基线与兼容 | 从修复后的 Alpha 隔离开发，保留 v1 文件、公开 API、ABI；现有门禁继续运行 |
| B02 | 内容与列表组合 | ContentControl/Presenter、Button 内容槽、稳定键 ItemsControl；重排保持实例/焦点，移除释放绑定 |
| B03 | 常用组件 | CheckBox、RadioButton/Group、ComboBox、NumericUpDown、ProgressBar、TabControl、Separator、只读 DataGrid；指针、键盘、禁用、边界值和状态通知 |
| B04 | 弹层与交互 | 独立于父裁剪的 Popup、视口约束、Escape/外部点击关闭、焦点恢复、所有者退出清理；ComboBox 复用 |
| B05 | 布局 | Auto/Fixed/Percent、Min/Max、Margin/Alignment、Grid 固定/Auto/Star 和跨格、Wrap；拒绝非法数值与控制权冲突 |
| B06 | 应用机制 | 显式可释放绑定、命令/CanExecute、双向值、文本校验；错误路径与订阅退出可验证 |
| B07 | 声明式制作 | Beta 组件的版本化文件、验证、运行时、编辑器面板、属性、保存重开、撤销与 C# 导出保持一致 |
| B08 | 参考程序 | 可运行组件示例和表单场景；真实 renderer 输出、紧凑窗口和主题截图；自动行为回归 |
| B09 | 消费与打包 | 本地预发行 NuGet、匹配 native 库、独立消费者；不依赖编辑器即可使用运行时 |
| B10 | 验收与审查 | Debug/Release Logic、Gpu、Window 分层证据，检查图片、独立审查并修复；记录失败和未运行项 |

实施顺序：B01 → B02/B03/B04 与 B05/B06 并行 → B07/B08 → B09/B10。
以完成定义推进，不虚构工期。任何新增范围先记录进本表再实现。

## 明确的能力边界

DataGrid 本轮为固定行高、只读、稳定行键、排序/筛选/选择；编辑单元格、TreeGrid、
Dock 和日期控件属于后续迭代。ItemsControl 与应用绑定由 C# 业务配置，文件不序列化
委托或业务对象。Beta 文件沿用有界数据、安全路径和资源归属约定。

动画 SDK 深化、完整制作工具重构、Prefab/时间轴、动态高度异步虚拟列表、复杂文本
软换行/完整 bidi、完整样式模板、平台无障碍、多指针和新增平台验收继续按 1.0 路线。
这些项目不因为本轮完成组件而自动得到实现或验证结论。

## 验收记录规则

每次运行记录源 SHA、dirty 状态与补丁、配置、工具链、实际 adapter/backend、退出码和
产物路径。原生先于托管构建，Debug/Release 库配对；修改输入/布局后运行
`scripts/verify.ps1`，图片必须实际查看。Logic、真实 GPU、原生窗口、物理输入/IME 和
平台支持分别记录；历史日志不能补作本轮结果。系统鼠标移动验收需记录用户保持鼠标
空闲的条件。正式 tag、发布和资产上传另有发行步骤。

## 当前进度

B01–B10 已完成实现、独立审查修复及冻结源码后的本地验收。
受测产品提交为 `90e86add65c6ffd24f73dca7a30874176d887474`：干净源码的 Debug/Release
All 与两个配置的独立包消费者均通过；逐项交付、真实 GPU/窗口证据及运行入口见
[Beta 验收报告](beta-acceptance.md) 和 [独立审查回执](beta-review.md)。
首次 SDK 路径、编排断言、截图可见问题及旧冻结提交的独立审查失败记录保留。
后续收尾仅含文档与项目记忆，不改变受测产品源码；当前结论不等于已经公开发行。
