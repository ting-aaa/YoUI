# 原生 API：Rust 与 C ABI

[API 索引](README.md) · [C# 绘制绑定](rendering.md) · [架构约定](../architecture.md)

`youi-render` 不依赖 C#、控件树或主循环。`youi-scene` 是可选的命令生产者。当前运行验收为 Windows x64 / DX12、Vulkan、原生 OpenGL；无窗口入口仍使用真实 GPU。创建前可通过 `YOUI_BACKEND` 严格选择 API，默认 DX12，详见[后端验证](../render-backends.md)。

源码：[IR](../../native/youi-render/src/ir.rs)、[Renderer](../../native/youi-render/src/renderer.rs)、[C ABI](../../native/youi-render/src/ffi.rs)、[C 头文件](../../native/youi-render/include/youi.h)、[Scene](../../native/youi-scene/src/lib.rs)。

## Rust 模块

`youi_render` 公开 `ir`、`renderer`、`ffi` 模块，并在 crate 根导出 `Rect`、`Command`、`FrameStats`、`validate` 和 `Renderer`。内部文字模块不作为独立公共 API。

`youi_scene` 公开 `NodeId`、`Transform2D`、`Mesh`、`Scene`。两个 crate 当前都通过 workspace 源码使用。

## Render IR

`Rect` 是 `#[repr(C)]` 的四个 f32：`x, y, w, h`。

| 方法 | 语义 |
| --- | --- |
| `Rect::new(x: f32, y: f32, w: f32, h: f32) -> Self` | 构造，不自动校验 |
| `intersect(self, b: Self) -> Self` | 返回矩形交集，宽高最低为零 |
| `valid(self) -> bool` | 坐标尺寸有限，宽高非负 |

`Command` 为 80 字节的 `#[repr(C)]` 结构：

```text
kind: u32, flags: u32, text_offset: u32, text_length: u32,
rect: Rect,
color: [f32; 4], color2: [f32; 4],
radius: f32, font_size: f32, reserved: [f32; 2]
```

| kind | 操作 | 字段 |
| --- | --- | --- |
| 0 | 圆角矩形、纵向渐变 | rect、color/color2、radius |
| 1 | 文字 | rect、color、font_size、text_offset/text_length；flags bit 0=粗体 |
| 2 / 3 | 压入 / 弹出矩形裁剪 | 压入使用 rect，嵌套求交 |
| 4 / 5 | 开始 / 结束隔离层 | color[3] 为整层透明度；rect 不定义局部层范围 |
| 6 | 任意平色三角形 | rect.xy=p0，rect.wh=p1，reserved=p2；此时 w/h 是坐标，不是尺寸 |
| 7 | 图像 | flags=图像 ID，rect=目标矩形，color=tint |

辅助构造器：

```text
Command::rect(rect: Rect, color: [f32; 4], radius: f32) -> Self
Command::text(rect: Rect, color: [f32; 4], font_size: f32,
              offset: u32, length: u32) -> Self
Command::triangle(points: [[f32; 2]; 3], color: [f32; 4]) -> Self
validate(commands: &[Command], text: &[u8]) -> Result<(), String>
```

其他命令用字段和 `..Default::default()` 构造。默认颜色为透明黑；手工图像命令需要显式设置白色 tint `[1.; 4]`。Text 的 offset/length 是 UTF-8 **字节**，rect.w 控制换行且 rect 同时裁剪文字。

输入坐标为物理像素、左上原点、Y 向下。两套输入颜色都须为 0..1 的非预乘 sRGB RGBA。

### 帧与资源限制

| 限制 | 当前值 |
| --- | --- |
| 命令数 | ≤1,000,000 |
| UTF-8 帧缓冲 | ≤16 MiB，引用范围须完整且是有效 UTF-8 |
| 输出宽高 | 每边 1..8192 |
| 普通矩形 | 坐标与尺寸有限，宽高非负；圆角非负 |
| 字号 | 1..512 物理像素 |
| 裁剪深度 | ≤64 |
| 隔离层深度 / 每帧数量 | ≤8 / ≤16 |
| 隔离层像素预算 | 累计 ≤64 Mi pixels；当前每层分配完整视口 |
| 共用图集 | 2048×2048 RGBA8，字形与图像共享 |
| 单张图像 | 每边 1..2046，且能放入剩余图集空间 |

裁剪栈和层栈分别检查平衡；调用方按正常嵌套顺序配对。validate 不查询图像所属 renderer，只检查 ID 非零；真实资源有效性在 render 时校验。

没有多页图集、自动字形驱逐或异步资产管理。图像释放后同尺寸槽位可复用；排版缓存达到 512 项后整体清空。

## Renderer

公开字段 `adapter_name: String` 和 `stats: FrameStats`。构造是 unsafe，因为非零 HWND 的有效性和存活时间由调用者保证。

| 方法签名 | 返回值 / 语义 |
| --- | --- |
| `unsafe Renderer::new(width: u32, height: u32, hwnd: isize) -> Result<Self, String>` | hwnd=0 离屏；非零为本进程有效窗口 |
| `resize(&mut self, width: u32, height: u32) -> Result<(), String>` | 改变输出尺寸并清空离屏层池 |
| `measure(&mut self, text: &str, size: f32, width: f32, bold: bool) -> Result<(f32,f32), String>` | 返回文字宽高；width 有限非负，内部至少按 1 排版 |
| `upload_image(&mut self, width: u32, height: u32, rgba: &[u8]) -> Result<u32, String>` | 紧密排列的 straight-alpha RGBA8 sRGB，长度必须等于 width×height×4 |
| `release_image(&mut self, id: u32) -> Result<(), String>` | 释放本 renderer 的图像；失效 ID 报错 |
| `render(&mut self, commands: &[Command], text: &[u8]) -> Result<FrameStats, String>` | 校验并按顺序提交完整一帧，返回统计 |
| `read_pixels(&self) -> Result<Vec<u8>, String>` | 阻塞 GPU 读回，返回自上而下、无行填充的 RGBA 字节 |
| `save_png(&self, path: &str) -> Result<(), String>` | 阻塞读回并覆盖写出 PNG；父目录须存在 |

renderer 通过 Rust drop 释放，没有额外 destroy 方法。停止使用图像，drop renderer，再销毁 HWND。图像 ID 只能用于创建它的 renderer，释放后不再可用。

render 每帧从透明背景绘制，仅相邻兼容操作合批。返回前已读完调用方的命令和文本，但不等待 GPU 执行完毕。measure 与绘制共用文字系统，可能生成字形并占用 GPU 图集。

Surface lost/outdated 会重配后重试一次；呈现超时可能跳过窗口呈现但仍返回统计。当前不提供完整设备丢失重建。

read_pixels / save_png 保存的是 sRGB 目标中的预乘内容，未做反预乘；透明输出不能直接视作 straight-alpha 图像回传上传。截图示例先画不透明背景，避免此问题。读回适合导出和验证，不宜每帧调用。

### FrameStats

48 字节的 `#[repr(C)]` 结构：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `commands`、`instances`、`draw_calls`、`layers` | u32 | 命令、实例、绘制调用、隔离层数 |
| `upload_bytes` | u64 | 实例缓冲上传量 |
| `prepare_us`、`submit_us` | u64 | CPU 准备 / 编码提交微秒 |
| `atlas_glyphs`、`text_cache_hits` | u32 | 累计缓存字形数 / 本次渲染文字缓存命中 |

instances 不含最终窗口呈现实例，draw_calls 在确实呈现时多一次。upload_bytes 包含预留的合成实例。时间不是 GPU 时间或 FPS，详见 [C# 字段说明](rendering.md#framestats)。

### Rust 独立示例

完整程序，需要依赖本地 `youi-render`：

```rust
use youi_render::{Command, Rect, Renderer};

fn main() -> Result<(), String> {
    // No HWND is borrowed in this offscreen example.
    let mut renderer = unsafe { Renderer::new(400, 240, 0)? };
    let text = "Hello, native YoUI".as_bytes();
    let commands = [
        Command::rect(Rect::new(0., 0., 400., 240.), [0.05, 0.07, 0.1, 1.], 0.),
        Command::text(Rect::new(24., 24., 352., 48.), [1.; 4], 24., 0, text.len() as u32),
    ];
    let stats = renderer.render(&commands, text)?;
    renderer.save_png("youi-native.png")?;
    println!("{}: {:?}", renderer.adapter_name, stats);
    Ok(())
}
```

仓库中已有更多可运行示例：

```powershell
New-Item -ItemType Directory -Force artifacts
cargo run -p youi-render --example render_scene --locked -- artifacts/render-scene.png
cargo run -p youi-scene --example scene_graph --locked -- artifacts/scene-graph.png
```

## Scene

`youi_scene` 是可选场景树，只生成渲染命令，不拥有 renderer、窗口或循环。

`NodeId` 的 index/generation 私有，支持 Copy/Clone/Eq/Debug。必须与产生它的 Scene 一起使用：generation 防止删除后槽位复用误认旧节点，但 ID 本身没有 Scene 所有者标记，不保证检测跨 Scene 错用。

`Transform2D` 的公开字段：

- `position: [f32;2]`，默认 `[0,0]`。
- `scale: [f32;2]`，默认 `[1,1]`，允许有限负值。
- `rotation: f32`，默认 0，单位弧度；Y 向下时正角度视觉上为顺时针。

局部变换依次缩放、旋转、平移，再与祖先变换组合。

### Mesh

| 方法 | 语义 |
| --- | --- |
| `Mesh::new(vertices: Vec<[f32;2]>, indices: Vec<u32>) -> Result<Self,String>` | 顶点有限、索引有效、索引数量为 3 的倍数 |
| `Mesh::rectangle(width: f32, height: f32) -> Result<Self,String>` | 创建四顶点两三角形；调用者通常提供正尺寸，当前不单独拒绝有限负尺寸 |

网格没有 UV/纹理或逐顶点色。颜色在节点 set_mesh 时指定。用 `Arc<Mesh>` 在多个节点共享。

### Scene 方法

`Scene::default()` 创建空场景，`rebuilds: u64` 是公开命令重建计数。

| 方法签名 | 行为 |
| --- | --- |
| `create(&mut self, parent: Option<NodeId>) -> Result<NodeId,String>` | None 创建根，否则追加到父节点 |
| `set_transform(&mut self, id: NodeId, t: Transform2D) -> Result<(),String>` | 设置有限的局部变换 |
| `set_mesh(&mut self, id: NodeId, mesh: Arc<Mesh>, color: [f32;4]) -> Result<(),String>` | 设置共享网格和 0..1 颜色 |
| `set_visible(&mut self, id: NodeId, visible: bool) -> Result<(),String>` | 隐藏时跳过整个子树 |
| `set_opacity(&mut self, id: NodeId, opacity: f32) -> Result<(),String>` | 有限 0..1，累乘到后代图元 alpha |
| `reparent(&mut self, id: NodeId, parent: Option<NodeId>) -> Result<(),String>` | 保留局部变换，追加到新父/根序列末尾；拒绝环和过深层级 |
| `remove(&mut self, id: NodeId) -> Result<(),String>` | 递归删除子树，旧 ID 失效 |
| `prepare(&mut self) -> &[Command]` | 脏时重建全部命令并增加 rebuilds，否则返回缓存切片 |

深度上限按父子链接计数为 128，根深度为 0。即使 reparent 到同一父级，也会移到末尾。顺序为根/兄弟插入顺序，深度优先，先节点自身 mesh 再子节点。

set_opacity 是逐图元继承，**不产生隔离层**，不能用作重叠子节点的整体透明度。Scene 当前不提供文字/图片节点、裁剪、动画调度、组件生命周期、公开节点查询接口或 C ABI。

场景接入示例（可作为完整程序）：

```rust
use std::sync::Arc;
use youi_render::Renderer;
use youi_scene::{Mesh, Scene, Transform2D};

fn main() -> Result<(), String> {
    let mut scene = Scene::default();
    let node = scene.create(None)?;
    scene.set_mesh(node, Arc::new(Mesh::rectangle(120., 60.)?), [0.4, 0.9, 0.7, 1.])?;
    scene.set_transform(node, Transform2D {
        position: [64., 64.], rotation: 0.2, ..Default::default()
    })?;
    let mut renderer = unsafe { Renderer::new(320, 240, 0)? };
    renderer.render(scene.prepare(), &[])?;
    Ok(())
}
```

## C ABI

包含 [youi.h](../../native/youi-render/include/youi.h)。Windows 使用 `__cdecl`，renderer ID 是 uint64_t，图像 ID 是 uint32_t，HWND 参数是 intptr_t。ABI 版本为 `0x00010000`，command=80 字节，stats=48 字节，使用自然对齐，不要启用改变结构布局的 packing。

`youi_rect`、`youi_command`、`youi_frame_stats` 的字段名和顺序对应上面的 Rust Rect / Command / FrameStats；完整声明以头文件为准。

### 函数签名

```c
uint32_t YOUI_CALL youi_abi_version(void);
uint32_t YOUI_CALL youi_command_size(void);
int32_t YOUI_CALL youi_create(uint32_t width, uint32_t height,
                            intptr_t hwnd, uint64_t* result);
int32_t YOUI_CALL youi_destroy(uint64_t renderer);
int32_t YOUI_CALL youi_resize(uint64_t renderer, uint32_t width, uint32_t height);
int32_t YOUI_CALL youi_render(uint64_t renderer,
                            const youi_command* commands, uint32_t count,
                            const uint8_t* text, uint32_t length,
                            youi_frame_stats* stats);
int32_t YOUI_CALL youi_measure(uint64_t renderer,
                             const uint8_t* text, uint32_t length,
                             float size, float max_width, uint32_t bold,
                             float* width, float* height);
int32_t YOUI_CALL youi_upload_image(uint64_t renderer,
                                  uint32_t width, uint32_t height,
                                  const uint8_t* rgba, uint32_t length,
                                  uint32_t* image);
int32_t YOUI_CALL youi_release_image(uint64_t renderer, uint32_t image);
int32_t YOUI_CALL youi_save_png(uint64_t renderer,
                              const uint8_t* path, uint32_t length);
uint32_t YOUI_CALL youi_last_error(uint8_t* output, uint32_t capacity);
int32_t YOUI_CALL youi_adapter_name(uint64_t renderer,
                                  uint8_t* output, uint32_t capacity,
                                  uint32_t* written);
```

| 函数 | 要点 |
| --- | --- |
| `youi_abi_version / youi_command_size` | 初始化前检查 ABI 版本和本地结构大小 |
| `youi_create` | result 必须可写；先置零，成功写入非零 ID |
| `youi_destroy` | 释放 renderer；失效或重复销毁返回错误 |
| `youi_resize` | 限制同 Rust Renderer |
| `youi_render` | count 是**命令条数**；length 是 UTF-8 缓冲字节数；stats 必须可写 |
| `youi_measure` | bold 任意非零即粗体；输出两个物理像素尺寸 |
| `youi_upload_image / youi_release_image` | 字节长度与所有权限制同 Rust Renderer |
| `youi_save_png` | path 为 UTF-8 字节，长度不包含 NUL；不要求 NUL 终止 |
| `youi_last_error` | 返回错误文本完整字节数，最多复制 capacity 字节，不补 NUL |
| `youi_adapter_name` | output/written 非空；written 是实际复制字节数，不是所需容量；不补 NUL |

除 version/size/last_error 外，返回 **0=成功、-1=参数/资源/设备等错误、-2=捕获到 Rust panic**。失败时在原调用线程立即读取 last_error；成功调用不会清空旧错误文本。panic 后不承诺实例可恢复，不能盲目重试。

`youi_last_error(NULL,0)` 可查询长度，再分配缓冲读取；如需 C 字符串，调用者额外分配并写入末尾 NUL。adapter_name 没有相同的长度查询协议，缓冲太小会截断，甚至截断 UTF-8 多字节字符。

命令 count=0 或字节 length=0 时对应输入指针可为空。非空输入必须指向正确对齐、足量、真实有效的内存；所有输出指针必须可写。ABI 无法证明任意地址有效。借用仅持续当前调用，不保留调用方缓冲。

除 render 命令数组外，共用字节输入入口也有 16 MiB 上限。所有经 renderer 注册表的操作串行执行；这不表示 C# 控件树可以多线程修改。ABI 没有 read_pixels 导出，像素导出使用 save_png。

### C 接入示例

以下程序画一张 128×128 不透明色块，输出到当前目录：

```c
#include "youi.h"
#include <stdio.h>
#include <stdlib.h>

static int check(int32_t status) {
    if (status == 0) return 1;
    uint32_t length = youi_last_error(NULL, 0);
    uint8_t* message = (uint8_t*)malloc((size_t)length + 1);
    if (message != NULL) {
        youi_last_error(message, length);
        fwrite(message, 1, length, stderr);
        fputc('\n', stderr);
        free(message);
    }
    return 0;
}

int main(void) {
    if (youi_abi_version() != 0x00010000 ||
        youi_command_size() != sizeof(youi_command) ||
        sizeof(youi_frame_stats) != 48) return 1;
    uint64_t renderer = 0;
    if (!check(youi_create(128, 128, 0, &renderer))) return 1;
    youi_command command = {0};
    command.rect.w = command.rect.h = 128;
    command.color[1] = command.color2[1] = 0.7f;
    command.color[3] = command.color2[3] = 1;
    youi_frame_stats stats = {0};
    int32_t status = youi_render(renderer, &command, 1, NULL, 0, &stats);
    const uint8_t path[] = "youi-c.png";
    if (status == 0)
        status = youi_save_png(renderer, path, (uint32_t)(sizeof(path) - 1));
    int succeeded = check(status);
    int released = check(youi_destroy(renderer));
    return succeeded && released ? 0 : 1;
}
```

保存为 `artifacts/native-example.c`，先从仓库根目录构建原生库，再在 **x64 Native Tools Command Prompt / 已配置 MSVC 的终端**编译。例如：

```powershell
cargo build -p youi-render --release --locked
cl /nologo /W4 /TC /I native/youi-render/include artifacts/native-example.c /Foartifacts/native-example.obj /Feartifacts/native-example.exe /link target/release/youi_render.dll.lib
Copy-Item -LiteralPath target/release/youi_render.dll -Destination artifacts/youi_render.dll
./artifacts/native-example.exe
```

`cl` 需要 MSVC 环境，普通终端未必已配置。Rust MSVC cdylib 生成 `youi_render.dll.lib` 供链接，运行时 DLL 必须可被 exe 找到。此示例只使用离屏 GPU，不创建原生窗口。
