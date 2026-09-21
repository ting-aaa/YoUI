# 渲染后端验证

2026-09-12，Windows x64 / 10.0.26200，Rust 1.96，.NET 9.0.20，wgpu 26.0.1。以下结论针对本机与当前 UI 特性集。ANGLE 为可选实验，不是验收条件。

## 结果

| 后端 | 实际适配器 / 驱动 | 真实 GPU | Win32 窗口 | 画面对比 |
|---|---|---|---|---|
| DX12 | NVIDIA RTX 5060 Laptop，32.0.15.7705 | 通过 | Editor 13 项、Showcase 7 项通过 | 基线 |
| Vulkan | NVIDIA RTX 5060 Laptop，NVIDIA 577.05 | 通过 | Editor 13 项、Showcase 7 项通过 | 14 张输出与 DX12 逐像素一致 |
| OpenGL / WGL | AMD Radeon 860M，OpenGL 4.6，26.8.1.260810 | 通过 | Editor 13 项、Showcase 7 项通过 | 14 张均通过容差；Editor 默认画面最大差异 2/255 |

每个后端运行 8 个独立进程：独立渲染示例、GPU 参考像素、Rust 场景示例、Editor 离屏、Editor 窗口、Showcase 离屏、Showcase 窗口、Showcase 基准。共 **24 项运行检查、28 组图像对比通过**。脚本验证每个进程实际返回的后端身份；没有启用跨 API 静默回退。

像素参考覆盖绘制顺序、线性预乘 alpha、裁剪、整体透明度、缓冲复用、resize、纹理上传/释放、三角形；新增全屏不透明度与矩形角点断言。三后端的参考图像完全一致。

图像对比使用未缩放的 RGBA 输出。每张图要求平均单通道差异不超过 0.25/255、任一通道差异超过 4/255 的像素比例不超过 0.1%。OpenGL 全部场景的最大差异为 5/255，最大平均差异 0.11799/255；原始差异数量逐图记录，不将容差通过表述为逐像素相同。缩略对比图仅供查看，统计在原尺寸上完成。

已通过真实电脑操作查看 Vulkan 和 OpenGL 的 Editor 屏幕呈现、点击切换预览；OpenGL 的预览开关能从 ON 切换到 OFF。实际窗口未见倒置、黑屏、明显色偏或内容缺失。Win32 自动检查使用真实 HWND 与系统消息，不能替代所有手动输入场景；图像对比来自 GPU 纹理回读，不是桌面截图的逐像素比较。

## 本次修复

1. Vulkan 创建 Win32 surface 需要 `HINSTANCE`。现在从 HWND 所属窗口类读取实例句柄，补全 raw-window-handle；首轮中失败的两个原生窗口检查已复测通过。
2. 原 SDF 抗锯齿在矩形角点使用距离导数，DX12 的全屏背景右下角出现 alpha 215、Vulkan 为 255。现在按物理像素定义固定一像素过渡，修复角点褪色；GPU 断言覆盖此问题。
3. 新增 `YOUI_BACKEND` 严格选择，以及适配器类型/驱动信息。拼写错误、多个后端列表、`angle` 别名会明确报错。
4. Editor/Showcase 支持 `YOUI_ARTIFACT_DIR`，便于隔离不同后端的截图与日志。

`scripts/verify.ps1 -Configuration Release` 全链通过：Rust 逻辑 10 项、真实 GPU 1 项、C# 逻辑 14 项、Editor/导出 12 项（含生成 C# 独立编译运行）、Editor Win32 13 项、Showcase Win32 7 项；格式和 lint 检查通过。主后端矩阵另外使用 Release 原生 GPU 测试和示例。

## 运行与复现

在创建第一个 renderer 前设置进程环境变量；同一进程已有 renderer 不会因此切换后端。Windows 默认仍为 DX12。`gl` 在当前正式构建中表示 WGL 原生 OpenGL。

```powershell
$env:YOUI_BACKEND = 'vulkan' # dx12 / vulkan / gl
./scripts/editor.ps1 -Configuration Release
```

取消覆盖以恢复默认：`Remove-Item Env:YOUI_BACKEND`。`metal` 仅作为可选 API 标识接受，本机 Windows 构建不能运行，也没有进行 macOS 验证。

运行完整后端矩阵（用新的输出目录保留每次证据）：

```powershell
./scripts/verify-backends.ps1 -Output artifacts/backends-my-run
```

脚本使用 uv 管理 Python 及图像比较依赖；每个直接运行的 GPU 子进程最多 45 秒，失败或超时会记录并返回非零。已有非空后端目录会被拒绝，避免旧图片被误当作本轮结果。常规构建正在运行的 DLL 会被 Windows 锁定，重新构建前应关闭对应应用。

## 证据与性能边界

- [完整矩阵及逐图差异](../artifacts/backends-final/results.json)
- [DX12 / Vulkan Editor 对比](../artifacts/backends-final/compare-vulkan-editor.png)
- [DX12 / OpenGL Editor 对比](../artifacts/backends-final/compare-gl-editor.png)
- [DX12 / OpenGL Showcase 对比](../artifacts/backends-final/compare-gl-showcase-dark.png)
- [全量回归日志](../artifacts/backend-quality-gate.log)
- [Vulkan 可见窗口适配器](../artifacts/window-vulkan/visible-final.log)、[OpenGL 可见窗口适配器](../artifacts/window-gl/visible.log)
- [DX12 基准](../artifacts/backends-final/dx12/benchmark.json)、[Vulkan 基准](../artifacts/backends-final/vulkan/benchmark.json)、[OpenGL 基准](../artifacts/backends-final/gl/benchmark.json)

基准仅记录 1280×820 Showcase、30 帧预热与 240 帧采样的 CPU 构帧/准备/提交时间。没有测量 GPU 时间戳或屏幕 FPS；采样期间存在编译任务，且 OpenGL 使用另一块 GPU，这组记录不用于后端速度排名。三个后端均只实现 6 个可见行节点、动画阶段布局重算 0 次。尚未覆盖长时间压力、显存峰值、设备丢失和多显示器迁移。

## 可选 ANGLE 实验

现用 wgpu 26 的 `angle` feature 只改变 Apple 平台依赖，Windows 的 GL 模块固定选择 WGL。上游当前版本已有 Windows ANGLE 构建开关，但不能把其支持范围直接套到本工程锁定版本。[wgpu 上游说明](https://github.com/gfx-rs/wgpu)

`scripts/prepare_angle.py` 可在独立目录复制本工程与锁定的 wgpu-hal 26.0.6，切换到其已有 EGL 模块，并按 ANGLE 扩展显式选择硬件 D3D11 或 Vulkan。它不修改 Cargo 全局缓存、主工程锁文件或正式 DLL；生成补丁和哈希保存在实验目录。选择常量来自 [ANGLE D3D 扩展](https://chromium.googlesource.com/angle/angle/+/main/extensions/EGL_ANGLE_platform_angle_d3d.txt) 与 [ANGLE Vulkan 扩展](https://chromium.googlesource.com/angle/angle/+/main/extensions/EGL_ANGLE_platform_angle_vulkan.txt)。ANGLE DLL 由调用者本地提供，不随项目分发。

本次实验构建成功，但 **没有通过渲染验收**：D3D11 成功识别 AMD 860M / ANGLE ES 3.0，随后因当前 device 请求包含 ES 3.0 不支持的 compute limits 而失败；ANGLE Vulkan 在本机库初始化时失败，具体原因未定位。遵循“ANGLE 不是必须”的范围，不继续推进其产品接入。参见 [实验结果](../artifacts/backends-angle/results.json) 与 [库来源、哈希及构建信息](../artifacts/angle-build/provenance.json)。

如以后需要继续实验：

```powershell
uv run scripts/prepare_angle.py --angle-dir C:/path/to/local/angle --output artifacts/angle-experiment
uv run scripts/verify_backends.py --backends angle-d3d11 angle-vulkan --angle-build artifacts/angle-experiment --baseline artifacts/backends-final/dx12 --output artifacts/angle-results
```

这是诊断性实验入口，可能返回失败；不属于正式后端支持承诺。
