# A11 / 组件图库与基础交互示例

[文档首页](README.md) · [控件 API](api/controls.md) · [外部组件](custom-components.md)

图库位于现有 Showcase 项目，以 `--gallery` 单独运行；不替换原来的工作区/性能演示，也不需要未合并的 A06 分支。它是一组应用层示例，复用现有控件及公开 API，没有另建控件框架。

## 运行

从仓库根目录，使用[固定工具链](toolchains.md)，先构建同配置原生库：

```powershell
./scripts/build.ps1 -Configuration Debug
dotnet run --project samples/YoUI.Showcase -c Debug --no-build -- --gallery
```

交互窗口仍仅支持 Windows。Linux 可运行逻辑测试和有适配器的离屏模式：

```bash
cargo build --workspace --locked --target-dir "$PWD/target"
dotnet run --project samples/YoUI.Showcase -c Debug -- --gallery --headless
```

Release 对应 Cargo 的 `--release` 与 .NET 的 `-c Release`，不要混用原生库。图库支持至少 **640×600 DIP** 的视口，页面可垂直滚动，键盘聚焦会将控件滚入视口。

## 三个可切换页面

- Controls：普通、主要、禁用按钮，Toggle、Slider、裁剪/合成透明度和真正跨程序集的 RatingControl。Rating 使用应用持有的 RatingState；键盘、指针和“Set model to 5”演示双向同步。
- Collection：240 条固定记录，TextBox 筛选、VirtualList 虚拟化与键盘选择。筛选保留仍存在的记录身份；没有匹配项时清空选择，避免把旧索引当作新记录。数据相同数量的切换也刷新行。
- Layout：StackPanel 固定/增长尺寸、自由布局的居中 RectTransform，以及 PropertyGrid 编辑 Gap/Padding。输入先验证有限的 0..32 DIP，再改模型；Enter/失焦提交，非法值保留原布局。

顶部导航始终存在；切换页面会释放旧页的焦点和指针捕获，并把新焦点放在相应导航按钮。页实例保留以便往返比较；外部 RatingControl 随页面附着/分离订阅模型，再进入时读取最新状态。深浅主题共享同一 UiDocument。

顶部 Reset 打开确认弹窗，阻止背景交互。Cancel 或 Escape 保留值并恢复原焦点；Reset 恢复初始 rating、预览、筛选/选择和间距。重复打开不会覆盖原焦点；程序切页会先取消弹窗。

树、资源编辑与导出等更大场景继续由 [Editor](editor.md) 演示。图库不声称覆盖每个属性、所有控件组合或完整应用开发。

## 验证与证据

```bash
dotnet run --project tests/YoUI.Tests -c Debug
```

逻辑回归纳入现有测试入口，覆盖页面往返、模型同步、选择重映射、模态取消和输入契约。它们通过 UiDocument 分发事件，不是操作系统键鼠验收。

```powershell
./scripts/verify.ps1 -Configuration Debug -Layer Logic
./scripts/verify.ps1 -Configuration Debug -Layer Gpu
# 仅 Windows 桌面：通过 PostMessage 的窗口 smoke，仍不是实体键鼠操作
dotnet run --project samples/YoUI.Showcase -c Debug --no-build -- --gallery --smoke
```

两种配置都应运行。Gpu 门禁包含 `--gallery --headless`，Window 门禁包含 `--gallery --smoke`；默认 All 包含两者。编排模拟测试只能证明命令与失败传播，不能代替真实渲染。

离屏模式在 `artifacts/`（可通过 `YOUI_ARTIFACT_DIR` 指定）输出：

- `gallery-controls-dark.png`、`gallery-controls-light.png`
- `gallery-collection-dark.png`、`gallery-layout-dark.png`
- `gallery-modal-dark.png`、`gallery-compact-0.png` 至 `gallery-compact-2.png`
- `gallery-compact-focus.png`：紧凑视口键盘聚焦滚入可见区域
- `gallery-smoke.json`：实际适配器、视口、通过断言及输入证据边界

截图使用固定数据，不依赖时间或外部网络。请实际检查宽/窄视口文字、裁剪、焦点和弹窗。软件 Vulkan/llvmpipe 的通过只是软件适配器证据；不能当作真实硬件 GPU、Windows 原生输入、IME 或无障碍验收。详细边界见[分层验证](verification-layers.md)。
