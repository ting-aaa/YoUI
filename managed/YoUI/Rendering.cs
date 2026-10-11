using System.Runtime.InteropServices;
using System.Text;

namespace YoUI;

[StructLayout(LayoutKind.Sequential)]
public struct DrawCommand
{
    public uint Kind, Flags, TextOffset, TextLength;
    public Rect Rect;
    public Color Color, Color2;
    public float Radius, FontSize, Reserved0, Reserved1;
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct FrameStats
{
    public readonly uint Commands, Instances, DrawCalls, Layers;
    public readonly ulong UploadBytes, PrepareMicroseconds, SubmitMicroseconds;
    public readonly uint AtlasGlyphs, TextCacheHits;
    public override string ToString() => $"{Commands} commands · {Instances} quads · {DrawCalls} draws · CPU prepare {PrepareMicroseconds / 1000.0:F2} ms";
}

/// <summary>Byte ceilings for retained GPU allocations. Device limits also apply.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct ResourceBudget
{
    public readonly ulong MaxTextureBytes, MaxInstanceBufferBytes;
    public ResourceBudget(ulong maxTextureBytes, ulong maxInstanceBufferBytes)
    {
        MaxTextureBytes = maxTextureBytes;
        MaxInstanceBufferBytes = maxInstanceBufferBytes;
    }
    /// <summary>The limits used by the original NativeRenderer constructor: 528 MiB and 256 MiB.</summary>
    public static ResourceBudget Default => new(528UL * 1024 * 1024, 256UL * 1024 * 1024);
}

/// <summary>
/// Payload sizes of retained GPU objects, including unused pooled layers.
/// Excludes driver overhead, swapchains, staging and in-flight old allocations.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct ResourceUsage
{
    public readonly ulong AtlasBytes, OutputBytes, LayerBytes, InstanceBufferBytes;
    public readonly uint LayerCount, Reserved;
}

/// <summary>Reusable command and UTF-8 arenas, pinned only during synchronous submission.</summary>
public sealed class DrawList
{
    private readonly List<DrawCommand> commands = [];
    private byte[] text = new byte[4096];
    private int textLength;
    public ReadOnlySpan<DrawCommand> Commands => CollectionsMarshal.AsSpan(commands);
    public ReadOnlySpan<byte> TextBytes => text.AsSpan(0, textLength);
    public void Clear() { commands.Clear(); textLength = 0; }
    public void Rectangle(Rect rect, Color color, float radius = 0, Color? gradientEnd = null) => commands.Add(new() { Rect = rect, Color = color, Color2 = gradientEnd ?? color, Radius = radius });
    public void Text(Rect rect, string value, Color color, float size = 14, bool bold = false, bool wrap = true, bool clip = true)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        if (textLength + count > text.Length) Array.Resize(ref text, Math.Max(text.Length * 2, textLength + count));
        Encoding.UTF8.GetBytes(value, text.AsSpan(textLength, count));
        commands.Add(new() { Kind = 1, Flags = (bold ? 1u : 0) | (wrap ? 0u : 2u) | (clip ? 0u : 4u), Rect = rect, Color = color, Color2 = color, FontSize = size, TextOffset = (uint)textLength, TextLength = (uint)count });
        textLength += count;
    }
    public void PushClip(Rect rect) => commands.Add(new() { Kind = 2, Rect = rect });
    public void PopClip() => commands.Add(new() { Kind = 3 });
    public void BeginLayer(float opacity) => commands.Add(new() { Kind = 4, Color = new(0, 0, 0, Math.Clamp(opacity, 0, 1)) });
    public void EndLayer() => commands.Add(new() { Kind = 5 });
    public void Triangle(System.Numerics.Vector2 a, System.Numerics.Vector2 b, System.Numerics.Vector2 c, Color color) => commands.Add(new() { Kind = 6, Rect = new(a.X, a.Y, b.X, b.Y), Color = color, Color2 = color, Reserved0 = c.X, Reserved1 = c.Y });
    public void Image(Rect rect, NativeImage image, Color? tint = null) { ObjectDisposedException.ThrowIf(image.Id == 0, image); commands.Add(new() { Kind = 7, Flags = image.Id, Rect = rect, Color = tint ?? new Color(1, 1, 1), Color2 = tint ?? new Color(1, 1, 1) }); }
    public void CopyScaledTo(DrawList target, float scale)
    {
        if (ReferenceEquals(this, target) || !float.IsFinite(scale) || scale <= 0) throw new ArgumentException("Require a distinct target and positive finite scale.");
        target.Clear();
        target.Append(this, System.Numerics.Vector2.Zero, scale);
    }
    /// <summary>Append a document's render IR with a uniform viewport transform; preserves scopes and painter order.</summary>
    public void Append(DrawList source, System.Numerics.Vector2 offset, float scale = 1)
    {
        if (ReferenceEquals(this, source) || !float.IsFinite(scale) || scale <= 0 || !float.IsFinite(offset.X) || !float.IsFinite(offset.Y)) throw new ArgumentException("Require a distinct source and finite viewport transform.");
        var target = this;
        foreach (var original in source.Commands)
        {
            var c = original; c.Rect = new(c.Rect.X * scale + offset.X, c.Rect.Y * scale + offset.Y, c.Rect.Width * scale, c.Rect.Height * scale); c.Radius *= scale; c.FontSize *= scale;
            if (c.Kind == 1) target.Text(c.Rect, Encoding.UTF8.GetString(source.TextBytes.Slice((int)c.TextOffset, (int)c.TextLength)), c.Color, c.FontSize, (c.Flags & 1) != 0, (c.Flags & 2) == 0, (c.Flags & 4) == 0);
            else { if (c.Kind == 6) { c.Rect = c.Rect with { Width = c.Rect.Width + offset.X, Height = c.Rect.Height + offset.Y }; c.Reserved0 = c.Reserved0 * scale + offset.X; c.Reserved1 = c.Reserved1 * scale + offset.Y; } target.commands.Add(c); }
        }
    }
}

public sealed class NativeImage : IDisposable
{
    internal uint Id { get; private set; }
    private readonly NativeRenderer owner;
    internal NativeImage(NativeRenderer owner, uint id) { this.owner = owner; Id = id; }
    public void Dispose() { if (Id == 0) return; owner.ReleaseImage(Id); Id = 0; }
}

public interface ITextMeasurer { (float Width, float Height) Measure(string text, float size, float width, bool bold = false); }

public sealed unsafe class NativeRenderer : IDisposable, ITextLayoutProvider
{
    private ulong handle;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    public string Adapter { get; }
    public NativeRenderer(uint width, uint height, nint window = 0) : this(width, height, window, null) { }
    /// <summary>
    /// Creates a renderer with explicit resource ceilings; use window = 0 for offscreen rendering.
    /// Requires a native library exporting the additive v1 resource-budget API.
    /// </summary>
    public NativeRenderer(uint width, uint height, nint window, ResourceBudget budget) : this(width, height, window, (ResourceBudget?)budget) { }
    private NativeRenderer(uint width, uint height, nint window, ResourceBudget? budget)
    {
        if (budget is { } configured) ValidateResourceBudget(width, height, configured);
        if (Native.AbiVersion() != 0x00010000 || Native.CommandSize() != Marshal.SizeOf<DrawCommand>()) throw new InvalidOperationException("Incompatible YoUI native ABI.");
        if (budget is { } requested)
        {
            VerifyResourceAbi();
            try { Check(Native.CreateWithBudget(width, height, window, in requested, out handle)); }
            catch (EntryPointNotFoundException ex) { throw MissingResourceApi(ex); }
        }
        else Check(Native.Create(width, height, window, out handle));
        try
        {
            byte[] name = new byte[512];
            fixed (byte* ptr = name) { Check(Native.AdapterName(handle, ptr, 512, out uint length)); Adapter = Encoding.UTF8.GetString(name, 0, (int)length); }
        }
        catch { Native.Destroy(handle); handle = 0; throw; }
    }
    private static void ValidateResourceBudget(uint width, uint height, ResourceBudget budget)
    {
        if (width is 0 or > 8192) throw new ArgumentOutOfRangeException(nameof(width), "Target dimensions must be in 1..8192.");
        if (height is 0 or > 8192) throw new ArgumentOutOfRangeException(nameof(height), "Target dimensions must be in 1..8192.");
        const ulong atlasBytes = 2048UL * 2048 * 4;
        if (budget.MaxTextureBytes < atlasBytes + (ulong)width * height * 4)
            throw new ArgumentOutOfRangeException(nameof(budget), "Texture budget must cover the 16 MiB atlas and initial RGBA8 output.");
        if (budget.MaxInstanceBufferBytes < 1024UL * 96)
            throw new ArgumentOutOfRangeException(nameof(budget), "Instance-buffer budget must cover the initial 1024 instances (98304 bytes).");
    }
    private static void VerifyResourceAbi()
    {
        try
        {
            if (Native.ResourceBudgetSize() != Marshal.SizeOf<ResourceBudget>() || Native.ResourceUsageSize() != Marshal.SizeOf<ResourceUsage>())
                throw new InvalidOperationException("Incompatible YoUI native resource-budget layouts.");
        }
        catch (EntryPointNotFoundException ex) { throw MissingResourceApi(ex); }
    }
    private static InvalidOperationException MissingResourceApi(EntryPointNotFoundException inner) => new(
        "This YoUI native library does not export the resource-budget API. ABI v1 alone does not guarantee these additive symbols; rebuild or deploy the matching native library.", inner);
    private void Verify() { ObjectDisposedException.ThrowIf(handle == 0, this); if (ownerThread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Renderer calls must stay on the creating thread."); }
    public ResourceBudget ResourceBudget
    {
        get
        {
            Verify(); VerifyResourceAbi();
            try { Check(Native.ResourceBudget(handle, out var budget)); return budget; }
            catch (EntryPointNotFoundException ex) { throw MissingResourceApi(ex); }
        }
    }
    public ResourceUsage ResourceUsage
    {
        get
        {
            Verify(); VerifyResourceAbi();
            try { Check(Native.ResourceUsage(handle, out var usage)); return usage; }
            catch (EntryPointNotFoundException ex) { throw MissingResourceApi(ex); }
        }
    }
    public FrameStats Render(DrawList list)
    {
        Verify();
        fixed (DrawCommand* commands = list.Commands)
        fixed (byte* text = list.TextBytes) { Check(Native.Render(handle, commands, (uint)list.Commands.Length, text, (uint)list.TextBytes.Length, out var stats)); return stats; }
    }
    public void Resize(uint width, uint height) { Verify(); Check(Native.Resize(handle, width, height)); }
    public NativeImage UploadImage(uint width, uint height, ReadOnlySpan<byte> rgba)
    {
        Verify(); fixed (byte* data = rgba) { Check(Native.UploadImage(handle, width, height, data, (uint)rgba.Length, out uint image)); return new(this, image); }
    }
    internal void ReleaseImage(uint image) { if (handle == 0) return; Verify(); Check(Native.ReleaseImage(handle, image)); }
    public (float Width, float Height) Measure(string text, float size, float width, bool bold = false)
    {
        Verify(); var bytes = Encoding.UTF8.GetBytes(text);
        fixed (byte* ptr = bytes) { Check(Native.Measure(handle, ptr, (uint)bytes.Length, size, width, bold ? 1u : 0, out float w, out float h)); return (w, h); }
    }
    /// <summary>Uses the same shaping/cache inputs as drawing. Returned snapshots outlive the renderer.</summary>
    public TextLayout Layout(string text, float size, float width, bool bold = false, bool wrap = true)
    {
        Verify(); ArgumentNullException.ThrowIfNull(text); TextBoundary.Validate(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            if (Native.TextCellSize() != Marshal.SizeOf<NativeTextCell>()) throw new InvalidOperationException("Incompatible YoUI native text-cell layout.");
            fixed (byte* ptr = bytes)
            {
                Check(Native.TextCells(handle, ptr, (uint)bytes.Length, size, width, (bold ? 1u : 0) | (wrap ? 0u : 2u), null, 0, out uint count));
                var native = new NativeTextCell[checked((int)count)];
                fixed (NativeTextCell* output = native)
                    Check(Native.TextCells(handle, ptr, (uint)bytes.Length, size, width, (bold ? 1u : 0) | (wrap ? 0u : 2u), output, count, out count));
                if (count != native.Length) throw new InvalidOperationException("Native text-cell count changed during a synchronous query.");
                // Build one exact UTF-8 -> UTF-16 boundary map. Never interpret byte offsets as UTF-16 indices.
                var indices = new Dictionary<uint, int> { [0] = 0 };
                uint offset = 0; int utf16 = 0;
                foreach (var rune in text.EnumerateRunes()) { offset += (uint)rune.Utf8SequenceLength; utf16 += rune.Utf16SequenceLength; indices[offset] = utf16; }
                var cells = new TextCell[native.Length];
                for (int i = 0; i < native.Length; i++)
                {
                    var c = native[i];
                    if (!indices.TryGetValue(c.Start, out int start) || !indices.TryGetValue(c.End, out int end) || c.Flags > 1)
                        throw new InvalidOperationException("Native text geometry contains an invalid UTF-8 boundary or flags.");
                    cells[i] = new(start, end, checked((int)c.Line), c.Bounds, (c.Flags & 1) != 0);
                }
                return new(text, cells);
            }
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new InvalidOperationException("This YoUI native library does not export the text-layout API. Rebuild or deploy the matching native library; ABI v1 alone does not guarantee additive symbols.", ex);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTextCell { public uint Start, End, Line, Flags; public Rect Bounds; }
    public void SavePng(string path) { Verify(); var bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(path)); fixed (byte* ptr = bytes) Check(Native.SavePng(handle, ptr, (uint)bytes.Length)); }
    public void Dispose() { if (handle == 0) return; Verify(); Check(Native.Destroy(handle)); handle = 0; GC.SuppressFinalize(this); }
    private static void Check(int status)
    {
        if (status == 0) return;
        uint length = Native.LastError(null, 0); var bytes = new byte[length];
        fixed (byte* ptr = bytes) Native.LastError(ptr, length);
        throw new InvalidOperationException($"YoUI native ({status}): {Encoding.UTF8.GetString(bytes)}");
    }
    private static class Native
    {
        private const string Dll = "youi_render";
        [DllImport(Dll, EntryPoint = "youi_abi_version", CallingConvention = CallingConvention.Cdecl)] internal static extern uint AbiVersion();
        [DllImport(Dll, EntryPoint = "youi_command_size", CallingConvention = CallingConvention.Cdecl)] internal static extern uint CommandSize();
        [DllImport(Dll, EntryPoint = "youi_resource_budget_size", CallingConvention = CallingConvention.Cdecl)] internal static extern uint ResourceBudgetSize();
        [DllImport(Dll, EntryPoint = "youi_resource_usage_size", CallingConvention = CallingConvention.Cdecl)] internal static extern uint ResourceUsageSize();
        [DllImport(Dll, EntryPoint = "youi_create", CallingConvention = CallingConvention.Cdecl)] internal static extern int Create(uint w, uint h, nint window, out ulong handle);
        [DllImport(Dll, EntryPoint = "youi_create_with_budget", CallingConvention = CallingConvention.Cdecl)] internal static extern int CreateWithBudget(uint w, uint h, nint window, in ResourceBudget budget, out ulong handle);
        [DllImport(Dll, EntryPoint = "youi_resource_budget", CallingConvention = CallingConvention.Cdecl)] internal static extern int ResourceBudget(ulong handle, out ResourceBudget budget);
        [DllImport(Dll, EntryPoint = "youi_resource_usage", CallingConvention = CallingConvention.Cdecl)] internal static extern int ResourceUsage(ulong handle, out ResourceUsage usage);
        [DllImport(Dll, EntryPoint = "youi_destroy", CallingConvention = CallingConvention.Cdecl)] internal static extern int Destroy(ulong handle);
        [DllImport(Dll, EntryPoint = "youi_resize", CallingConvention = CallingConvention.Cdecl)] internal static extern int Resize(ulong handle, uint w, uint h);
        [DllImport(Dll, EntryPoint = "youi_upload_image", CallingConvention = CallingConvention.Cdecl)] internal static extern int UploadImage(ulong handle, uint w, uint h, byte* data, uint length, out uint image);
        [DllImport(Dll, EntryPoint = "youi_release_image", CallingConvention = CallingConvention.Cdecl)] internal static extern int ReleaseImage(ulong handle, uint image);
        [DllImport(Dll, EntryPoint = "youi_render", CallingConvention = CallingConvention.Cdecl)] internal static extern int Render(ulong handle, DrawCommand* commands, uint count, byte* text, uint length, out FrameStats stats);
        [DllImport(Dll, EntryPoint = "youi_measure", CallingConvention = CallingConvention.Cdecl)] internal static extern int Measure(ulong handle, byte* text, uint length, float size, float maxWidth, uint bold, out float width, out float height);
        [DllImport(Dll, EntryPoint = "youi_text_cell_size", CallingConvention = CallingConvention.Cdecl)] internal static extern uint TextCellSize();
        [DllImport(Dll, EntryPoint = "youi_text_cells", CallingConvention = CallingConvention.Cdecl)] internal static extern int TextCells(ulong handle, byte* text, uint length, float size, float maxWidth, uint bold, NativeTextCell* cells, uint capacity, out uint count);
        [DllImport(Dll, EntryPoint = "youi_save_png", CallingConvention = CallingConvention.Cdecl)] internal static extern int SavePng(ulong handle, byte* path, uint length);
        [DllImport(Dll, EntryPoint = "youi_last_error", CallingConvention = CallingConvention.Cdecl)] internal static extern uint LastError(byte* output, uint capacity);
        [DllImport(Dll, EntryPoint = "youi_adapter_name", CallingConvention = CallingConvention.Cdecl)] internal static extern int AdapterName(ulong handle, byte* output, uint capacity, out uint written);
    }
}
