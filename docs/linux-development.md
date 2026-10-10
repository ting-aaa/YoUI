# Linux 构建与逻辑验证

Linux 可以参与 Rust、C# 控件、布局及编辑器逻辑的开发。当前窗口和输入宿主仍是 `YoUI.Platform.Windows`；Linux 编译成功不代表支持 Linux 桌面窗口，也不代表 GPU 渲染已经验收。

## 环境与命令

准备 Rust 工具链（包括 rustfmt、Clippy）、.NET 9 SDK、Bash 及系统 C/C++ 编译工具。首次运行需要下载 Cargo / .NET 依赖；无需 PowerShell 或 Python。仓库的历史工具链版本见[快速开始](getting-started.md)。

在仓库根目录运行：

```bash
bash scripts/verify-linux.sh
bash scripts/verify-linux.sh Release
```

脚本先构建对应配置的 Rust workspace，然后执行格式检查、Clippy、Rust 逻辑测试、C# 控件测试和编辑器测试（包含导出代码编译与插件验证），最后编译 Showcase、HelloYoUI 并检查原生库复制。所有步骤失败即退出。脚本固定使用仓库的 `target/` 输出目录，不依赖全局工具链安装路径。

这是无 GPU 的编译/逻辑验证入口。被标记为 ignored 的 GPU 测试仍不会运行；脚本不会创建窗口、执行截图或性能验收。Windows 的完整验收入口仍是 `scripts/verify.ps1`。

## 原生库与配置

Showcase、Editor 和 HelloYoUI 共用 `scripts/NativeLibrary.targets`，按构建宿主选择真实库文件：Windows 为 `youi_render.dll`，Linux 为 `libyoui_render.so`，macOS 为 `libyoui_render.dylib`。macOS 文件名映射并非 macOS 运行验收。

Debug 从 `target/debug/` 复制，Release 从 `target/release/` 复制。库不存在时构建会明确失败并提示 Cargo 命令，不会用占位文件或跳过复制来伪装成功。仅编译 C# 时，先从仓库根目录构建相同配置的 Rust 库：

```bash
cargo build --workspace --locked --target-dir target
dotnet build tools/YoUI.Editor -c Debug
# Release 对应 cargo build --workspace --locked --target-dir target --release
```

此流程针对宿主原生构建，不处理 Cargo `--target`、自定义 profile、.NET RID 交叉编译或发布打包。自行调用 Cargo 时请保留上述输出目录，且不要将其他平台或架构的库混入其中。

构建三个应用后，可单独运行复制目标回归检查：

```bash
bash scripts/test-native-copy.sh Debug
```

检查使用实际生成的原生库，比较三个应用输出中的文件，验证从其他工作目录调用 MSBuild 仍能复制正确配置，并确认缺少原生库时返回错误。临时输出在检查结束后清理，不修改真实的原生库。
