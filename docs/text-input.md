# A08：文字命中与单行输入基础

本阶段提供与绘制共源的文字几何，以及单行 `TextBox` 的点击定位、Shift 点击和捕获拖选。不是完整文本编辑器、跨平台输入法或无障碍实现。

## 同一份塑形数据

`LayoutCache` 的同一次 cosmic-text 塑形产生绘制 glyph、advance cell 和测量尺寸。缓存键包含全文、字号、布局宽度、粗体和换行选项；命中不重新测量前缀。cell 保留空格等没有 raster 的 advance。字体回退和多个 glyph 的同一 cluster 合并后，按 extended grapheme cluster 分成编辑单元；一个 ligature 覆盖多个字素时等分其 advance，RTL 顺序反向，不读取字体 GDEF ligature caret 表。

cosmic-text 0.14 在极窄宽度下可能把一个 Indic 字素的多个 glyph 分到不同视觉行。检测到这种情况时，对同一份已塑形 glyph 先按不可分的字素/cluster advance 进行原子换行，再恢复原始 glyph、字体、偏移和 bidi level；绘制与 cell 都使用修正后的 runs，不按单字素重新塑形。一个字素比布局宽度更宽时允许完整溢出。若段落确实需要这项原子换行修正，且该段落内还存在跨不同 bidi embedding level 的字素，整个段落保守回退为不自动换行，以保留原有双向顺序；跨级字素不一定就是最初被拆行的那个字素。这样不会在重新流排时把它再次拆开，也不人为决定新的 bidi level。长段落可能因此整体超出给定宽度，但 hit/caret/cell 和绘制仍一致。若原排版没有拆开任何字素，则不触发修正或回退，保持原有换行。这是本阶段明确的自动换行限制。

部分有效 prepend/combining/bidi 文本会使 cosmic-text 产生 end≤start 的退化 glyph cluster 范围。转换层保留 glyph 的逻辑 start 锚点和所有绘制信息，把这种范围归入该锚点所在字素（末尾锚点归入最后字素），并统一钳制到段落边界；不交换锚点、不丢弃可见 glyph。分行检测、原子换行和几何 cell 使用同一规范化规则，避免合法文本触发越界或整数下溢。

普通 `Measure` 和 `DrawList.Text` 默认继续换行。新增 `wrap: false` 使用 cosmic-text `Wrap::None`，宽度仍用于行对齐。绘制旗标 bit 0=bold，bit 1=no-wrap，bit 2=不使用该文字命令自身的矩形裁剪；最后一项不会绕过 clip stack。`TextBox` 使用 `wrap: false, clip: false`，再用自己的视口裁剪，这样水平滚动不会改变排版宽度或提前截断右侧内容。`Append` 和 `CopyScaledTo` 保留这些旗标。

显式 CR/LF/CRLF/LFCR 与空行按照 native buffer 的段落规则处理；末尾换行现在保留末尾空行，测量高度因此包含这一行。自动换行丢弃的空白以相邻实际 shaped edge 上的零宽 cell 保留索引。没有可见 advance 的换行/格式字符不生成选择色块；行末没有额外的“选中换行”标记。

## C# API

`ITextMeasurer` 仍可独立实现。`ITextLayoutProvider : ITextMeasurer` 新增：

```csharp
TextLayout Layout(string text, float size, float width,
                  bool bold = false, bool wrap = true);
```

`NativeRenderer` 实现该接口。必须在 renderer 创建线程调用，字号为 1..512，宽度有限且非负（内部至少按 1 排版）。快照生成不上传 atlas；创建 NativeRenderer 本身仍需图形适配器。返回的 `TextLayout` 完全独立，renderer 释放后仍可使用，不需要 Dispose。匹配原生库必须包含新增符号，ABI v1 本身不足以保证这些可选新增接口存在。

- `TextCell(Start, End, Line, Bounds, RightToLeft)`：索引均为 UTF-16 code unit offset，范围 `[Start, End)`；Line 是从零开始的视觉行号，Bounds 是相对文字原点的 advance 矩形，不是 ink。空行用 Start=End、零宽 cell 表达。
- `CaretStops`：由该 provider 的 cell 端点产生的只读、排序 UTF-16 编辑边界；精确控件以它为唯一边界来源。
- `TextPosition(Index, Affinity)`：索引加 `Upstream` / `Downstream`。同一索引在自动换行或 bidi 边界可能有两个位置：Upstream 优先前一逻辑 cell 的 end，Downstream 优先后一逻辑 cell 的 start。
- `GetCaret(position)`：返回 1.5 单位宽的 caret 矩形。索引钳制到 `[0, Text.Length]`；落在字素内部时 Upstream 向前、Downstream 向后吸附。格式字符缺少自身 advance 时使用最近的逻辑 cell 边界。
- `HitTest(Vector2)`：最近视觉行、最近 cell edge；返回字素边界和 affinity。越界有限坐标钳制到最近 edge；NaN/Infinity 抛 `ArgumentOutOfRangeException`。完全相等的左右距离选择逻辑尾边缘。
- `GetSelection(start, end)`：接受正向/反向逻辑范围，钳制端点并向外扩至完整字素；合并同一行相邻的 advance 矩形，保留 bidi 分离片段。相等端点返回空列表。
- `TextLayout(string, IEnumerable<TextCell>)`：自定义排版器/测试夹具的入口。复制 cell 数组，检查有效 UTF-16、scalar 端点（不能切断 surrogate pair）、有限非负宽度和正高度；空文本也须提供空行 cell。调用者负责 cell 端点遵守其排版器的字素分段、几何确实对应自己的塑形结果；构造器不使用另一套 Unicode 表再次拆分 cell。

UTF-16 code unit、Unicode scalar 和 extended grapheme cluster 不可互换。`😀` 为两个 UTF-16 unit，一个 scalar；`é` 和 `👩‍🚀` 可能包含多个 scalar，却各为一个编辑字素。公共输入拒绝孤立 surrogate，避免 .NET UTF-8 替代编码悄悄移动索引。精确排版的边界规则以 provider cell 为准。native 使用固定的 unicode-segmentation 依赖；.NET `StringInfo` 还受运行时/系统 Unicode 表影响，两者可能不同，例如 Indic `क्षि`。因此有 provider 时，C# 导航、删除和编辑后吸附一律使用 `CaretStops`，不混用 `StringInfo` 分段。

## Rust / C ABI

Rust 导出 `TextCell` 和 `Renderer::text_cells(text, size, width, bold, wrap) -> Result<Vec<TextCell>, String>`。Rust/C 的 Start/End 是原字符串中的**绝对 UTF-8 字节偏移**，不是 C# UTF-16；C# 绑定建立一次精确 scalar boundary 映射，然后验证字素边界。

`youi_text_cell` 为 32 字节，顺序是四个 uint32（start, end, line, flags）及四个 float（x, y, width, height）。cell flags bit 0 表示 RTL。通过 `youi_text_cell_size()` 核对布局。

```c
int32_t youi_text_cells(uint64_t id, const uint8_t *text, uint32_t len,
    float size, float max_width, uint32_t flags,
    youi_text_cell *cells, uint32_t capacity, uint32_t *count);
```

输入 flags bit 0=bold、bit 1=no-wrap，其他位拒绝；这里没有绘制专用的 bit 2。capacity/count 单位是 cell 个数。第一次传 NULL/0 获取总数，第二次传自己的数组；容量不足返回错误、写出所需 count，但不部分写入 cell。其他错误不改输出。调用同步完成，不保留指针。指针须有效、自然对齐、输出不互相重叠也不覆盖输入，细节见[头文件](../native/youi-render/include/youi.h)。

## TextBox 行为

```csharp
var input = root.Add(new TextBox { Text = "office · é · 👩‍🚀" });
input.Select(0, 6);                   // UTF-16 start/length
input.Select(input.Text.Length, -5); // 支持反向选择
Console.WriteLine(input.CaretIndex);
Console.WriteLine(input.SelectedText);
```

- `CaretIndex`、`SelectionStart`、`SelectionLength` 只读；`Select(start, length)` 对原始两端钳制，非空范围向外吸附，零长度向后吸附。负 length 保留反向 anchor/caret。程序选择清除预编辑，不改变文字或撤销历史。
- 点击定位，Shift 点击扩展原 anchor；拖动捕获指针，离开控件仍可扩展。PointerUp、PointerCancel、Blur、detach 终止拖动。使用 `VisualBounds`，包含祖先偏移；paint 与 hit 共用水平滚动原点。
- 左右箭头是**逻辑字素顺序**，没有视觉 bidi 导航；Home/End 到逻辑首尾。选区替换、剪切、Backspace/Delete、撤销/重做保持字素边界。插入/删除可能把相邻 combining mark、ZWJ 或 regional indicator 重新连接成字素，编辑后 caret 再吸附。
- setter、粘贴、TextInput、Composition 一致过滤控制字符及 Unicode 行/段分隔符，保留有效 Unicode；TextBox 不提供多行。非法 UTF-16 抛异常，原文本/历史不变。空的已提交输入或空粘贴不删除选区。
- Composition 是临时预编辑，绘制时替换选区并加下划线；TextInput 才提交。预编辑非空时不处理文字键盘导航或指针重定位，避免擅自提交/取消平台输入法。实际候选窗口、重转换、IME selection offsets 不在此 API 内。
- 未附加文档，或自定义 `ITextMeasurer` 未实现 `ITextLayoutProvider` 时，键盘/程序选择使用 .NET `StringInfo` 回退规则，不能承诺与 native Unicode 表一致。附加到精确 provider 时重新吸附已有选区；撤销/重做恢复旧 provider 下的历史位置时同样重新吸附，其后编辑使用当前 provider 边界。测量-only 文档仍可文字显示和键盘编辑，点击只聚焦，不绘制未经验证的 caret/selection 几何，也不提供精确拖选。

Windows DIP 适配器把字号/宽度映射到物理像素塑形，再把 cell 映射回文档单位；DPI 变化失效布局，PointerDown 转发 Shift。Editor Preview 也将 zoom 纳入最终塑形字号和宽度，hit 坐标按原预览变换还原。快照不跨这些动态缩放缓存。这里只做代码和逻辑覆盖，未据此声称新 Windows 窗口、混合 DPI 或真实 IME 验收通过。

## 验证与边界

- `cargo test --workspace --locked`：native 字素、空行/尾换行、wrap/no-wrap、RTL cell、共享缓存和 FFI 验证。
- `dotnet run --project tests/YoUI.Tests -c Debug`：确定性 geometry 夹具、UTF-16、选区编辑、捕获/取消、偏移/滚动、composition、flag 传播；夹具不是字体塑形证据。
- `scripts/verify.ps1 -Layer Gpu` 额外运行 native geometry/paint/ABI 和 `--gpu-text-input`：真实 NativeRenderer bridge、Unicode/双向几何和选区像素读回。
- `scripts/verify.ps1 -Configuration Debug|Release -Layer Logic|Gpu` 分开记录；Window/All 仍须 Windows 桌面。

没有多行编辑控件、触摸/多指针、视觉 bidi 键盘导航、词/段落导航、双击词选、定时越界自动滚动、富文本、密码模式、字体注册/GDEF caret、平台辅助技术或设备丢失恢复。software OpenGL 的读回只能证明该适配器的渲染路径，不能代替硬件 GPU、Windows 窗口或真实输入法验收。
