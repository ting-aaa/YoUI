# Alpha 验收清单：渲染与基础 Demo

[文档首页](README.md) · [组件图库](component-gallery.md) · [分层验证](verification-layers.md) · [设计对照](design-status.md)

**状态：初版 `v0.1.0-alpha.1` 已发布，随后本机复审发现缺陷。** Tag 对应
`7a42be921bea384facf3bb417de4a23747a39d23`。原 tag 的 Debug / Release
完整本机验收未通过；本次修复从该 tag 独立建立，不覆盖已发布 tag。
本页记录约定范围与发行后复审，不因此扩大平台支持或推进 Beta。

## 里程碑边界

- **Alpha：渲染能力和可运行的基础 Demo。** 本次补充现有图库的第四个
  Rendering 页面，把已经存在的图像、纹理网格、九宫格、裁剪和嵌套整体透明度
  放进同一个可检查的示例，不新增一套组件体系。
- **Beta：更多组件。** 当前已有的控件、Editor 和外部组件示例可以继续作为回归
  场景；它们不把“完整控件库”变成 Alpha 的新增门槛。
- **1.0：足够完整的 UI 能力。** [设计对照](design-status.md)中的长期路线图
  不全部提升为本轮 Alpha 要求。
- 初版 Alpha tag 已建立。本次仅修复其审查与测试问题，修复提交的本机证据
  与原 tag 分开记录；补丁 tag 或二进制发行需要另行发布。

## 已有实现合约

下表的“已有”只描述源码能力。是否在某一提交、配置、适配器或窗口上通过，
必须另看同次运行的证据。

| 范围 | 已有合约 | 对应文档 / 验证关注点 |
| --- | --- | --- |
| 独立渲染与提交 | Rust renderer 不依赖 C# 控件或场景树；宿主拥有循环；C ABI 同步接收命令和 UTF-8 缓冲 | [架构](architecture.md)、[原生 API](api/native.md)；匹配原生库、命令校验、独立示例 |
| 绘制顺序与颜色 | 保留透明绘制顺序，只合并相邻兼容操作；straight-alpha sRGB 输入，线性预乘混合 | [架构](architecture.md)；重叠像素和 alpha，不能按纹理全局重排 |
| 图像、几何与文字 | RGBA 图像、平色 / 纹理三角形、纹理网格、九宫格；共源文字塑形 / 测量和绘制 | [A06](a06-textured-mesh.md)、[文字驻留](text-residency.md)、[A08](text-input.md)；UV、边缘过滤、九宫格缩小、文字边缘 / 裁剪和失效图像 |
| 裁剪与合成 | 嵌套轴对齐矩形裁剪；有序隔离层和嵌套 group opacity | [架构](architecture.md)；子层在原绘制位置合成，整体透明度不等于给每个重叠图元单独乘 alpha |
| 资源与回收 | 创建时 payload 预算、保留容量快照、图像空闲区复用、显式字形驻留 purge、显式离屏层 trim | [资源预算](resource-budgets.md)、[文字驻留](text-residency.md)；拒绝帧不部分提交，保留 live image，重复回收和重建 |
| 渲染诊断 | 已创建 renderer 的实际后端、设备类型和不可变有效上限查询 | [能力查询](render-capabilities.md)；不代表创建前支持探测、剩余容量、资源准入保证或平台验收 |
| 失败与重建 | 有界 surface 重试、设备丢失诊断锁存、显式宿主 stop / dispose / recreate / reupload / rebind | [重建合约](renderer-recovery.md)；旧命令 / 句柄拒绝、文本 provider 更新；不是透明设备恢复 |
| 基础应用层交互 | 原有 Controls / Collection / Layout 页面、主题、页面往返、焦点 / 捕获、模态 Reset、紧凑视口滚动 | [组件图库](component-gallery.md)；由 `UiDocument` 分发的逻辑事件与 OS 输入证据分开记录 |

具体边界继续以各合约为准。例如图集仍是固定单页，purge 不消除碎片或缩小纹理；
trim 不缩小实例缓冲、不保证物理显存立即下降；payload 预算不等于总显存上限；
重建测试不能证明真实硬件设备丢失后可恢复。

能力查询只是对现有渲染器的有界诊断补充，不增加 Alpha 验收门槛。
软件 / Cpu 适配器结果仍须与硬件 GPU、原生窗口和平台证据分开记录；
查询可用不替代完整验收，也不意味着推进 Beta。

## 本次 Rendering 页的有界检查

Rendering 是现有 `--gallery` 中的第四页，复用公开绘制 API 和图库导航。
目标是可复现地展示已有渲染能力，并保住原有基础交互，而不是增加 Beta 控件。

- [ ] 同一页可检查图像、纹理网格、九宫格，以及裁剪内的嵌套整体透明度。
- [ ] 图像资源由示例明确持有，使用与渲染器匹配的有效资源；绘制缓存不能延长
  图像或渲染器的生命周期。
- [ ] 第四页加入页面往返、主题、Reset / Cancel 和紧凑视口回归；切页仍取消旧页
  捕获 / 焦点及模态状态，不破坏前三页的模型、选择或布局行为。
- [ ] 同次运行生成 Rendering 页和紧凑视口截图，并实际检查文字可读性、图像
  边缘、网格 / 九宫格、裁剪边界和重叠透明度。输出文件和入口见[图库文档](component-gallery.md)。
- [ ] 将新增逻辑断言和离屏检查接入现有测试入口，保留失败传播；mock 编排通过
  不能代替真实程序、渲染或截图检查。

这不是软遮罩、滤镜、路径 / 完整 SVG、透视纹理、Spine / Live2D、自定义 pass、
多行编辑、平台无障碍或新增平台支持的交付。本页不为这些能力建立新的 Alpha 门槛。

## 证据记录与当前状态

每次验收要能定位源码 SHA、dirty 状态及对应补丁、配置、工具链、宿主、实际
适配器 / 后端、所选层、退出码、日志和本轮产物。使用仓库已有的
[分层记录](verification-layers.md)；需要独立离屏归档时使用
[平台验收包装器](platform-acceptance.md)。`artifacts/` 内旧文件或存在的二进制
本身不证明当前提交通过。

| 证据类别 | 初版 tag 本机复审记录（2026-10-14） | 可作出的结论 |
| --- | --- | --- |
| 源码 | 干净的 `v0.1.0-alpha.1` 独立副本，SHA 如上 | 与原工作区未提交功能隔离 |
| Debug / Release 构建与 Logic | 两种配置均通过 | 逻辑结果不能抵消 GPU / 生命周期失败 |
| Debug / Release Gpu 与图库离屏截图 | 常规 Demo / 图库通过；图集复用污染失败；DX12 / Vulkan fractional-text oracle 失败 | 完整 `All` 失败；原 tag 不能因部分检查通过而视为通过 |
| Windows 原生窗口 / 输入 | 标准 Editor / Showcase / Gallery smoke 通过；三条错误生命周期实际复现 | Win32 消息测试不等于物理键鼠或真实 IME 验收 |
| Linux X11 / WM 原生验收 | **NOT RUN（本机未运行）** | Windows 上的 Linux host 逻辑检查不等于 X11 / WM 验收 |
| 其他平台、真实 IME、平台无障碍 | 本轮未验收 | 不据此扩大支持范围 |

原 tag 的本机审查归档在 `artifacts/alpha-audit-20261014/`。修复说明见
[Alpha 修复](alpha-fixes.md)，修复工作区的本机结果归档在
`artifacts/fix-validation/RESULTS.md` 与对应 `result.json`，其中记录实际测试
SHA、工作区状态、适配器、配置、退出码和截图。这些本机产物不随源码自动发布。
若仅执行 Release，不把 Debug 也写成通过；若只执行 Logic 或 Gpu，不把
结果称作 `All` 完整验收。

历史 Windows 结果见[验证记录](verification.md)和[后端记录](render-backends.md)。
历史虚拟 X11 结果及当前限制见[Linux X11](linux-window.md)和[X11 CI](x11-ci.md)。
这些是指定旧源码 / 环境的背景证据，不自动覆盖此页或当前 HEAD。第二桌面平台
完整验收仍未完成；WM 最小化 / 恢复是已有有界验收补充，不因此扩大 Alpha 范围。

## 剩余验收与阶段出口

- [ ] 完成本次 Rendering 页及上述基础 Demo 回归。
- [ ] 在最终源码上运行适用的现有构建、Logic / Gpu 门禁，保留真实结果并复核
  本轮图片；失败、阻塞、未运行与通过分别记录，不用旧图片或旧日志补空缺。
- [ ] 逐项核对本轮公开能力声明与证据。若要声明当前提交的原生窗口或特定平台
  通过，补跑对应已有入口；否则明确保留 NOT RUN 和实验性边界，不将离屏
  成功提升为窗口成功，也不新增未经约定的平台要求。
- [ ] 按“渲染 + 基础 Demo”的约定范围完成 Alpha 验收与接受，列明保留的限制。
- [x] 初版 `v0.1.0-alpha.1` tag 已建立。修复提交尚未建立补丁 tag，Beta
  的更多组件工作不属于本次修复范围。

现有完整 Windows 入口仍为 `scripts/verify.ps1 -Configuration <Debug|Release>`；
它不会因为离屏层通过而自动算作已运行。各层、图库窗口 smoke 和 Linux 实验
窗口的运行条件、证据边界保持原文档约定。本清单不引入新的性能数字、后端数量、
长时压力门槛或跨平台完成承诺。
