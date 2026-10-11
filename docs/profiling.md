# A12 可复现的 CPU 帧分析

Showcase 的 `--benchmark` 是离屏 CPU 构帧/渲染提交探针。它使用真实 renderer；软件 Vulkan 也会执行渲染，但不代表独立显卡，更不代表第二个平台的窗口/输入支持。完整执行入口和平台边界见[离屏平台验收](platform-acceptance.md)。

## 运行

先按[固定工具链](toolchains.md)构建相同配置的原生库和 Showcase。以下 PowerShell 在仓库根目录执行；Linux 也可使用已安装的 PowerShell 7。Bash 用户用 `export` 设置同名环境变量。

```powershell
./scripts/build.ps1 -Configuration Release
$env:YOUI_BACKEND = 'vulkan'
$env:YOUI_ARTIFACT_DIR = 'artifacts/profile-my-run'
dotnet run --project samples/YoUI.Showcase -c Release --no-build -- --benchmark --warmup 30 --frames 240
```

默认预热 30 帧、采样 240 帧；`--warmup` 允许 0–100000，`--frames` 允许 1–100000。重复、未知、缺失、负值或越界参数会在创建 renderer 前报错。零预热用于冷启动诊断，不得混作预热结果。采样数上限是内存/运行时间护栏，不是性能目标。

场景保持 1280×820、100000 条列表数据、一个全尺寸隔离层；模拟时间每帧推进 1/60 秒，没有实际 60 Hz 节流。`benchmark.json`（schemaVersion 2）包含完整样本和摘要，`benchmark-frame.png` 是最后一帧。普通直接运行会覆盖同名文件；审计/平台验收必须用新的输出目录，优先使用验收脚本。不要根据旧文件或进程日志尾部推断本轮通过。

## 指标定义

- `managedBuildMs`：只包住 `UiDocument.BuildFrame()`；不包括 `scene.Step()`。
- `nativePrepareMs`：原生计时器记录的准备阶段 CPU 时间。
- `nativeSubmitMs`：原生编码/提交阶段 CPU 时间，包括可能的驱动背压。不是纯 queue API 开销，也不是同步 GPU 执行时间。
- 不使用 GPU timestamp；没有每帧等待、读回或窗口呈现。最终 PNG 读回在采样之后执行。不能据此推算屏幕 FPS 或比较 GPU 吞吐量。
- 摘要的中位与 P95 沿用 `sorted[floor((N - 1) * p)]`，与某些插值/nearest-rank 工具不同；每帧原始值可自行重新计算。计时不得为非有限数或负数。
- 每帧导出 commands、instances、drawCalls、layers、uploadBytes、atlasGlyphs、textCacheHits 和 layoutPasses。glyph 数量是当前驻留数量，cache hits 是该帧命中；不是累计 miss 或全应用性能分析。
- `resourceUsageBefore/After` 复用 A07 保留分配快照：atlas、output、layer、instanceBuffer 的 payload 容量和 layer 数量。包括缓存容量，不包括 staging、驱动开销、已提交但未释放的旧对象；既不是显存峰值，也不是泄漏证明。
- `managedAllocatedBytes` 使用当前线程计数，范围为 Step + BuildFrame + Render + 样本赋值。预先分配样本数组；排序、JSON、资源查询和 PNG 在计数之外。其他线程/原生分配不包含。旧 schema 1 含有 List 增长等 bookkeeping；分配量不能直接与旧记录比较。

保留旧摘要键（含 `Commands` 等大小写）；`samples` 使用 camelCase。新字段记录实际 adapter/backend/type、请求后端、运行时、系统、进程架构、处理器数、配置、分辨率和预热/样本方法。适配器 `cpu` 是软件渲染，`virtual`/`other` 不能作为硬件证据。实际身份取自 native renderer 的适配器报告，不根据型号营销名称猜测。

## 证据与比较边界

直接运行 JSON 不冒充源码 provenance：`--no-build` 可能运行旧二进制。验收脚本负责先构建、记录 SHA/dirty/工具链/平台，并校验实际报告。需要定位回归时在同一设备、驱动、配置、工具链、场景和无并发构建的条件下进行多次独立运行，保留每次完整样本；不要以一次 P95 或不同机器数值作为门禁。

本阶段不引入 FPS/耗时目标，也没有长时 soak、设备丢失、GPU 时间戳、总显存或完整 UI profiler。CPU 统计/参数测试已经接入 Debug / Release Logic 层；它们不创建 renderer。实际渲染需显式运行 Gpu 层或平台验收脚本。
