# A12 离屏平台验收

`scripts/accept_platform.py` 把已有 PowerShell `Logic` / `Gpu` 门禁包装为一次有超时、独立输出和可审计元数据的宿主验收。它不实现新的渲染后端或窗口宿主，不运行 `Window` / `All`，也不把 Linux 编译、软件 Vulkan 或离屏截图当作 Windows 桌面或 Linux 原生窗口通过。

## 运行入口

需要仓库[固定工具链](toolchains.md)、PowerShell 7、Python 3.11+ 和 `uv`。脚本仅使用 Python 标准库，没有额外 Python 依赖。非空 `RUSTUP_TOOLCHAIN` 会覆盖仓库 pin，因此包装器直接拒绝；先取消该覆盖。GPU 层另需当前宿主可用的指定后端/驱动。选择后端不会安装驱动，也不会自动改用其他后端或 CPU 实现。

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:POWERSHELL_TELEMETRY_OPTOUT = '1'
uv run scripts/accept_platform.py --configuration Debug --backend vulkan
uv run scripts/accept_platform.py --configuration Release --backend vulkan
```

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1 POWERSHELL_TELEMETRY_OPTOUT=1
uv run scripts/accept_platform.py --configuration Release --backend vulkan \
  --timeout-seconds 900 --output artifacts/platform-acceptance/my-release-run
```

配置和后端必须显式填写。`--backend` 接受 `dx12`、`vulkan`、`gl`、`metal`，但列出名称不是证明该宿主支持它。默认依次调用：

```text
pwsh -NoLogo -NoProfile -NonInteractive -File scripts/verify.ps1 -Configuration <Debug|Release> -Layer Logic
pwsh -NoLogo -NoProfile -NonInteractive -File scripts/verify.ps1 -Configuration <Debug|Release> -Layer Gpu
```

所有命令以仓库为工作目录；脚本可通过绝对路径从其他目录调用。`--powershell` 可指定 PowerShell 7 可执行文件的路径，包括空格路径。原门禁负责相同配置的原生/托管构建，包装器没有 `--skip-build` 或直接使用旧二进制的捷径。

- `--layers Logic` 或 `--layers Gpu` 明确缩小范围；结果列出选中层。两层同时指定时必须是 `--layers Logic Gpu`。重复层、`Window`、`All` 和未知参数被拒绝。
- `--timeout-seconds` 是每个门禁的总墙钟上限，包含构建，默认 900 秒；允许 1–86400 秒。元数据命令分别最多 30 秒，且不会超过指定上限。冷缓存首次构建可能需要更长门禁时间。
- 超时或 Ctrl+C 会尝试终止整个命令树：POSIX 独立进程组，Windows `taskkill /T /F`。清理命令/回收等待另各最多 15 秒；清理失败会记录 `cleanupError`，本轮不会通过。不要把 Windows 清理的模拟测试当作 Windows 实机验收。
- `--require-hardware` 仅用于选中 `Gpu` 的运行。只有原生报告的 `integrated` / `discrete` 类型满足要求；`cpu` 是软件，`virtual` / `other` 标记为 `unclassified`。默认允许软件渲染，但结果明确标注软件，不升级为硬件证据。
- 基准使用已有 GPU 门禁的默认预热 30 帧和采样 240 帧；本包装器不改变基准参数。自定义帧数、指标含义和比较边界见[CPU 帧分析](profiling.md)。

例如可通过宿主正常驱动配置选择软件 Vulkan，再运行 `--backend vulkan`。记录会保留指定 ICD、`LIBGL_ALWAYS_SOFTWARE` 等相关环境变量，以及实际适配器身份。是否使用软件以真实适配器类型为准，不以变量是否存在推断。

## 输出与严格校验

未指定输出目录时，在 `artifacts/platform-acceptance/` 创建独立时间戳/随机目录。显式 `--output` 也必须是尚不存在的目录；已有目录即使为空也拒绝，不删除、不覆盖。默认位置已被 Git 忽略。不要在构建产物目录中放置验收记录，也不要同时运行其他构建、验证脚本或编辑源码。

每轮记录：

- `result.json`：总体状态、选中层、配置/请求后端、开始/结束时间、命令/退出码/耗时/超时、宿主 OS/架构/Python、Rust/Cargo/.NET/PowerShell 版本和少量相关环境变量。不会转储整个环境或凭据。
- 开始和结束的 Git SHA、dirty、porcelain 路径；结束时 SHA 或状态变化则失败。`global.json` / `rust-toolchain.toml` 保存 SHA256，运行中变化则失败。
- 每个门禁的完整独立日志，以及本轮新生成的 `artifacts/verification/*/result.json` 原始副本。零退出码必须配合唯一新记录；SHA、dirty、配置、层、成功状态、OS 元数据和开始/结束时间均需有效，时间必须落在命令窗口内（允许两秒精度差）。缺失、失败、陈旧、矛盾或多份并发记录都不会被接受。
- 每层成功构建后的 `YoUI.Showcase.dll`、`YoUI.dll`、Showcase 同目录的宿主原生库和对应 Cargo profile 原生库的 SHA256。复制的原生库必须与该 Cargo 配置产物相同且非空。每层分别记录，避免把后来重建的文件误当作先前的样本来源。
- `gpu-artifacts/`：通过 `YOUI_ARTIFACT_DIR` 独立保存的基准 JSON、Showcase/Editor 等离屏产物，另记录文件长度和 SHA256。现有 Rust 示例仍会把部分 PNG 写到仓库 `artifacts/` 固定路径；它们不作为本目录的独立图片证据。实际截图仍需查看。

GPU 门禁只有在新 `benchmark.json` 完整且自洽时才通过：schema 2、配置、请求/实际后端、适配器字符串/类型、与同次 PowerShell 门禁完全相同的 OS 描述、非空运行时/进程架构、固定 1280×820 / 100000 数据项 / 1⁄60 秒模拟步长、正整数处理器数、1 到数据项数之间的整数实例化行数、整数预热/帧数、连续 `0..239` 的全部样本、有限非负耗时、整数非负计数和规定资源快照字段。还会用样本重算中位/P95，核对旧摘要的最后一帧计数、布局合计和分配总量/均值。拒绝非有限 JSON 常量和重复键；仅有进程零退出码、摘要或旧文件不算完成。

`result.json` 在命令开始前和各阶段结束时更新。失败保留已收集的日志、门禁副本和部分文件，返回非零；Ctrl+C 返回 130。若进程被强制杀死到无法执行清理，最后的 `running` / 部分记录不是通过记录。输出目录拒绝或参数错误发生在正式运行之前，不生成假通过记录。

SHA 是源码基线，不是正在运行的二进制自行报告的版本号。使用干净、已提交且无并发修改的 checkout 做正式验收；`dirty=true` 的结果用于开发诊断，并需另保存对应补丁。开始/结束 SHA 和 porcelain 相同不证明已修改文件内容从未变化，二进制哈希也不能替代缺失的源码快照。

## 可作出的结论

- Logic 通过：本次宿主、配置和已列出的逻辑/构建检查通过。
- Gpu + `software` 通过：本次实际软件适配器上的 GPU API 离屏检查通过；不等于硬件性能或真实 GPU 验收。
- Gpu + `hardware` 通过：本次报告的硬件适配器及请求后端的离屏检查通过；不推广到其他驱动、机器或系统。
- 两层都通过仍不包含原生窗口、真实 OS 输入、IME、无障碍、长时稳定性、设备丢失、跨平台窗口或屏幕 FPS。

A12 的工具是可在受支持构建宿主上运行的验收入口；文件本身不是平台验收记录。本阶段没有新增 Linux 原生窗口宿主，也没有凭这套入口宣称新的 Windows/macOS 实机通过。具体本轮结果应以归档的 `result.json`、日志、基准和图片为准。已有[验证层边界](verification-layers.md)与[Linux 开发边界](linux-development.md)继续适用。

## 无 GPU 的脚本回归

```bash
uv run scripts/test_accept_platform.py
```

测试使用临时含空格路径、假子进程/产物和模拟的进程树清理；覆盖显式 Debug/Release 与后端传播、新目录拒绝、缺失/陈旧/矛盾记录、失败/超时/中断留档、源码变化、原生库复制哈希不一致、硬件类型要求和严格基准样本/摘要校验。它们不构建或运行真实 Rust/.NET，也不提供 GPU、Windows 或其他平台通过证据。
