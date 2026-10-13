# 已创建渲染器的能力与上限查询

[文档首页](README.md) · [Rust / C API](api/native.md) · [C# API](api/rendering.md) · [资源预算](resource-budgets.md)

本查询返回**已成功创建的 renderer** 所选后端、适配器类型和有限的有效上限。
它是只读诊断与应用输入预检的依据，不是创建前的支持探测、完整 feature-bit
清单、剩余容量查询或平台验收结论；不会选择另一后端、启用设备特性或扩大上限。

## API 与二进制兼容

- Rust：`Renderer::capabilities(&self) -> RendererCapabilities`。
- C：`uint32_t YOUI_CALL youi_renderer_capabilities_size(void)` 返回 32；
  `int32_t YOUI_CALL youi_renderer_capabilities(uint64_t renderer, youi_renderer_capabilities_t* output)` 写入快照。
- C#：`NativeRenderer.Capabilities` 返回 `RendererCapabilities` 值。

这是 **ABI v1 的新增符号**，ABI 版本仍为 `0x00010000`，不改变已有 command、
frame-stats、预算或资源使用量的布局。仅检查 ABI 版本不能证明旧库含有这些符号。
使用查询时须部署匹配的原生库，并核对 capabilities 结构大小；托管绑定对缺少
新增入口或布局不匹配报告明确错误，不能用猜测值代替实际查询。

size 函数无需 renderer，只说明当前库的结构布局，不提供设备支持信息。
能力查询需要有效且未销毁的 renderer。C 输出指针必须非 NULL、自然对齐、
指向至少 32 字节真实可写内存；库无法验证任意非空地址的有效性。
成功返回 0；失败遵循已有 -1 / -2 和 `youi_last_error` 规则，不写输出。
调用方须在失败的原线程立即保存诊断，不要将输出中的旧值当作本次结果。

## 固定字段与单位

`youi_renderer_capabilities_t` 按下列顺序自然对齐，共 **32 字节**；不要改变 packing。
Rust / C 使用 snake_case 字段名，C# 使用表中的 PascalCase 名称。

| 偏移 | Rust / C 字段 | C# 字段 | C 类型 | 含义 |
| --- | --- | --- | --- | --- |
| 0 | `backend` | `Backend` | `uint32_t` | 实际选中的后端，数值映射见下文 |
| 4 | `device_kind` | `DeviceKind` | `uint32_t` | 所选适配器报告的设备类型 |
| 8 | `max_target_dimension_2d` | `MaxTargetDimension2D` | `uint32_t` | 输出 / resize 的单边像素上限：8192 与已启用设备 `max_texture_dimension_2d` 的较小值 |
| 12 | `max_image_dimension_2d` | `MaxImageDimension2D` | `uint32_t` | 单次图像上传的单边像素上限 2046；固定图集中另需每边 1 像素 padding |
| 16 | `atlas_dimension_2d` | `AtlasDimension2D` | `uint32_t` | 固定单页共用图集的单边尺寸 2048 |
| 20 | `reserved` | `Reserved` | `uint32_t` | 固定返回 0，不承载功能位 |
| 24 | `max_instance_buffer_bytes` | `MaxInstanceBufferBytes` | `uint64_t` | 已启用设备 `max_buffer_size` 与创建配置 `max_instance_buffer_bytes` 的较小值，单位字节 |

稳定数值映射：

- `backend`：0 Other、1 Vulkan、2 Metal、3 Dx12、4 Gl、5 BrowserWebGpu。
- `device_kind`：0 Other、1 IntegratedGpu、2 DiscreteGpu、3 VirtualGpu、4 Cpu。

Rust / C# 对应枚举为 `RendererBackend` 和 `RendererDeviceKind`，底层均为 32 位
无符号值。C# `RendererCapabilities` 是只读结构：`Backend` / `DeviceKind`
使用这两个枚举，其余字段为四个 `uint` 和一个 `ulong`，与表中布局一致。

数值表示身份，不是支持等级。应用应保留 Other / 未识别值的处理路径，不能仅凭
后端或设备类型推断未列出的功能。Backend 来自创建时实际选中的适配器，不能用
`YOUI_BACKEND` 的请求值替代。DeviceKind 是适配器报告，不是独立硬件鉴定。
枚举包含 Metal / BrowserWebGpu 不代表 macOS / Web 已实现宿主或完成验收；
Cpu 软件适配器的成功也不构成真实硬件 GPU 或平台验收证据。

## 生命周期与错误边界

快照在同一 renderer 生命周期内不变。resize、图像上传 / 释放、glyph purge、
layer trim 和不同帧的保留资源用量不会改变它；重建 renderer 后重新查询。
能力查询不分配图集槽位、不提交帧、不等待 GPU，也不清除设备失败锁存。
与资源诊断一样，已锁存设备失败但仍存活的 renderer 可以查询该快照；这不证明
设备健康，也不授权继续提交。panic / 注册表不可用等错误仍按现有错误规则处理。

C / Rust 调用遵循原有 renderer 线程与生命周期约定。C# 的每次属性访问都检查
创建线程和 disposed 状态，不能因为数据不变而通过缓存绕过这些检查。
调用者已取得的值可以作为历史诊断保存；它不会延长 renderer 或设备的生命周期。

## 上限不是准入保证

- `MaxTargetDimension2D` 是每个轴的尺寸上限，不是可分配面积或显存保证。
  输出、完整视口离屏层、固定 atlas 的 texture payload 总预算仍同时生效。
- `MaxImageDimension2D` 描述空图集中单次上传的尺寸上限，不是剩余空间。
  glyph 与 image 共用一页；已有驻留与碎片会使尺寸合格的上传仍然失败。
  字节长度、资源所有权和原有输入校验也不改变。
- `MaxInstanceBufferBytes` 是单个实例缓冲的有效字节上限，不是当前容量、剩余
  容量或可绘制命令数。展开后的 glyph / 几何实例、合成预留和增长策略仍参与准入。
- 命令、文本、字号、裁剪深度、层深度 / 数量 / 像素预算，以及驱动分配或设备失败
  均有独立边界。处于所有公开上限以内，也不保证创建、上传、resize 或整帧成功。

保留分配快照使用 `ResourceUsage`；创建预算使用 `ResourceBudget`。
这些查询都不是驱动实际 VRAM 或进程峰值内存监控，不能由 capability 上限相减
推导可用显存。失败后的处理继续遵循[显式停止与重建](renderer-recovery.md)。

## 验证与 Alpha 范围

布局 / 大小、数值映射和上限取较小值可以用逻辑测试验证；实际查询需要记录同次
运行的源码、构建配置、真实选中的后端和适配器。软件适配器的离屏结果与硬件
GPU、原生窗口 / 输入、平台支持证据分别记录，未执行的项目保持 NOT RUN。

本查询只补充现有渲染诊断，不引入 Alpha 新门槛、不声明未实现的绘制功能，
也不代表 Alpha 已接受、建立 tag 或进入 Beta。阶段出口见[Alpha 验收清单](alpha-acceptance.md)。
