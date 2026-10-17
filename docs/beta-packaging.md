# Beta 本地 NuGet 与独立消费

Beta 包默认版本为 `0.2.0-beta.1`，面向 `.NET 10`。本地打包不发布到 NuGet，
不创建 tag、GitHub Release 或上传资产。包中原生库仅提供 Windows x64；
托管 API 中的其他平台入口不构成其他平台的分发或验收结论。

| 包 | 内容与依赖 |
| --- | --- |
| `YoUI` | 控件、布局、绑定、V1/V2 文件运行时及 Rust/wgpu 绑定；`runtimes/win-x64/native/youi_render.dll` |
| `YoUI.Platform.Windows` | 原生窗口与输入宿主；依赖同版 `YoUI` |
| `YoUI.Editor.Core` | 无窗口的编辑模型与 C# 导出；依赖同版 `YoUI` |

普通应用只需 `YoUI`，Windows 窗口应用再引用 `YoUI.Platform.Windows`；
不需要引用编辑器。应用创建 `UiDocument`，通过宿主发送事件、提交绘制并管理退出。
绑定由应用持有并释放 `BindingScope`。包没有内置应用循环。

```powershell
./scripts/pack-beta.ps1 -Configuration Debug
./scripts/pack-beta.ps1 -Configuration Release
```

使用仓库 `global.json` 指定的 SDK，并确保 `dotnet` 指向该 SDK。
脚本默认使用 Cargo 离线缓存；需要下载依赖时显式传入 `-AllowNetworkRestore`。
原生代码总是在托管打包之前构建。`NativeProfile` 必须严格匹配
`Debug=debug`、`Release=release`；缺少匹配 DLL 时拒绝打包。每次运行创建全新的
`artifacts/beta-packages/<version>-<configuration>-<run-id>/`，其 `feed/` 仅含
本次三个包，避免相同版本不同配置互相覆盖。

应用的项目示例：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>false</SelfContained>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="YoUI" Version="[0.2.0-beta.1]" />
    <PackageReference Include="YoUI.Platform.Windows" Version="[0.2.0-beta.1]" />
  </ItemGroup>
</Project>
```

在应用 `NuGet.Config` 中清空源，然后添加本次生成的 `feed/`。
同一版本的 Debug 与 Release 包属于不同本地源；请选择与所需 DLL 对应的运行目录。
依赖包自身使用同版运行时为最低版本，应用应如上固定三个包需要的准确版本。

脚本复制 `scripts/beta-package-consumer/` 到本次隔离目录，阻断仓库上级
`Directory.Build.props/targets`，并创建全新的 NuGet 缓存、`obj/`、`bin/`。
恢复时只允许本次 `feed/`，验证依赖闭包无项目引用、程序集版本以及 DLL 的
源文件/压缩包/消费输出 SHA-256。先以仅 `YoUI` 的配置运行控件、布局、绑定、
V2 文件与真实离屏 GPU，随后引用三个包验证编辑保存重开/撤销、导出和 Windows
宿主程序集。默认检查宿主程序集加载；`-VerifyWindow` 另行创建 HWND 并提交帧，
不模拟物理鼠标、不验证 IME。

每次运行保留 `manifest.json`、日志、源 SHA、dirty 状态、源码快照哈希、tracked
补丁和 untracked 路径、包哈希、实际 adapter/backend、截图和消费者报告。
`status=passed` 才表示本次完整打包工作流通过，失败运行保留 `status=failed`
及原因。图片仍需实际打开检查。离屏 GPU 与原生窗口证据分开记录；
本工作流不代替 Beta 全量验收，也不验证其他平台、物理输入或无障碍。
