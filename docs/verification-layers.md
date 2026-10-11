# 分层验证

构建通过、逻辑通过、GPU 像素通过和原生窗口通过是不同证据。默认完整入口保留所有检查，不会因为机器没有 GPU 或不是 Windows 而自动降级为通过。

## 入口与边界

使用 Rust（包含 rustfmt、Clippy）、.NET 10 SDK 和 PowerShell 7。Linux 的逻辑入口只需要 Bash，不要求安装 PowerShell。

```powershell
# Windows 完整验收：构建 + 逻辑 + GPU/示例/基准 + 原生窗口
./scripts/verify.ps1 -Configuration Release
# 与默认行为相同
./scripts/verify.ps1 -Configuration Release -Layer All

# 任意支持构建的宿主：构建 + 无 GPU 逻辑测试
./scripts/verify.ps1 -Configuration Release -Layer Logic

# 需要可用适配器：构建 + GPU 像素、离屏渲染、示例、基准
./scripts/verify.ps1 -Configuration Release -Layer Gpu

# 仅 Windows 桌面：构建 + Editor/Showcase 原生窗口 smoke 检查
./scripts/verify.ps1 -Configuration Release -Layer Window
```

```bash
# Linux 兼容入口仍然只做构建、逻辑及真实原生库复制检查
bash scripts/verify-linux.sh Debug
bash scripts/verify-linux.sh Release
```

- `Logic` 执行 Rust fmt、Clippy、非 ignored 测试，以及 C# 控件和编辑器测试。编辑器测试包含导出代码编译与插件逻辑。
- `Gpu` 显式执行 `cargo test -p youi-render --test gpu ... -- --ignored --nocapture`，并通过 `--lib renderer::text_residency_tests::cached_text_pixels_survive_residency_mapping_loss_with_live_image ... -- --exact --ignored --nocapture` 执行文本驻留映射丢失后的像素回归。`All` 同样包含这两个检查。没有适配器、设备初始化失败或像素断言失败均返回失败，不跳过或输出通过。它还执行 Editor/Showcase 离屏、Showcase 基准和三个 Rust 示例（render_scene、scene_graph、textured_mesh）。
- `Window` 执行 Editor 和 Showcase 的 `--smoke`；它需要 Windows 桌面与可用渲染设备，不等于覆盖所有手动输入、IME、无障碍或多显示器场景。
- 所有 PowerShell 层都会先构建 Rust、Showcase、Editor、导出插件和 HelloYoUI。Debug/Release 同时应用到 Rust 构建、Clippy、测试、示例及 C# 项目；Cargo 输出固定到仓库 `target/`。
- `All` 和 `Window` 在非 Windows 平台明确失败。其他层成功不会被表述为完整验收；Linux 编译不表示 Linux 窗口可用。
- GPU 日志中的适配器身份是证据的一部分。软件适配器运行不能当作真实硬件 GPU 验收，截图仍需人工查看。不要把一次离屏或 smoke 通过推广为其他后端、机器或平台均通过。

所有入口可从其他工作目录调用。配置仅接受 `Debug` 或 `Release`；额外参数会被拒绝。任何子命令失败都会终止后续检查。

## 记录与复核

每次运行在 `artifacts/verification/` 创建独立记录目录，保存提交 SHA、工作区是否有未提交变更、配置、平台、测试层、开始/结束时间和状态。PowerShell 写 `result.json`；Bash 写 `result.txt`。失败记录不等于通过记录；启动前的参数或平台拒绝不产生运行记录。

SHA 只标识基线：`dirty=true` 时还必须保留对应补丁才能复现。日志必须与同一次运行的记录一起保存；GPU 图片和指标沿用各程序的 `artifacts/` 输出约定，旧图片不能作为新运行成功的证明。

[CI 说明](verification-ci.md)描述 Linux/Windows Debug/Release 构建与逻辑矩阵。Hosted CI 通过只证明这些层，不证明真实 GPU 或 Windows 桌面交互通过。

## 脚本回归

```bash
bash scripts/test-verification.sh
```

```powershell
pwsh -NoProfile -File scripts/test-verification.ps1
```

脚本回归使用临时仓库路径和假的命令，只验证编排：不同工作目录及含空格路径、Debug/Release 传播、GPU 集成测试及文本驻留单元测试的显式 ignored 调用（后者使用完整名称和 `--exact`）、textured_mesh 示例的精确参数与层归属、参数拒绝、每一步的错误传播和成功/失败记录。Windows 上还验证默认完整入口和窗口编排；非 Windows 上验证完整/窗口入口不会静默降级。这些模拟检查不计入 Rust、.NET、GPU 或窗口的真实通过数量。

真实原生库复制回归仍由 Linux 入口调用 `scripts/test-native-copy.sh` 完成，不用模拟文件替代。
