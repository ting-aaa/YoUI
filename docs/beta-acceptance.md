# Beta 开发完成与本地验收

日期：2026-10-17（Asia/Shanghai）。本轮按“Alpha 完成渲染和少量组件，Beta 扩充组件与框架，1.0 完善更广泛 UI 能力”的既有路线交付。
验收对象是 [B01–B10 开发合约](beta-development.md)，结论限定为 Windows x64 本地 Beta；尚未创建新 tag、GitHub Release 或公开发布包。

## 源码与复验边界

- 开发分支：`codex/beta-components`；独立工作区：`C:\Users\HT\.codex\worktrees\beta-components\YoUI`。原工作区的未提交 1.0 草案没有混入本轮实现。
- 基线：本地缓存的 Alpha 修复提交 `e336d6e6ec8881764d5b3a8053e152f791ddb581`。远端刷新失败，因此不声称已与最新远端同步。
- 最终受测产品提交：`90e86add65c6ffd24f73dca7a30874176d887474`。四条验收命令开始和结束均为同一干净提交，退出码全部为 0。
- 验收之后的收尾仅更新文档和项目记忆；该文档提交不冒充重新运行产品测试。产品源码、测试、样例和构建脚本相对上述受测提交保持不变。

最终驱动记录：[driver-result.json](../artifacts/beta-final/90e86add65c6ffd24f73dca7a30874176d887474/driver-result.json)。
完整独立逐项审查见 [Beta 审查回执](beta-review.md)。

## 合约交付

| ID | 已交付能力 | 实现与验证依据 |
| --- | --- | --- |
| B01 | 隔离 Alpha 基线，保留 v1 文件和既有公开入口，原生 ABI 不变 | v1 文件实际保存/加载、绘制字节与回调对照；未修改 Rust 渲染器和既有平台实现；既有完整门禁、包 ABI 查询通过 |
| B02 | ContentControl/Presenter、Button 内容槽、稳定键 ItemsControl | `Composition.cs`、`Controls.cs`；实例/焦点保留、重排、移除释放、失败路径测试；v2 复合 Button 导出与运行时一致 |
| B03 | CheckBox、RadioButton/Group、ComboBox、NumericUpDown、ProgressBar、TabControl、Separator、只读 DataGrid | `ApplicationControls.cs`；指针/键盘/禁用/数值/通知与虚拟绘制测试；Radio 在 modal 和顶层 popup 中保持导航范围 |
| B04 | 独立弹层、视口放置、Escape/外部点击关闭、焦点恢复和所有者退出 | `Popups.cs`；父裁剪外命中、嵌套、隐藏/禁用/移除、错误清理回归；ComboBox 使用同一机制 |
| B05 | BoxLayout 的 Auto/Fixed/Percent、Min/Max、Margin/Alignment，Grid 固定/Auto/Star/跨格，Wrap | `Layout.cs`、`Element.cs`；精确坐标、嵌套百分比、Grow、重测量、非法数值与控制权冲突断言 |
| B06 | 可释放绑定、命令/CanExecute、双向值和文本校验 | `Binding.cs`；订阅退出/失败回滚、归一化回写、重入上限和线程归属测试；模型与控件钳制后的值一致 |
| B07 | v2 文件、验证、加载、原生编辑器属性/保存重开/撤销、C# 导出 | `Assets/BetaAssetComponents.cs`、EditorSession/Exporting、EditorShell/EditorCanvas；实际编译生成代码并比对全部绘制命令字节、UTF-8 和业务命令；运行库无编辑器依赖 |
| B08 | 原生组件表单和布局参考程序 | Showcase `--beta`；真实 renderer、深浅主题、弹层、紧凑窗口、Items/Presenter/Separator/自定义 Button 场景；离屏与原生窗口回归 |
| B09 | 三个 `0.2.0-beta.1` 本地 NuGet 与隔离消费者 | `pack-beta.ps1`；独立 feed/cache/obj/bin、仅 PackageReference、包闭包/ABI/版本/native 哈希；仅运行时消费者和带编辑器/窗口消费者分别通过 |
| B10 | 干净源码 Debug/Release 全量门禁、图片复核、独立审查和修复历史 | 下表的两次 All、两次包工作流；逐项审查回执与最终图片检查；失败记录和未验收范围保留 |

类型、API 用法和生命周期归属见 [Beta 使用指南](beta-components.md)，文件示例见 [beta.youi.json](examples/beta.youi.json)。
表中的 C# 运行时文件位于 `managed/YoUI/`，编辑器核心位于 `managed/YoUI.Editor.Core/`，原生编辑器位于 `tools/YoUI.Editor/`。

## 最终门禁证据

| 配置 | 命令与结果 | 机器记录 / 日志 |
| --- | --- | --- |
| Debug | `verify.ps1 -Configuration Debug -Layer All`：通过 | [result.json](../artifacts/verification/1a5285693a6444aeae3a80cdaeb33036/result.json)、[Debug-All.log](../artifacts/beta-final/90e86add65c6ffd24f73dca7a30874176d887474/Debug-All.log) |
| Release | `verify.ps1 -Configuration Release -Layer All`：通过 | [result.json](../artifacts/verification/b56c2ee269ec469e89f670c7cb59038f/result.json)、[Release-All.log](../artifacts/beta-final/90e86add65c6ffd24f73dca7a30874176d887474/Release-All.log) |
| Debug 包 | `pack-beta.ps1 -Configuration Debug -VerifyWindow`：通过 | [manifest.json](../artifacts/beta-packages/0.2.0-beta.1-Debug-e078b5678b144b4a9f5421e64e0651d0/manifest.json)、[Debug-Package.log](../artifacts/beta-final/90e86add65c6ffd24f73dca7a30874176d887474/Debug-Package.log) |
| Release 包 | `pack-beta.ps1 -Configuration Release -VerifyWindow`：通过 | [manifest.json](../artifacts/beta-packages/0.2.0-beta.1-Release-01d827e977a54cf5a4a7642e7c9d6584/manifest.json)、[Release-Package.log](../artifacts/beta-final/90e86add65c6ffd24f73dca7a30874176d887474/Release-Package.log) |

两次 All 均包含 Cargo fmt/clippy/workspace tests、213 项托管逻辑检查、25 项编辑器检查，以及既有组件契约、性能分析和平台逻辑检查。
生成 C# 的验证是实际编译并执行的命令流/文本/回调对照，不只是字符串检查。
Beta 场景分别完成 12 项离屏交互检查、13 项原生窗口混合检查；原有 HWND 生命周期完成 48 项检查。
这些数量属于各自检查组，不合并为一个“所有输入行为均已验证”的结论。

真实 GPU 为 NVIDIA GeForce RTX 5060 Laptop GPU，DX12 / DiscreteGpu，驱动 `32.0.15.7705`。
原生窗口在 125% DPI 下创建并提交帧；紧凑场景实际使用 SetWindowPos 调整 HWND，再验证表单滚动和 Apply 可达。
Beta 原生检查使用 Win32 PostMessage，并有显式模型/焦点更新；已有 OS 鼠标移动门禁在用户明确同意“测试期间不操作鼠标”的条件下通过。
上述结果分别证明对应层次，不等同于 Beta 全部控件的物理键鼠、真实 IME 或辅助技术验收。

每个 All 有 69 个原生同步记录阶段、7 个消费者。独立审查核对了每阶段 DLL 的配置与源/消费者哈希，错配为 0。
Cargo 各构建阶段产生过不同 DLL 哈希，不能把最后打包 DLL 的哈希当成所有阶段的同一二进制；各阶段采用自己的同步记录。
`scripts/test-verification.ps1` 的独立通过记录只证明编排和失败传播，不计为真实 GPU/窗口证据。

工具链：[toolchain.json](../artifacts/beta-final/90e86add65c6ffd24f73dca7a30874176d887474/toolchain.json)：Windows x64 `10.0.26200`、.NET SDK `10.0.401` / runtime `10.0.12`、Rust/Cargo `1.96.0`、uv `0.12.2`。
系统默认 SDK 9 不满足本分支 global.json；本机复验使用记录中的独立 SDK 路径。字体采用 renderer 系统字体发现及默认 sans-serif，门禁未枚举实际解析字体，因此不声称跨机器像素完全相同。

## 独立包消费

每个配置均生成 `YoUI`、`YoUI.Platform.Windows`、`YoUI.Editor.Core` 三个包，版本 `0.2.0-beta.1`、目标 `net10.0`、原生 RID `win-x64`。
仅运行时消费者完成 23 项检查，不加载 Editor/Windows 宿主；完整消费者完成 27 项检查，包含编辑保存重开/撤销/导出、实际 GPU 和独立 HWND。
错误 NativeProfile 与缺失 native DLL 均被预期拒绝。消费者 ABI 查询为 `0x10000`，DrawCommand 为 80 字节。

两个最终 manifest 的 `sourceSha` 均为受测提交、`sourceDirty=false`、`sourceChangedDuringRun=false`，运行前后及两个配置之间源码快照均为：

```text
B02A8083667E903726D7EF1DE2CE9CC196F088FFC2CCD493F552AFF4B1A64004
```

最终包内 native DLL SHA-256（源文件、包归档和对应消费输出一致）：

```text
Debug   F9F2765789A2B7EB58604792A7F1A59569FE08F728FA5E79226EF146001E7FA5
Release 873FF2D27E5219D0BF79F6B3AF0D767A66E51CABCB695AB582B23B0AFAD5F65B
```

包文件与哈希逐项列在 manifest，`feed/` 位于对应运行目录。使用方式见 [本地打包与消费](beta-packaging.md)。
Debug/Release 的同版包使用不同 feed，不互相覆盖。这里没有 NuGet 发布或 GitHub Release 结果。

## 图片检查与修复记录

最终产物已实际打开检查：[image-qa.json](../artifacts/beta-final/90e86add65c6ffd24f73dca7a30874176d887474/image-qa.json) 记录主审查看的 8 张图片及 SHA-256。
包含 Debug 布局与 popup、Release 浅色与紧凑 HWND，以及两个配置的包消费者/窗口图片。
独立审查另看了最终 Debug 紧凑/浅色/布局、Release 紧凑/编辑器窗口和四张包消费者图片。
数字步进符号完整、选中 popup 文字清楚、浅色表单配色一致、组合容器可见，紧凑窗口 Apply 可达。

开发过程曾出现并已修复：嵌套 StackPanel 百分比误差、非空 Tab 的 -1 验证/运行时不一致、数值绑定钳制后模型不同步、Radio 组重入转移、VirtualList 缩容通知、v2 Button 内容槽及参考程序缺少真实组合示例。
实际图片检查也修复了焦点选中标题不可见、数字按钮符号裁切、浅色主题卡片和紧凑表单滚动问题。早期迭代图片有覆盖，不声称保存了每次失败画面。

首次冻结提交 `0555ea5` 的 Debug/Release All 虽然通过，后续独立审查仍复现 Radio 方向键越过 modal/popup 范围的问题。
修复为 `90e86ad` 后新增 modal/嵌套 popup 回归，并重新完整运行四条命令；旧成功结果不替代最终修复提交的验收。
SDK 路径/版本失败、编排断言失败、独立复现日志和历史 All 记录保留在本地 artifacts；完整审查回执逐项区分其版本。

## 保留的能力边界

- DataGrid 是固定行高只读表格，支持稳定键、排序/筛选/选择；单元格编辑、TreeGrid、Dock、日期组件留待后续迭代。
- ItemsControl 是非虚拟化容器，业务模板、委托和绑定由 C# 应用持有；ReleaseItem 配合 BindingScope 显式释放，不自动发现任意外部订阅，也不将业务对象写入文件。
- 编辑器新增组件/属性/保存/导出沿用原生自举框架；复杂集合属性使用 JSON 字段，完整模板系统、Prefab/时间轴和制作工具重构未纳入本轮。
- 动画 SDK 深化、动态高度异步虚拟列表、复杂文本软换行/完整 bidi、多指针和更广泛 UI 能力按 1.0 路线推进。
- 本轮实际 GPU 后端是 DX12。历史 Alpha 的 Vulkan/OpenGL 结果不自动成为 Beta 后端验收；Linux 原生窗口、新平台、真实 IME、无障碍、设备丢失和长时间 soak 尚未验收。

## 复现与启动

从 Beta 工作区根目录运行，并先使 `dotnet --version` 为 `10.0.401`。本机已验证 SDK 位置见 toolchain.json，可设置：

```powershell
$betaSdk = 'C:\Users\HT\DEV\YoUI\artifacts\alpha-audit-20261014\dotnet'
$env:DOTNET_ROOT = $betaSdk
$env:DOTNET_ROOT_X64 = $betaSdk
$env:PATH = $betaSdk + ';' + $env:PATH
$env:CARGO_NET_OFFLINE = 'true'
./scripts/build.ps1 -Configuration Debug
dotnet run --project samples/YoUI.Showcase -c Debug --no-build -- --beta
```

完整验收会移动系统鼠标，运行前需确认桌面条件及鼠标空闲：

```powershell
./scripts/verify.ps1 -Configuration Debug -Layer All
./scripts/verify.ps1 -Configuration Release -Layer All
./scripts/pack-beta.ps1 -Configuration Debug -VerifyWindow
./scripts/pack-beta.ps1 -Configuration Release -VerifyWindow
```

`artifacts/` 是忽略提交的本地产物；本文、开发合约和审查回执随分支保存，日志、JSON、PNG 和 nupkg 保留在本工作区，可按上述命令重新生成。
