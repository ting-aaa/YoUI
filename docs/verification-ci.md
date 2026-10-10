# 分层验证与 CI

## GitHub Actions：构建与 CPU 逻辑

工作流：[`.github/workflows/verify.yml`](../.github/workflows/verify.yml)。它在 `main` 分支 push、pull request 和手动触发时运行以下四个独立任务：

| 宿主 | 配置 | 验证入口 |
| --- | --- | --- |
| Ubuntu 24.04 x64 | Debug、Release | `bash scripts/verify-linux.sh <配置>` |
| Windows Server 2025 x64 | Debug、Release | `pwsh -NoProfile -File scripts/verify.ps1 -Layer Logic -Configuration <配置>` |

每个任务先运行 `scripts/test-verification.ps1`；Linux 还运行 `scripts/test-verification.sh`。这些检查测试脚本编排、参数与失败处理，不提供真实 GPU 或窗口验收证据。随后调用相应平台的真实构建/逻辑入口。矩阵不因另一配置失败而提前停止；同一分支的新一次触发会取消旧运行。

托管 runner **只负责编译、原生库复制与 CPU 逻辑**。不会调用 `-Layer Gpu`、`-Layer Window`、完整验收入口或后端验收脚本。普通 Cargo 测试中 ignored 的 GPU 用例保持未执行。CI 的绿色状态不能表示像素正确性、真实显卡、Win32 输入、可见窗口、IME 或无障碍支持已经通过。

## 工具链与权限

- Rust 固定为仓库历史验收使用的 [1.96.0](https://github.com/rust-lang/rust/releases/tag/1.96.0)，通过托管镜像提供的 rustup 安装 rustfmt、Clippy；`RUSTUP_TOOLCHAIN` 保证后续 Cargo 命令使用该版本。
- .NET 安装已发布的 `9.0.x` SDK。工作流按 `setup-dotnet` 返回的具体版本，在 checkout 的上级目录生成仅本次运行使用的 `global.json`，禁用向其他版本滚动，并核对 `dotnet --version`。这样既避免 runner 预装的更高版本改变验证工具链，也不会让生成的文件把 checkout 标成 dirty。具体版本会随 .NET 9 修订更新，并保存在证据中。
- Linux 的 `TMPDIR` 通过 `GITHUB_ENV` 指向同一上级目录下的独立临时目录，位于 checkout 外部。`test-native-copy.sh` 会从 `mktemp` 目录调用 .NET；临时目录必须也在该 `global.json` 的作用范围内，不能退回不受 SDK 选择约束的 `/tmp`。这样仍能验证不相关工作目录，且不改变 checkout 状态。
- 使用明确的 `ubuntu-24.04`、`windows-2025` runner 标签；镜像软件仍会更新，因此保存镜像版本及 `rustc`、Cargo、.NET、PowerShell 版本。
- GitHub Actions 仅申请 `contents: read`，checkout 不保留凭据；不需要仓库 secret，不发布包、不部署、不创建 PR，也不改变合并设置。
- 三个官方 Action 固定为完整提交 SHA，注释标注版本：checkout 7.0.1、setup-dotnet 6.0.0、upload-artifact 7.0.2。升级时需一起核对官方发布记录和 SHA。

官方参考：[checkout](https://github.com/actions/checkout/releases/tag/v7.0.1)、[setup-dotnet](https://github.com/actions/setup-dotnet/releases/tag/v6.0.0)、[SDK 选择与输出](https://github.com/actions/setup-dotnet#matrix-testing)、[upload-artifact](https://github.com/actions/upload-artifact/releases/tag/v7.0.2)、[Ubuntu 镜像](https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2404-Readme.md)、[Windows 镜像](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md)。

## 日志、失败与复现

任务结束时，无论前面成功还是失败，都会尝试上传 `logic-<runner>-<配置>-<重试次数>` 证据包，保留 14 天：

- `artifacts/ci/`：运行元数据、选中的 SDK、工具链版本、脚本回归与真实逻辑验证控制台日志。
- `artifacts/verification/`：验证脚本产生的本次执行记录，包括 PowerShell 的 `result.json` 或 Bash 的 `result.txt`。
- 已生成的 `artifacts/editor-tests.json` 与编辑器验证 `manifest.json`。

Linux 日志管道开启 `pipefail`；Windows 通过独立 `pwsh` 进程运行脚本，并把其退出码传回任务，日志复制不会把失败变为成功。更早的 checkout/安装错误可能发生在验证记录生成之前，此时查看 Actions 自身日志；空证据目录会提示警告，不会补造通过结果。上传失败也不能替代或消除原验证失败。

下载证据时先核对 `run.json` 的提交、配置、宿主以及对应任务状态。失败任务可能只生成部分证据；尚未执行的 GPU/窗口层不能按通过计数。直接在相同平台复现对应配置：

```bash
# Linux
bash scripts/test-verification.sh
pwsh -NoProfile -File scripts/test-verification.ps1
bash scripts/verify-linux.sh Debug
bash scripts/verify-linux.sh Release
```

```powershell
# Windows PowerShell 7
pwsh -NoProfile -File scripts/test-verification.ps1
pwsh -NoProfile -File scripts/verify.ps1 -Layer Logic -Configuration Debug
pwsh -NoProfile -File scripts/verify.ps1 -Layer Logic -Configuration Release
```

本工作流定义不等于托管 runner 已执行成功；是否通过须以对应提交的 Actions 运行结果为准。历史真实 GPU/窗口记录见[验证记录](verification.md)和[渲染后端验证](render-backends.md)。
