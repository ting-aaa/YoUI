# 验证记录

验证日期：2026-09-12。工程目录 `C:\Users\HT\DEV\YoUI`，初始为空且不是 Git 仓库；本次没有创建提交或远程仓库。

## 后端验证更新

DX12、Vulkan 和原生 OpenGL 已在本机完成 24 项运行检查、28 组图像对比，全部通过。修复 Vulkan HWND 缺少 HINSTANCE 和 SDF 矩形角点褪色问题，全量 Release 回归通过。ANGLE 为可选实验，未通过渲染验收。详细范围、驱动、实际窗口与复现入口见 [渲染后端验证](render-backends.md)。下文保留各阶段历史记录。

## 使用文档与 API 文档检查

以下是新增使用/API 文档时执行的检查；后续章节保留桌面基础版本和 Editor 的历史验收记录。本轮变更为文档、入门示例、示例资源和源码注释，未修改运行库行为。

| 类型 | 本轮结果 |
| --- | --- |
| 文档结构 | 本地文件链接、页内跳转、代码围栏、入门程序/项目文件与正文一致性检查通过 |
| C# 示例编译 | 从 Markdown 提取的 15 个示例编译成功，0 warnings / 0 errors；HelloYoUI 项目单独构建成功 |
| Rust / C 示例 | 2 个 Rust 示例通过 cargo check；C 示例使用 MSVC x64 编译链接，并成功执行离屏绘制 |
| Rust API 网页 | cargo doc 成功；RUSTDOCFLAGS=-D warnings 下无 rustdoc 警告 |
| 资源与导出逻辑 | 示例 JSON 加载、EditorSession 事务和保存、CLI C# 导出、DLL 插件导出通过 |
| 生成代码独立验证 | 仅引用 YoUI 的项目编译运行成功；JSON 和生成代码产生相同 IR/UTF-8 内容，app.greet 命令回调一致；使用逻辑文字测量器 |
| 真实 GPU | NVIDIA RTX 5060 Laptop / DX12：HelloYoUI、直接 DrawList 绘制、C ABI 色块输出成功，三张图片均已查看 |
| 原生窗口交互 | 本轮未重跑；离屏 GPU 示例不能替代 HWND 输入与真实用户交互验收 |

文档入门截图保存在 `docs/images/hello-youi.png`。临时提取工程、编译/导出产物在 `artifacts/documentation-validation/`，检查清单见各运行目录中的 `manifest.json`。本轮没有重复执行完整 `scripts/verify.ps1`；下文全链路测试数量属于此前的运行库验收。

## 环境

- Windows x64，系统报告 `10.0.26200`。
- Rust 1.96.0；.NET SDK 9.0.318，运行时 .NET 9.0.20。
- NVIDIA GeForce RTX 5060 Laptop GPU，wgpu DirectX 12。
- 初期窗口检查为 100% DPI；本次 Editor 原生输入验收记录为 125% DPI，画布拖动/缩放、预览输入和 Tab 均通过。尚未进行多显示器移动实测。

## 自动验证

Editor 完成后，`scripts/verify.ps1 -Configuration Debug` 与 `-Configuration Release` 已依次完整通过。最后的资源大小、拖动撤销和原生捕获释放修复包含在最终 Release 全链检查中。Rust 渲染器本次未改动，所有下游 UI、导出与窗口检查已纳入同一脚本。

| 检查 | 结果 |
|---|---|
| Debug / Release 原生与 C# 构建 | 通过，C# 0 warnings / 0 errors |
| cargo fmt / clippy（warnings as errors） | 通过 |
| Rust 核心 + 场景逻辑 | 9 tests passed |
| 真实 GPU 参考像素 | 1 test passed；包含顺序、线性 alpha、裁剪、整体透明度、重用、resize、图像、三角形和失效资源 |
| C# 行为测试 | 14 tests passed；布局、输入路径、捕获、焦点、模态、Unicode、虚拟列表、剪贴板及撤销 |
| Editor / UI 控件 / 资源 / 导出测试 | 12 tests passed；结构与大小校验、层级与历史、保存、布局、视口变换、复用控件、交互预览、拖动取消、插件与生成代码 |
| 导出的 C# 独立编译运行 | 通过；临时项目仅依赖 YoUI，配置与代码产生的完整 IR / UTF-8 字节相同，事件绑定行为相同 |
| Editor Win32 消息驱动检查 | 13 checks passed；画布选中/拖动/缩放/撤销/重做、保存重开、预览切换、开关、命令、输入、Tab、状态恢复，以及打开模态前释放 OS 捕获 |
| Editor GPU 截图 | 1440×900 编辑/选择/预览，以及 1100×760 紧凑布局，均生成并检查 |
| Win32 消息驱动检查 | 7 checks passed；主题、输入筛选、列表滚动、模态阻挡、确认与 resize |
| 独立 renderer / scene 示例 | 通过，不使用 C# UI |
| 深色、浅色、模态、窄窗口图像 | 生成并完成可见检查 |

GPU 测试在普通 `cargo test` 中标记 ignored，因为需要真实适配器；验证脚本随后显式执行 `--ignored`，本次确实运行通过，未将跳过当作通过。

## 可见窗口验证

使用原生电脑操作工具检查实际显示的 Showcase 窗口：鼠标切换主题成功，搜索框获得可见光标，粘贴 `Aurora` 后显示 16,667 项，Ctrl+Z 后恢复 100,000 项；确认弹窗和 Escape 关闭正常。

Editor 实际窗口验证：画布选中 Heading → 属性框粘贴中文 → Enter 提交后画布显示中文、文档出现未保存标记；保存对话框创建 `artifacts/editor-manual.youi.json`，随后通过 Open 对话框重新打开成功。通过 Load plugin 选择已构建的 `YoUI.Exporter.Sample.dll`，导出目标出现 `Plugin · Component inventory`，Export 对话框生成 `artifacts/editor-manual-plugin-export/inventory.md`，已读取核对节点和 `project.create` 命令。

可见验收发现并修复了三处实际问题：32 像素高输入框使用固定内边距导致字形裁切，已在 UI 库改为垂直居中与独立水平裁剪；OPENFILENAME 中 StringBuilder 字段无法正确计算原生结构尺寸，已改为具有明确所有权的 UTF-16 缓冲区并重新完成保存/打开/插件导出；按钮 Clicked 打开同步对话框时仍持有 OS 鼠标捕获，现已先释放 OS 捕获并保留本次 PointerUp 的逻辑目标。新增原生 GetCapture 回归检查通过，实际用鼠标取消 Open 对话框后仍选中 Artboard，背景未误选。

测试窗口曾关闭以释放构建 DLL；最终 Release Editor 已重新打开供试用。

此过程发现并修复了最初未接入剪贴板导致的粘贴输入缺口，同时增加了选择/复制/剪切/粘贴/撤销逻辑测试。尚未以真实中文或日文输入法完成候选框和组合输入验收。

早期默认多后端初始化停滞并留下 OpenGL `wgpu Device Class` 进程错误提示，已结束旧进程并关闭残留提示。Windows 改为明确选择 DX12 后，全部渲染、窗口和可见输入检查通过；不据此宣称已定位所有驱动问题或验证 OpenGL/Vulkan。

## 性能快照

原始结果：[benchmark.json](../artifacts/benchmark.json)。Release，1280×820，30 帧预热后采样 240 帧，开启示例动画，数据集 100,000 项，6 个实际行节点；1 个全视口离屏层。

| 指标 | 中位数 | P95 |
|---|---:|---:|
| C# 构帧 | 0.0194 ms | 0.0204 ms |
| Rust 准备 | 0.026 ms | 0.034 ms |
| CPU 编码和提交 | 0.035 ms | 0.045 ms |

采样帧包含 444 条命令、约 1,054 个实例、4 次离屏渲染 draw call；窗口呈现会再增加一次合成绘制。实例上传 101,280 bytes/frame。动画阶段布局重算 0 次。Managed 采样均摊分配 53 bytes/frame，包含采样程序自身的列表分配；不包含 Rust 分配和 GPU 内存。

这些数据只表示该机器、该场景的热缓存 CPU 开销，不是 GPU 耗时、端到端输入延迟、帧率承诺或跨设备性能结论。没有使用 GPU 时间戳，也没有进行长时间运行、设备丢失或显存峰值压力验收。

以上基准场景仍为 Showcase，不能当作大型 Editor 文档编辑的性能结论。Editor 的测试验证空闲时复用帧和树视图只绘制可见行；编辑变更目前重建预览树，大型文档的持续编辑基准尚未建立。

## 产物

- `artifacts/showcase-dark.png`、`showcase-light.png`、`showcase-modal.png`、`showcase-compact.png`
- `artifacts/window-smoke.png`、`window-smoke.json`
- `artifacts/render-scene.png`、`scene-graph.png`
- `artifacts/benchmark.json`、`benchmark-frame.png`
- `artifacts/editor.png`、`editor-selected.png`、`editor-preview.png`、`editor-compact.png`
- `artifacts/editor-tests.json`、`editor-window-smoke.json`、`editor-window-smoke.png`
- `artifacts/editor-validation/<run-id>/compiled/`：生成的代码、配置及独立编译验收工程
- `artifacts/editor-manual.youi.json`、`editor-manual-plugin-export/inventory.md`：真实文件对话框操作产物

产物可由脚本重新生成；`artifacts/` 不纳入 Git。支持范围及下一阶段事项见 [设计对照](design-status.md)。
