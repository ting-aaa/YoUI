# 固定工具链与 .NET 10 升级

## 当前版本与安装

- [`rust-toolchain.toml`](../rust-toolchain.toml)：Rust **1.96.0**，minimal profile，包含 rustfmt、Clippy。
- [`global.json`](../global.json)：.NET SDK **10.0.401**，`rollForward: disable`，`allowPrerelease: false`。
- 所有托管项目、示例与导出代码验证项目目标为 **net10.0**。上述 SDK 携带 .NET **10.0.12** 运行时。

.NET 10 是 LTS。截至 2026-10-10，微软支持策略列出的结束日期为 **2028-11-14**。SDK 10.0.401 于 2026-09-08 发布。来源：[支持策略](https://dotnet.microsoft.com/platform/support/policy)、[官方下载](https://dotnet.microsoft.com/download/dotnet/10.0)、[官方发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)。

安装 [rustup](https://rustup.rs/) 后，在仓库目录运行 `rustup show active-toolchain`；rustup 会按文件选择并按需安装工具链及组件。Windows 仍需 MSVC C++ Build Tools / Windows SDK。从 .NET 官方下载页安装上述精确 SDK，再检查：

```text
rustc --version
cargo --version
rustup component list --installed
dotnet --version
dotnet --list-runtimes
```

不要设置 `RUSTUP_TOOLCHAIN` 覆盖仓库版本。SDK 选择依据**当前工作目录**向上寻找 `global.json`，而非 `--project` 的路径；从仓库目录运行命令。原生复制回归有意从独立临时目录调用 .NET，因此将同一 `global.json` 复制过去。CI 直接读取仓库选择文件，不再生成独立的浮动 SDK 配置。

固定版本用于复现，不保证系统编译器、驱动或托管 runner 镜像完全相同，也不自动追踪安全更新。后续更新需显式修改选择文件，核对官方支持状态，并重跑 Debug / Release。

## 升级边界与回退

变更拆成两层：

1. 固定此前已验证的 Rust 1.96.0、.NET SDK 9.0.318，保留 `net9.0`。同时统一本地、CI 和独立工作目录的 SDK 选择。
2. 单独将 SDK 升至 10.0.401，将运行目标、脚本输出路径、插件路径、文档示例与生成代码测试升级至 `net10.0`。

需要回退时完整撤销第二层，保留第一层；只改 `global.json` 或只改目标框架会造成不一致。不要复用旧 `bin/*/net9.0` 应用或插件。外部应用和导出插件需重新以 `net10.0` 编译；本次不承诺旧 .NET 9 插件二进制兼容。

资产格式仍为 V1，未改动 `docs/examples/welcome.youi.json`。新增兼容回归读取该既有文件，验证保存/重载后完整绘制命令 IR、UTF-8 文本与 `app.greet` 键盘绑定一致。生成 C# 的子项目改为跟随外层 Debug / Release 配置；原测试始终使用 Release。

原生 ABI、库名与复制规则不变：Windows `youi_render.dll`、Linux `libyoui_render.so`、macOS `libyoui_render.dylib`；Debug 对应 Cargo `debug`，Release 对应 `release`。这不是新增 macOS 或 Linux 窗口支持的声明。

## 验证入口与限制

按[分层验证](verification-layers.md)与 [CI 说明](verification-ci.md)运行两种配置：

```bash
bash scripts/test-verification.sh
pwsh -NoProfile -File scripts/test-verification.ps1
bash scripts/verify-linux.sh Debug
bash scripts/verify-linux.sh Release
```

逻辑门禁包含 Rust build/fmt/Clippy/test、托管核心、编辑器、真实导出插件、生成代码、Showcase、HelloYoUI 及原生库复制。Linux 检查复制文件与对应 Cargo 输出逐字节相同，并确认缺失原生库时明确失败。

历史 Windows/GPU 记录仍保留当时的 .NET 9 版本。本次 Linux 编译/CPU 逻辑通过不能替代 .NET 10 下的 Windows DLL 加载、Win32 输入、真实窗口或 GPU 验收；这些仍需在对应环境按分层入口重跑。不要把尚未运行的托管 CI 或真实设备验收写成通过。
