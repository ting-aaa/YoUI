# 常见问题

[文档首页](README.md) · [快速开始](getting-started.md) · [API 索引](api/README.md)

先核对错误发生在哪一层：构建、DLL 加载、资源解析、UI 布局、GPU 提交还是文件导出。保留原始错误信息；避免用旧截图或旧二进制判断当前源码是否正常。

## 构建和启动

| 现象 | 检查与处理 |
| --- | --- |
| 找不到 cargo/rustc、链接器或 Windows SDK | 确认 Rust MSVC x64 工具链、Visual Studio C++ Build Tools 和 Windows SDK 已安装 |
| 不支持 net9.0 / 找不到运行时 | 用 `dotnet --list-sdks` 核对 .NET 9 SDK；直接运行构建产物还需对应运行时 |
| `Build the Rust native library first` | 先 `cargo build --workspace --locked`；Release 加 `--release`，再构建 C# |
| `DllNotFoundException` | 检查应用输出目录的 `youi_render.dll`，并检查其本机依赖是否可加载 |
| `BadImageFormatException` | 确认应用与原生库位数一致；当前按 Windows x64 使用 |
| `Incompatible YoUI native ABI` / `EntryPointNotFoundException` | 从同一源码重新构建，并复制匹配配置的 DLL，勿混用旧原生库 |
| DLL 无法复制、文件正在使用 | 关闭正在运行的相同配置 Showcase / Editor / 示例窗口，再构建 |
| 没有可用适配器或 DX12 初始化失败 | 检查本机图形适配器与驱动；headless 也需要 GPU |
| `Renderer calls must stay on the creating thread` | 将构造、测量、绘制、截图、释放统一到创建线程；不要直接在 Task.Run 中访问 renderer |
| 编译 C 示例时找不到 cl | 使用配置好 MSVC 的 x64 Native Tools 终端；这不同于普通 PowerShell |

`scripts/build.ps1` 构建核心示例和工具，不自动构建 `docs/examples/HelloYoUI`；后者按[快速开始](getting-started.md)的单独命令运行。

## 布局、绘制和输入

| 现象 | 检查与处理 |
| --- | --- |
| `StackPanel owns child position and size` | 移除直接子节点的 Transform，或加入普通 Element 容器隔开两种布局 |
| 改 Width/Height 不影响自由布局控件 | 若设置了 Transform，最终矩形由 Transform 决定，修改其 SizeDelta/边距 |
| 根节点重复附着报错 | 每个文档/窗口构造独立根；不要先 CreateDocument 再将同一 Root 交给 DesktopWindow |
| 小窗口无法缩小 | DesktopWindow 默认最小客户区 790×740，显式传 minimumWidth/minimumHeight |
| 属性变化但画面不更新 | 自定义属性需用 Set/Invalidate；数据内容变化需 VirtualList.Refresh；不要修改文档缓存 DrawList |
| 文字或子节点超出区域 | 设置 ClipToBounds；圆角 Radius 本身不定义圆角裁剪 |
| 内容空间不足时溢出 | StackPanel 不自动收缩；调整尺寸、布局或添加有有限视口的 ScrollView |
| 图片跨窗口使用报无效 ID | 图像只能由上传它的 renderer 使用；每个 renderer 单独上传 |
| `glyph atlas budget exhausted` | 已缓存字形不会自动驱逐，释放图片也不回收字形空间；重建 renderer 后减少字形集和字号变化 |
| `image atlas budget exhausted` | 图集空间不足；已释放图片的槽位只供同尺寸图片复用，否则需重建 renderer，并控制图片尺寸/数量 |
| 层数、层深度或像素预算错误 | 减少 CompositedOpacity 隔离层；每层按整个视口分配，见[帧限制](api/native.md#帧与资源限制) |
| `unbalanced frame scopes` / 栈下溢 | 核对 PushClip/PopClip、BeginLayer/EndLayer 的配对顺序 |
| 自定义宿主 Ctrl+C/V 不工作 | 设置 Document.Clipboard；DesktopWindow 已注入内部实现 |
| TextBox 鼠标点不到中间光标 | 当前 PointerDown 定位到末尾；精确鼠标定位和拖选未实现 |
| 程序改 Text 后不能撤销之前输入 | Text setter 改值会清空该 TextBox 的输入历史 |
| Run(frameLimit) 等待不退出 | frameLimit 数的是循环次数，空闲会 WaitMessage；确定步进测试同时传 tick |

## 资源与导出

| 现象 | 检查与处理 |
| --- | --- |
| 资源 JSON 被拒绝 | 检查 version/root/id/kind 必填项、camelCase 属性、枚举字符串和未知字段 |
| 节点 ID 不合法 | 使用无连字符的 GUID N 格式，同一资源中不能重复 |
| Stretch 尺寸与预期相反 | Width/Height 在该模式表示右/下边距，不是最终宽高 |
| 运行时按钮没有业务行为 | 为 UiAssetRuntime.Build / Screen.Create 提供 command 回调；Command 只是名称 |
| Slider/TextBox 的 Command 无效果 | 当前 Command 只绑定 Button/Toggle 点击；其他控件通过 Nodes 找到实例再订阅 Changed |
| 主题没有随资源变化 | Build 返回的 UiInstance 不携带主题；设置 window.Document.Theme，或使用带 darkTheme 参数的 CreateDocument |
| 编辑后持有的 UiNode 不是最新模型 | EditorSession 会换成新的模型快照；保留 ID 并重新 Find |
| 保存报目录不存在 | EditorSession.Save 不自动创建父目录，先创建目录 |
| `Export folder already exists` | 选择一个新的输出文件夹路径；导出不会覆盖现有目录 |
| 插件无导出器 / ID 冲突 | 类型须公开、非抽象、实现 IUiExporter、有无参构造；Catalog 内 ID 忽略大小写唯一 |
| 插件依赖加载失败 | 使用与宿主兼容的 .NET/契约版本；当前无插件私有依赖解析器 |
| 多个 Screen.g.cs 编译重名 | 生成入口固定为 YoUI.Generated.Screen，调整不同界面的类名或命名空间 |

资源的详细数值、文本长度、文件字节限制见[格式校验](api/assets.md#版本-1-校验规则)；导出文件路径、数量和总大小限制见 [ExportCatalog](api/editor.md#exportcatalog)。

## 选择正确的验证

纯逻辑验证过程不创建 GPU。但 Editor 测试会构建引用的工具工程，其复制目标需要先有匹配配置的原生 DLL：

```powershell
cargo build --workspace --release --locked
dotnet run --project tests/YoUI.Tests -c Release
dotnet run --project tests/YoUI.Editor.Tests -c Release
```

Editor 测试还会编译生成代码和示例插件，需要 .NET SDK。上述检查不等价于 GPU 或原生窗口验收。

完整验证：

```powershell
./scripts/verify.ps1 -Configuration Release
```

`cargo test` 默认忽略需要真实 GPU 的测试；完整脚本会显式执行该测试。截图生成后还需检查可见布局，原生窗口输入另有消息检查。历史证据及未验收事项见[验证记录](verification.md)。
