# A11 / 组件图库与基础交互示例

[文档首页](README.md) · [控件 API](api/controls.md) · [外部组件](custom-components.md)

图库位于现有 Showcase 项目，以 `--gallery` 单独运行；不替换原来的工作区/性能演示。它是一组应用层示例，复用现有控件及公开 API，没有另建控件框架。

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

## 四个可切换页面

- Controls：普通、主要、禁用按钮，Toggle、Slider、裁剪/合成透明度和真正跨程序集的 RatingControl。Rating 使用应用持有的 RatingState；键盘、指针和“Set model to 5”演示双向同步。
- Collection：240 条固定记录，TextBox 筛选、VirtualList 虚拟化与键盘选择。筛选保留仍存在的记录身份；没有匹配项时清空选择，避免把旧索引当作新记录。数据相同数量的切换也刷新行。
- Layout：StackPanel 固定/增长尺寸、自由布局的居中 RectTransform，以及 PropertyGrid 编辑 Gap/Padding。输入先验证有限的 0..32 DIP，再改模型；Enter/失焦提交，非法值保留原布局。

- Rendering：应用生成的四角纹理、旋转 UV 的纹理网格、两种尺寸的九宫格，以及嵌套裁剪和整体透明度。Toggle 切换内层裁剪，Slider 改变整组透明度；重叠矩形不会在交叠处重复变暗。绘制只使用已有 DrawList API，不新增框架控件。

页面导航与主题/Reset 分为两行，在 640 DIP 宽下保持可读。顶部导航始终存在；切换页面会释放旧页的焦点和指针捕获，并把新焦点放在相应导航按钮。页实例保留以便往返比较；外部 RatingControl 随页面附着/分离订阅模型，再进入时读取最新状态。深浅主题共享同一 UiDocument。

顶部 Reset 打开确认弹窗，阻止背景交互。Cancel 或 Escape 保留值并恢复原焦点；Reset 恢复初始 rating、预览、筛选/选择、间距和 Rendering 的裁剪/透明度。重复打开不会覆盖原焦点；程序切页会先取消弹窗。

树、资源编辑与导出等更大场景继续由 [Editor](editor.md) 演示。图库不声称覆盖每个属性、所有控件组合或完整应用开发。

## 图像资源所有权

`GalleryRunner` 先创建渲染器，再创建 `GalleryRenderingResources` 并调用
`gallery.Rendering.SetResources(assets)`。图像属于这个渲染器；页面只借用资源，
切页不会释放它们。退出或失败时先 `SetResources(null)`、清理页面，再释放图像，
最后释放渲染器/窗口。第二张图像上传失败会释放第一张，异常继续向调用者报告。

无 GPU 的逻辑测试可以直接创建图库；Rendering 页显示资源未绑定提示。
若宿主重建渲染器，必须先解绑旧资源、停止提交旧帧，再重新上传并绑定新资源；
不能把旧资源或缓存 DrawList 提交给新渲染器。详见[重建合约](renderer-recovery.md)。

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
- `gallery-modal-dark.png`、`gallery-compact-0.png` 至 `gallery-compact-3.png`
- `gallery-rendering-dark.png`、`gallery-rendering-light.png`：Rendering 页上半部
- `gallery-rendering-detail-dark.png`、`gallery-rendering-detail-light.png`、`gallery-rendering-compact-detail.png`：滚动后的网格/九宫格/透明度细节
- `gallery-rendering-unclipped.png`：分发 Toggle/Slider 输入后的无遮裁剪对照
- `gallery-compact-focus.png`：紧凑视口键盘聚焦滚入可见区域
- `gallery-smoke.json`：实际适配器、视口、通过断言及输入证据边界

截图使用固定数据，不依赖时间或外部网络。请实际检查宽/窄视口文字、裁剪、焦点和弹窗。软件 Vulkan/llvmpipe 的通过只是软件适配器证据；不能当作真实硬件 GPU、Windows 原生输入、IME 或无障碍验收。详细边界见[分层验证](verification-layers.md)。

`--gpu-gallery-rendering` 在测试项目中独立检查生成纹理的 UV、九宫格边框、裁剪与嵌套透明度像素，以及资源失败和重建；Gpu 门禁包含此入口。[Alpha 清单](alpha-acceptance.md)区分已有能力、本轮证据和未完成的平台验收。
