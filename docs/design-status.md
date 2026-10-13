# 分享设计对照与后续实现

来源：[跨平台 UI 框架设计方案](https://chatgpt.com/share/6aa4e629-ec40-83e8-b299-0e60c6978b22)。下表区分已实现能力和完整设计，避免把可运行演示当作整个框架已产品化。

| 领域 | 结论 | 当前证据与边界 |
|---|---|---|
| Rust 渲染核心、C# UI | 符合 | 独立 Rust crate、C ABI、C# UI；本机 DX12/Vulkan/OpenGL 真实输出、像素与原生窗口检查通过 |
| 核心不依赖 UI / 不拥有主循环 | 符合 | 三个无窗口 Rust 示例；C# 宿主驱动 |
| 有序绘制、相邻合批、颜色规范 | 符合当前特性集 | GPU 像素检查；线性预乘混合；不全局排序 |
| 场景节点与生命周期 | 部分符合 | 代际 ID、父子关系、可见性、共享 mesh、变换与删除；未实现通用组件生命周期 |
| 图像、文字、网格 | 部分符合 | RGBA 上传、图集、塑形、平色/纹理三角形与九宫格（[A06 合约](a06-textured-mesh.md)）；未实现路径/完整 SVG、透视纹理或逐顶点颜色；A06 不继承历史硬件验收结论 |
| 裁剪与合成 | 部分符合 | 嵌套矩形裁剪、隔离层/整体透明度；无 stencil、软遮罩、滤镜和背景模糊 |
| Render Graph / 扩展片段 | 部分符合 | 有序层依赖树；无通用资源依赖与 custom pass 注册 |
| RectTransform 与布局控制权 | 部分符合 | 锚点、轴心、拉伸、StackPanel 冲突检查；坐标改为 Y 向下，无完整布局组件库 |
| 现代控件与主题 | 部分符合 | Label/Button/Toggle/Slider/TextBox/VirtualList/Modal，以及可复用 TreeView/ScrollView/PropertyGrid；无 Tab 容器和声明式绑定系统 |
| 输入、焦点、命中 | 符合当前桌面子集 | 逻辑测试与原生窗口消息检查；尚无触摸/笔/多指针 |
| 文字编辑 / IME | 部分符合 | Unicode grapheme、shaped 几何、点击/拖选、键盘选择、剪贴板、撤销、预编辑/提交桥接；多行编辑和真实输入法候选行为仍需后续 |
| 虚拟列表 | 部分符合 | 十万数据只实例化视口行；定高、同步数据，无动态高度/异步取消 |
| 无障碍 | 部分符合 | 语义树导出；尚未接入平台辅助技术 |
| 资源管理 | 部分符合 | checked handles、释放后拒绝访问、图集与缓冲复用；有[显式宿主重建约定](renderer-recovery.md)；无异步资产图、热重载和透明设备恢复 |
| 渲染能力查询 | 有限实现 | [已创建 renderer 的只读快照](render-capabilities.md)：实际后端、设备类型与有效尺寸 / 实例缓冲上限；无创建前支持探测或完整 feature-bit 清单，不保证资源准入或平台支持 |
| Spine / Live2D | 未实现 | 没有包含 SDK 或可运行模型适配，不以空接口作为支持证据 |
| 跨平台宿主与发布 | 部分验证 / 未实现 | Windows x64 历史 DX12/Vulkan/OpenGL 验收；Linux 支持构建/逻辑与显式离屏验收流程，SDL2/X11 实验宿主已有特定历史提交的 Release 虚拟 X11 原始 23 项检查通过，当前基线与新增 WM 最小化/恢复检查未运行（见下文）；不代表 Linux 硬件或物理输入验收；macOS、移动、Web 未验收，ANGLE 仅可选实验 |
| 编辑器与 UI 文件 | 部分符合 | 同库自举 Editor，层级/画布/属性/历史/保存打开/运行时预览，版本 1 JSON 和可编译 C# 导出；没有多选、模板、时间轴和资产面板 |
| 编辑器导出插件 | 符合当前基础范围 | 独立 IUiExporter 契约、GUI/CLI 加载本地 DLL、真实示例插件导出；不提供插件控件、沙箱、热卸载或私有依赖解析 |
| 性能稳定性 | 部分符合 | 可配置 warm CPU 逐帧指标、计数器与保留资源快照，复用和像素检查；有注入失败/重建测试；无 GPU 时间戳、长时 soak 或真实设备丢失验收 |

## 建议的后续里程碑

1. **P0 风险验证**：选定 Spine/Cubism 版本及可合法分发的素材；在基础纹理网格之上实现软遮罩和受控 custom pass；对照官方输出验证模型遮罩 + 父裁剪 + 交错透明层。获取 SDK 后再实现真实适配。
2. **P1 渲染补齐**：局部离屏层、纹理预算/驱逐、矢量路径与抗锯齿、设备恢复；现有能力查询仅覆盖身份与有限上限，完整后端 feature-set 检查仍需后续。
3. **P2 UI 完整化**：布局依赖与四阶段测量、Grid/Fitter/ScrollRect、多行编辑与视觉 bidi 导航、动态高度虚拟列表、平台无障碍、数据绑定。
4. **跨平台验收**：依次接入 macOS/Linux 宿主和字体，之后再做移动/Web 的输入、生命周期及发布适配。wgpu 后端可用不代表 UI 平台已验收。
5. **制作工具与稳定性**：在已实现的版本化资源、自举 Editor、配置/代码/插件导出基础上，补充多选、模板/资产、增量预览同步、长时间压测与 API 稳定化。

Spine/Cubism 的运行时、素材版本与再分发条件需在实际引入时根据官方材料确认；本仓库没有打包这些第三方 SDK，也没有据此对产品发布授权作结论。

渲染器 GPU payload 分配准入与保留容量快照见 [A07 资源预算](resource-budgets.md)；它不等同于总显存预算或自动回收。

文字命中、UTF-8/UTF-16 边界、caret affinity 与单行输入范围见 [A08 文字输入](text-input.md)。

[A09 布局失效与更新边界](layout-updates.md)：待处理阶段、有限次收敛、重入拒绝与失败重试；保留整棵可见树布局。

[A10 外部自定义控件](custom-components.md)：独立程序集评分控件、公共 API 消费者契约测试、文档附着订阅和绘制 IR；不含动态控件插件加载或通用 Dispose 生命周期。

[A12 CPU 帧分析](profiling.md)补齐可配置预热/采样、每帧现有计数器和资源快照导出；[离屏平台验收](platform-acceptance.md)区分 Linux/Windows 宿主、实际后端与软件/硬件适配器。该离屏流程不是原生窗口验收。

[Linux X11 实验宿主](linux-window.md)的历史 [GitHub Release 运行](https://github.com/ting-aaa/YoUI/actions/runs/38223249352)在提交 `59154396a30abb1a131c490a4bb708c6817a1a5f` 上通过原始 23 项检查，环境为 Xvfb/Openbox、合成 XTest 输入、SDL 2.30 和软件 Vulkan（llvmpipe）。这是该提交与配置的虚拟 X11 窗口呈现/输入证据；原始 unmap/remap 仅验证隐藏/重显，不证明窗口管理器最小化/恢复。

合入文字修复后的基线 `41d896eb9bee7bda281c5ef7d2c56740100dce89` 不继承上述原生验收结果；新增 `--require-wm` 原生检查为 **NOT RUN（未运行）**。该可选模式通过有时限的 Xlib worker 观察同一窗口的 ICCCM `WM_STATE` 从 `NormalState` 到 `IconicState` 再到 `NormalState`，同时检查 X map state、宿主挂起/帧数稳定/恢复和恢复后点击；[Openbox CI](x11-ci.md)要求此模式及其证据，默认无 WM 的 unmap/remap 路径保持不变。这是有界的验收补充，不扩大 Alpha 要求。

实现环境仍因 AF_UNIX socket 创建被拒绝而不能执行原生检查；本次不绕过限制，也不授权新的远端调度。宿主仍为实验状态，第二桌面平台的完整验收未完成；不据此宣称硬件 GPU、物理键鼠、原生 Wayland、IME 或平台无障碍支持。
