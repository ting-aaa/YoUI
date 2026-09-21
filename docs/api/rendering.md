# 绘制与原生绑定 API

[API 索引](README.md) · [原生 API 与帧限制](native.md) · [Windows 宿主](platform-windows.md)

命名空间：`YoUI`。源码：[Rendering.cs](../../managed/YoUI/Rendering.cs)。这些类型不依赖控件或原生窗口；创建 NativeRenderer 始终需要 GPU。

## ITextMeasurer

`(float Width, float Height) Measure(string text, float size, float width, bool bold = false)`。

返回给定字号、最大换行宽度和粗体设置下的文字尺寸。测量单位须与文档一致：`DesktopWindow` 使用内部 DIP 适配器；直接使用 NativeRenderer 时是物理像素。逻辑测试可自行实现，不代表 GPU 塑形已验证。

## DrawList

`sealed class DrawList`，`new DrawList()` 创建可复用命令列表和 UTF-8 缓冲区。

| 成员 | 行为 |
| --- | --- |
| `ReadOnlySpan<DrawCommand> Commands { get; }` | 当前命令的借用视图 |
| `ReadOnlySpan<byte> TextBytes { get; }` | 当前帧文本的 UTF-8 字节视图 |
| `void Clear()` | 重置命令数和文本长度，复用容量 |
| `void Rectangle(Rect rect, Color color, float radius = 0, Color? gradientEnd = null)` | 圆角矩形；gradientEnd 非空时作纵向渐变 |
| `void Text(Rect rect, string value, Color color, float size = 14, bool bold = false)` | 追加文字和 UTF-8；rect 决定布局宽度与文字裁剪区域 |
| `void PushClip(Rect rect)` / `PopClip()` | 压入/弹出矩形裁剪，嵌套求交 |
| `void BeginLayer(float opacity)` / `EndLayer()` | 隔离层，整体透明度；有限 opacity 钳制到 0..1 |
| `void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)` | 任意平色三角形 |
| `void Image(Rect rect, NativeImage image, Color? tint = null)` | 绘制图像；默认白色 tint |
| `void CopyScaledTo(DrawList target, float scale)` | 先清空 target，再复制并缩放坐标、字号、圆角 |
| `void Append(DrawList source, Vector2 offset, float scale = 1)` | 保留已有内容，追加缩放/平移后的命令并修正文本偏移 |

CopyScaledTo / Append 要求源目标不是同一对象，scale 有限且 >0，offset 有限，否则抛 ArgumentException。Span 在列表修改后不应继续持有。Image 不接管资源所有权，也不会为缓存帧延长图像生命期。

PushClip / PopClip 与 BeginLayer / EndLayer 必须保持平衡。保持范围按正常嵌套顺序使用，不交叉结束裁剪与层。仅相邻兼容项会合批，不可为减少 draw call 全局重排透明命令。

## DrawCommand

`[StructLayout(LayoutKind.Sequential)] struct DrawCommand`，布局为 80 字节：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `Kind`、`Flags` | uint | 操作类型及该操作的标志 / 图像 ID |
| `TextOffset`、`TextLength` | uint | UTF-8 缓冲的字节范围 |
| `Rect` | Rect | 操作矩形；三角形有特殊编码 |
| `Color`、`Color2` | Color | 颜色或纵向渐变端点 |
| `Radius`、`FontSize` | float | 圆角 / 字号 |
| `Reserved0`、`Reserved1` | float | 三角形第三个顶点 |

完整 kind 定义见[Render IR](native.md#render-ir)。普通应用用 DrawList 构造；Commands 只读，不提供直接追加任意 DrawCommand 的 C# 公共方法。

## NativeRenderer

`sealed class NativeRenderer : IDisposable, ITextMeasurer`。

`NativeRenderer(uint width, uint height, nint window = 0)`：

- 宽高为物理像素，每边 1..8192；window=0 为离屏目标。
- 非零 window 必须是本进程有效 HWND，并保持到 renderer 释放以后。
- 构造时校验 ABI 版本 `0x00010000` 和 DrawCommand 大小。
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

Measure 使用 renderer 的文字系统及共享图集，可能上传字形；不是无 GPU 的纯测量。当前没有 C# 字体家族、字体注册、磁盘图片解码或像素直接读回 API。

## NativeImage

`sealed class NativeImage : IDisposable`。由 UploadImage 创建，不提供公共构造函数或可公开修改的 ID。

RGBA 数据按行连续、每像素 4 字节，没有 stride 参数；单边须在 1..2046，总长度严格等于 `width × height × 4`。字形和图片共用 2048×2048 图集，实际可用容量可能更小。

`Dispose()` 释放图像槽位。释放后 DrawList.Image 立即拒绝该包装对象；已缓存的图像命令再次提交也会失败。只在原 renderer 上使用，不能跨 renderer 共享图像。推荐在 renderer 之前释放图像，尤其不要依赖终结器自动回收。

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
