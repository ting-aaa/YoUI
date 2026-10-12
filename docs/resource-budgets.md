# A07：渲染器资源预算

本增量在现有 renderer 上增加**创建时固定的 GPU payload 分配上限**与只读快照。
这是分配准入检查，不是进程显存监控，也不承诺驱动 OOM、设备丢失后的恢复。
与 A06 alpha 修复独立；不改变颜色、采样或透明绘制顺序。

## 计入范围

| 项目 | 计数方式与生命周期 |
|---|---|
| AtlasBytes / atlas_bytes | 固定 2048×2048×4 = 16 MiB；glyph 与 image 共用。图片上传/释放改变占用，不改变纹理分配大小 |
| OutputBytes / output_bytes | 当前 width×height×4，RGBA8 输出目标 |
| LayerBytes / layer_bytes | 已保留层数量×width×height×4；按历史高水位保留，空帧不释放 |
| InstanceBufferBytes / instance_buffer_bytes | 当前 GPU 实例缓冲容量×112；初始 1024 个实例，按高水位复用 |
| LayerCount / layer_count | 已保留的离屏层数量，不是上一帧使用数量 |

不包含：CPU 实例/布局/字体/raster cache、atlas 空洞的逻辑占用、uniform、bind group、pipeline、驱动对齐/内部开销、surface/swapchain、PNG/readback staging buffer、上传 staging、GPU 在途旧分配。
resize 或缓冲替换时，新旧分配的短期重叠不计入上限，因此它不是峰值物理显存的硬保证。
尤其是 CPU 文本缓存仍不受此预算控制。

## API

Rust 的 `ResourceBudget` 为两个 u64：`max_texture_bytes` 和 `max_instance_buffer_bytes`。
默认分别为 **528 MiB** 与 **256 MiB**。前者容纳已有最大输出（256 MiB）、层像素总上限（256 MiB）及 atlas（16 MiB）；后者匹配当前请求的 wgpu 默认 max_buffer_size。
设备自身限制与已有 16 层 / 64 Mi layer pixels 限制仍同时生效。

```rust
use youi_render::{Renderer, ResourceBudget};
let budget = ResourceBudget {
    max_texture_bytes: 32 * 1024 * 1024,
    max_instance_buffer_bytes: 4 * 1024 * 1024,
};
let mut renderer = unsafe { Renderer::new_with_budget(640, 480, 0, budget)? };
renderer.render(&[], b"")?;
let usage = renderer.resource_usage();
assert!(usage.atlas_bytes + usage.output_bytes + usage.layer_bytes <= budget.max_texture_bytes);
# Ok::<(), String>(())
```

`Renderer::new` 保留原签名并使用默认预算；`resource_budget()` 返回配置快照。
预算不能运行时修改。零不是“无限制”；创建至少要容纳 atlas、输出和 1024×112 字节初始实例缓冲。

C ABI 增加 `youi_create_with_budget`、`youi_resource_budget`、`youi_resource_usage` 及对应 size 查询；见 [native API](api/native.md)。原有 ABI 版本、command/frame-stats 布局与函数保持不变。旧 native binary 没有这些新增符号，不能仅凭 ABI v1 推断支持预算。

.NET 增加 `ResourceBudget.Default` 与四参数 `NativeRenderer(width, height, window, budget)`，并提供 `ResourceBudget` / `ResourceUsage` 属性，见 [rendering API](api/rendering.md)。旧构造函数继续使用默认预算。

## 准入、复用与错误行为

- 创建在 GPU 初始化前检查目标尺寸、texture 总量与初始实例容量。
- resize 先检查新输出（成功后旧层被丢弃）。不通过则尺寸、输出、保留层与实例缓冲不变。同尺寸 resize 是 no-op，保留层不释放。
- 每帧在文字准备前检查所需层与历史保留层的较大值。层预算错误不会触发 glyph 上传。
- 实例在展开命令/文字时逐项检查，始终为末尾 presentation quad 预留一个位置，离屏模式也计入。
- 实例缓冲通常按 2 的幂增长；若舍入将超过预算/设备限制，但精确大小可容纳，则按精确大小分配。因此上限边界不会被预留容量误拒绝。
- 全部实例通过检查后才扩展 layer targets 和 GPU 实例缓冲。预算拒绝的帧不提交绘制，保留上一张输出、FrameStats、live image handles 与 GPU 分配快照；可以继续提交较小帧。
- 失败不是全部状态事务：准备中的 CPU 文本缓存、glyph atlas residency 可能已经变化；固定 atlas 的分配大小不变。不要把此保证推广到 GPU/驱动错误或 panic。
- 释放 image 的空闲区域可复用给较小或同尺寸 image/glyph；显式 PurgeGlyphResidency 可回收 glyph 区域；不合并碎片、不缩小 atlas；空帧不缩小层和实例缓冲。没有 LRU 驱逐、通用 trim、资源搬迁或设备重建。

## 验证

纯逻辑测试覆盖尺寸、溢出、精确上限/少一字节、设备上限及增长舍入。
GPU 测试覆盖真实分配快照、层/实例拒绝、无提交像素保持、失败 resize、同尺寸 no-op、成功 resize 清层、释放图片和空帧高水位复用。
`verify.ps1 -Layer Gpu` 还运行托管预算绑定回归（`--gpu-resource-budget`），覆盖 native/managed 往返。
按 Debug 与 Release 分别运行 [分层验收](verification-layers.md)。软件 GL 的结果不能替代 Windows/DX12/Vulkan、硬件 GPU 或原生窗口验收。

A06 的纹理 UV 边界将内部 GPU 实例从 96 扩展为 112 字节，初始实例缓冲需 114688 字节。原生计费由实际实例结构大小派生；C# 创建前校验使用相同下限。对外 Command 仍为 80 字节，ResourceBudget/ResourceUsage 结构和 ABI 版本不变。旧的精确字节预算若低于新下限会被明确拒绝，不会超额分配。
