# 快速开始

[文档首页](README.md) · [使用指南](guide.md) · [API 索引](api/README.md)

本页从源码引用 YoUI，建立一个可以运行和截图的小应用。当前仓库没有 NuGet 安装或自动打包分发流程。

## 1. 准备环境

- Windows x64，支持 DirectX 12 的图形适配器及驱动。
- Rust 的 `x86_64-pc-windows-msvc` 工具链，以及 Visual Studio C++ Build Tools / Windows SDK。
- .NET 9 SDK：项目目标为 `net9.0`。
- PowerShell，用于运行仓库脚本。

历史验收使用 Rust 1.96.0、.NET SDK 9.0.318，具体机器与验证边界见[验证记录](verification.md)。这些是已验证版本，仓库没有声明更早版本的最低兼容范围。

检查本机安装：

```powershell
rustc --version
cargo --version
rustup show active-toolchain
dotnet --list-sdks
```

首次构建需要下载 Cargo / .NET 依赖。只有使用 Python 辅助工具时才需要 `uv`；运行 YoUI 本身不需要 Python。

## 2. 构建并试用

```powershell
./scripts/build.ps1 -Configuration Release
./scripts/run.ps1 -Configuration Release
```

`build.ps1` 先构建 Rust workspace，再构建 Showcase、Editor 和示例导出插件。`run.ps1` 也会先调用构建，再打开 Showcase；关闭窗口后返回终端。

启动 Editor：

```powershell
./scripts/editor.ps1 -Configuration Release
```

`run.ps1` 和 `build.ps1` 默认是 Debug，`editor.ps1` 默认是 Release。再次构建同一配置前关闭该配置的应用窗口，避免 DLL 被占用。更多操作见 [Editor 指南](editor.md)。

## 3. 运行第一个应用

文档附有可直接运行的[项目](examples/HelloYoUI/HelloYoUI.csproj)：

```powershell
cargo build --workspace --release --locked
dotnet run --project docs/examples/HelloYoUI -c Release
```

窗口包含标题、点击计数和按钮。点击按钮，或使用 Tab 将焦点移到按钮再按 Enter / Space，计数会增加。业务事件只修改控件属性，文档会在需要时重新生成绘制命令。

其[完整程序](examples/HelloYoUI/Program.cs)如下：

```csharp
using YoUI;
using YoUI.Platform.Windows;

var root = new StackPanel { Padding = 24, Gap = 16 };
root.Add(new Label("Hello, YoUI") { FontSize = 28, Bold = true });
var count = root.Add(new Label("Clicked 0 times"));
var button = root.Add(new Button("Click me") { Width = 160, Primary = true });
int clicks = 0;
button.Clicked += () => count.Text = $"Clicked {++clicks} times";

if (args.Length == 2 && args[0] == "--headless")
{
    string output = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    using var renderer = new NativeRenderer(640, 360);
    var document = new UiDocument(root, renderer, 640, 360);
    var stats = renderer.Render(document.BuildFrame());
    renderer.SavePng(output);
    Console.WriteLine($"{renderer.Adapter}\n{stats}\n{output}");
    return;
}

if (args.Length != 0)
    throw new ArgumentException("Usage: HelloYoUI [--headless output.png]");

using var window = new DesktopWindow(
    "My application", root, 640, 360, minimumWidth: 480, minimumHeight: 280);
window.Run();
```

显式设置最小窗口大小是因为 `DesktopWindow` 默认最小客户区为 790×740 DIP。`root` 只能附着到一个文档；这里窗口与离屏分支互斥。

## 4. 创建自己的项目

可以复制 `docs/examples/HelloYoUI`，或建立 .NET 9 控制台项目，引用以下两个项目：

- `managed/YoUI/YoUI.csproj`：控件、布局、资源和原生绑定。
- `managed/YoUI.Platform.Windows/YoUI.Platform.Windows.csproj`：Windows 窗口和输入。

下面是放在 `docs/examples/HelloYoUI` 时的完整项目文件。换目录时调整三个相对路径（两个引用和 `YoUIRoot`）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PlatformTarget>x64</PlatformTarget>
    <YoUIRoot>$(MSBuildThisFileDirectory)../../../</YoUIRoot>
    <NativeProfile Condition="'$(Configuration)' == 'Release'">release</NativeProfile>
    <NativeProfile Condition="'$(NativeProfile)' == ''">debug</NativeProfile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../managed/YoUI/YoUI.csproj" />
    <ProjectReference Include="../../../managed/YoUI.Platform.Windows/YoUI.Platform.Windows.csproj" />
  </ItemGroup>
  <Target Name="CopyNative" AfterTargets="Build">
    <Error Condition="!Exists('$(YoUIRoot)target/$(NativeProfile)/youi_render.dll')"
           Text="Build the matching Rust library first: cargo build --workspace [--release] --locked." />
    <Copy SourceFiles="$(YoUIRoot)target/$(NativeProfile)/youi_render.dll"
          DestinationFolder="$(OutDir)" />
  </Target>
</Project>
```

此目标**复制**原生 DLL，不负责构建 Rust。Debug 使用 `target/debug/youi_render.dll`；Release 使用 `target/release/youi_render.dll`。原生库与应用都使用 x64，且必须来自兼容的源码与 ABI 版本。

构建产物在 `docs/examples/HelloYoUI/bin/Release/net9.0/`。直接启动此目录的 `HelloYoUI.exe` 即可，保留同目录中的运行库和 `youi_render.dll`。这仍是依赖已安装 .NET 9 运行时的构建产物，不是独立安装包。

## 5. 生成第一张图片

```powershell
dotnet run --project docs/examples/HelloYoUI -c Release --no-build -- --headless artifacts/hello-youi.png
```

应生成 640×360 PNG，并打印适配器名称及绘制统计。这里不创建原生窗口，但仍使用**真实 GPU**；它不能在没有可用图形适配器的环境中当作纯逻辑测试。

![HelloYoUI 离屏截图，标题、计数和按钮](images/hello-youi.png)

## 6. 加载一个 Editor 资源

仓库附有不依赖 `artifacts/` 的[示例资源](examples/welcome.youi.json)：

```powershell
./scripts/editor.ps1 -Configuration Release -Open ./docs/examples/welcome.youi.json
```

在预览模式点击按钮可以触发 `app.greet` 命令。应用加载与事件绑定见[使用指南](guide.md#加载资源与生成代码)；完整字段定义见[资源 API](api/assets.md)。

## 验证与下一步

完整工程验证：

```powershell
./scripts/verify.ps1 -Configuration Release
```

脚本需要真实 GPU 和 Windows 桌面环境，包含逻辑测试、GPU 像素检查、原生窗口消息检查、截图、代码导出编译及性能探针。截图写入 `artifacts/`，应人工检查；各类验证不能互相替代。

接下来阅读[使用指南](guide.md)。遇到问题先查[常见问题](troubleshooting.md)，具体成员从 [API 索引](api/README.md)查找。
