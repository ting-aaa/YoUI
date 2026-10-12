# 绘制与原生绑定 API

[API 索引](README.md) · [原生 API 与帧限制](native.md) · [Windows 宿主](platform-windows.md)

命名空间：`YoUI`。源码：[Rendering.cs](../../managed/YoUI/Rendering.cs)。这些类型不依赖控件或原生窗口；创建 NativeRenderer 始终需要 GPU。

## ITextMeasurer

`(float Width, float Height) Measure(string text, float size, float width, bool bold = false)`。

返回给定字号、最大换行宽度和粗体设置下的文字尺寸。测量单位须与文档一致：`DesktopWindow` 使用内部 DIP 适配器；直接使用 NativeRenderer 时是物理像素。逻辑测试可自行实现，不代表 GPU 塑形已验证。

## ITextLayoutProvider / TextLayout

`ITextLayoutProvider : ITextMeasurer` 新增 `TextLayout Layout(string text, float size, float width, bool bold = false, bool wrap = true)`。NativeRenderer 和 Windows DIP 适配器实现此接口，返回与绘制共源的 UTF-16 字素 advance 几何。`TextLayout.HitTest`、`GetCaret`、`GetSelection` 处理命中、affinity 和选择矩形；返回快照独立于 renderer 生命周期。完整成员、构造验证、单位和边界见 [A08 文字输入](../text-input.md)。

## DrawList

`sealed class DrawList`，`new DrawList()` 创建可复用命令列表和 UTF-8 缓冲区。

| 成员 | 行为 |
| --- | --- |
| `ReadOnlySpan<DrawCommand> Commands { get; }` | 当前命令的借用视图 |
| `ReadOnlySpan<byte> TextBytes { get; }` | 当前帧文本的 UTF-8 字节视图 |
| `void Clear()` | 重置命令数和文本长度，复用容量 |
| `void Rectangle(Rect rect, Color color, float radius = 0, Color? gradientEnd = null)` | 圆角矩形；gradientEnd 非空时作纵向渐变 |
| `void Text(Rect rect, string value, Color color, float size = 14, bool bold = false, bool wrap = true, bool clip = true)` | 追加文字和 UTF-8；rect 决定布局宽度；wrap=false 禁止自动换行，clip=false 仅保留 clip stack 裁剪 |
| `void PushClip(Rect rect)` / `PopClip()` | 压入/弹出矩形裁剪，嵌套求交 |
| `void BeginLayer(float opacity)` / `EndLayer()` | 隔离层，整体透明度；有限 opacity 钳制到 0..1 |
| `void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)` | 任意平色三角形 |
| `void Image(Rect rect, NativeImage image, Color? tint = null)` | 绘制图像；默认白色 tint |
| `void TexturedTriangle(Vector2 a, Vector2 b, Vector2 c, Vector2 uvA, Vector2 uvB, Vector2 uvC, NativeImage image, Color? tint = null)` | 追加纹理三角形 |
| `void TexturedMesh(ReadOnlySpan<Vector2> positions, ReadOnlySpan<Vector2> uvs, ReadOnlySpan<int> indices, NativeImage image, Color? tint = null)` | 每 3 个索引生成三角形；全部校验后才追加，非法输入不留下部分命令 |
| `void NineSlice(Rect destination, NativeImage image, float left, float top, float right, float bottom, Color? tint = null)` | 源像素边距九宫格 |
| `void CopyScaledTo(DrawList target, float scale)` | 先清空 target，再复制并缩放坐标、字号、圆角 |
| `void Append(DrawList source, Vector2 offset, float scale = 1)` | 保留已有内容，追加缩放/平移后的命令并修正文本偏移 |

CopyScaledTo / Append 要求源目标不是同一对象，scale 有限且 >0，offset 有限，否则抛 ArgumentException。Span 在列表修改后不应继续持有。Image 不接管资源所有权，也不会为缓存帧延长图像生命期。

纹理辅助方法要求位置有限，UV 数量与位置一致、值有限且在 [0,1]，索引有效且数量为 3 的倍数。图像须仍有效，tint 为统一非预乘 sRGB RGBA，默认白色。NineSlice 要求目标尺寸有限非负、边距有限非负且源图像内不重叠；小目标按比例缩小相对边距，源或目标面积为零的格子省略。Append / CopyScaledTo 对 kind 8 只变换顶点位置，UV 不变。详见 [A06](../a06-textured-mesh.md)。

PushClip / PopClip 与 BeginLayer / EndLayer 必须保持平衡。保持范围按正常嵌套顺序使用，不交叉结束裁剪与层。仅相邻兼容项会合批，不可为减少 draw call 全局重排透明命令。

## DrawCommand

`[StructLayout(LayoutKind.Sequential)] struct DrawCommand`，布局为 80 字节：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `Kind`、`Flags` | uint | 操作类型及该操作的标志 / 图像 ID |
| `TextOffset`、`TextLength` | uint | UTF-8 缓冲的字节范围 |
| `Rect` | Rect | 操作矩形；三角形有特殊编码 |
| `Color`、`Color2` | Color | 颜色或渐变端点；kind 8 为 tint 和前两个 UV |
| `Radius`、`FontSize` | float | 圆角 / 字号；kind 8 为第三个 UV |
| `Reserved0`、`Reserved1` | float | 三角形第三个顶点 |

完整 kind 定义见[Render IR](native.md#render-ir)。普通应用用 DrawList 构造；Commands 只读，不提供直接追加任意 DrawCommand 的 C# 公共方法。

## NativeRenderer

`sealed class NativeRenderer : IDisposable, ITextLayoutProvider`。

`NativeRenderer(uint width, uint height, nint window = 0)`：

- 宽高为物理像素，每边 1..8192；window=0 为离屏目标。
- 非零 window 必须是本进程有效 HWND，并保持到 renderer 释放以后。
- 构造时校验 ABI 版本 `0x00010000` 和 DrawCommand 大小。kind 8 保持此布局，仍须使用包含 A06 的匹配原生库，版本/大小检查不能证明旧库支持新命令。
- 访问原生实例的方法须在创建线程调用，跨线程抛 InvalidOperationException。释放后再访问原生实例抛 ObjectDisposedException；缓存属性 Adapter 仍可读取，重复 Dispose 无操作。

| 成员 | 返回值 / 行为 |
| --- | --- |
| `string Adapter { get; }` | 适配器和后端描述 |
| `FrameStats Render(DrawList list)` | 校验并提交完整帧；返回 CPU 和绘制统计 |
| `void Resize(uint width, uint height)` | 重建输出与清空离屏层池；不修改 UiDocument 尺寸 |
| `NativeImage UploadImage(uint width, uint height, ReadOnlySpan<byte> rgba)` | 上传 RGBA8，返回本 renderer 拥有的图像包装对象 |
| `(float Width, float Height) Measure(string text, float size, float width, bool bold = false)` | 按物理像素测量；字号 1..512，宽度有限非负 |
| `void SavePng(string path)` | 将最近输出阻塞读回并写为 PNG；转换为绝对路径，不自动创建父目录，会覆盖同名文件 |
| `void Dispose()` | 在创建线程销毁 renderer；再次调用无操作 |

Render 同步借用命令与文本到返回为止，GPU 执行仍是异步。不要把“同步提交”理解为等待 GPU 完成。每帧重新从透明背景绘制，UiDocument 会额外发出主题背景。

原生失败被转为 `InvalidOperationException("YoUI native (status): ...")`。DLL 找不到、位数不匹配或入口不存在也可能直接抛 .NET 加载异常。具体错误码和容量限制见[原生 API](native.md)。

Measure / Layout 使用 renderer 的文字系统，生成布局但不上传字形；NativeRenderer 的构造仍需要图形适配器。当前没有 C# 字体家族、字体注册、磁盘图片解码或像素直接读回 API。

## NativeImage

`sealed class NativeImage : IDisposable`。由 UploadImage 创建，不提供公共构造函数或可公开修改的 ID。

`uint Width { get; }`、`uint Height { get; }` 保存上传时的源像素尺寸，供九宫格使用。

RGBA 数据按行连续、每像素 4 字节，没有 stride 参数；单边须在 1..2046，总长度严格等于 `width × height × 4`。字形和图片共用 2048×2048 图集，实际可用容量可能更小。

`Dispose()` 释放图像槽位，其空闲区域可复用给较小或同尺寸图片/字形，不影响其他 live 资源，也不保证消除碎片。释放后 DrawList.Image 及纹理辅助方法立即拒绝该包装对象；已缓存的图像命令再次提交也会失败。只在原 renderer 上使用，不能跨 renderer 共享图像。推荐在 renderer 之前释放图像，尤其不要依赖终结器自动回收。

## FrameStats

`readonly struct FrameStats`，48 字节，字段均只读：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `Commands` | uint | 输入命令数，包括裁剪和层控制命令 |
| `Instances` | uint | 图元 / 字形 / 合成实例数；不等于控件数，不含最终窗口呈现实例 |
| `DrawCalls` | uint | 离屏批次绘制加上实际窗口呈现绘制 |
| `Layers` | uint | 隔离层数量，不含根输出 |
| `UploadBytes` | ulong | 本帧实例缓冲上传字节，不是总 GPU 内存或全部资源上传流量 |
| `PrepareMicroseconds` | ulong | CPU 命令准备微秒 |
| `SubmitMicroseconds` | ulong | CPU 编码/提交微秒 |
| `AtlasGlyphs` | uint | 当前缓存字形数量 |
| `TextCacheHits` | uint | 本帧原生渲染期间的文字缓存命中次数 |
| `ToString()` | string | 简要计数和 CPU 准备时间 |

不是 GPU 时间、输入延迟或 FPS。UploadBytes 包含预留的窗口合成实例，不能简单用 Instances 乘固定大小推算。

## 直接绘制示例

完整程序，引用 YoUI 并复制匹配 DLL，从仓库根目录运行：

```csharp
using YoUI;

Directory.CreateDirectory("artifacts");
using var renderer = new NativeRenderer(400, 240);
using var image = renderer.UploadImage(2, 2, new byte[]
{
    255, 80, 80, 255,    80, 255, 80, 255,
    80, 80, 255, 255,    255, 255, 255, 255
});
var frame = new DrawList();
frame.Rectangle(new Rect(0, 0, 400, 240), Color.Hex(0x0B1019));
frame.Rectangle(new Rect(16, 16, 368, 208), Color.Hex(0x1B293B), 12);
frame.Text(new Rect(32, 32, 320, 40), "Direct rendering", Color.Hex(0xEDF3FA), 24);
frame.PushClip(new Rect(32, 88, 336, 120));
frame.BeginLayer(.8f);
frame.Image(new Rect(32, 88, 96, 96), image);
frame.Triangle(new(160, 184), new(220, 88), new(280, 184), Color.Hex(0x76E5C0));
frame.EndLayer();
frame.PopClip();
Console.WriteLine(renderer.Render(frame));
renderer.SavePng("artifacts/direct-render.png");
```

PNG 导出目前直接保存 sRGB 渲染目标中的预乘内容，没有反预乘处理。对透明背景截图，不要假定它能作为非预乘 RGBA 图片直接回传 UploadImage；需要普通截图时先绘制不透明背景，如本例。

## A07 资源预算与快照

`new NativeRenderer(width, height, window, budget)` 接受 `ResourceBudget`；原构造函数保持不变。
`ResourceBudget.Default` 的 `MaxTextureBytes` 为 528 MiB，`MaxInstanceBufferBytes` 为 256 MiB。
`ResourceBudget` 属性返回创建配置，`ResourceUsage` 属性返回 `AtlasBytes`、`OutputBytes`、`LayerBytes`、`InstanceBufferBytes`、`LayerCount`（及为 0 的 `Reserved`）。查询遵循原有 owner-thread / disposed 检查。

```csharp
var budget = new ResourceBudget(32UL * 1024 * 1024, 4UL * 1024 * 1024);
using var renderer = new NativeRenderer(640, 480, 0, budget);
Console.WriteLine(renderer.ResourceUsage.AtlasBytes); // 16777216
```

快照按 GPU 对象保留容量计数，不是 live image 大小、FrameStats.UploadBytes 或驱动实际显存；释放图像不缩小 atlas，空帧不回收高水位层/缓冲。新属性/构造函数需要匹配的新 native binary；旧 ABI v1 缺少新增符号时明确报错。
创建、resize、帧增长的拒绝行为与排除项见 [资源预算](../resource-budgets.md)。

## Linux Xlib surface factory

`NativeRenderer.CreateXlib(uint width, uint height, nint display, nuint window, int screen)` 及末尾接收 `ResourceBudget` 的重载创建借用 Xlib 句柄的 renderer。它不创建窗口、事件循环或 SDL 依赖，也不使旧 HWND 构造函数接受 XID。仅 Linux 可调用；非空 display、非零 window 和非负 screen 必须真实有效并属于同一个窗口，构造后的 renderer 必须在原线程先于窗口/display 释放。空句柄、非法尺寸和预算在 GPU 初始化前拒绝；错误的非空原生指针仍是调用方责任。旧 native library 缺少新增符号时给出明确的版本匹配错误。

[`YoUI.Platform.Linux`](../linux-window.md) 封装这一生命周期；原型使用物理像素，无 Windows DIP 适配。原生窗口成功路径仍需独立验收。
