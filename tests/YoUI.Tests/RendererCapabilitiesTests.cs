using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using YoUI;

static class RendererCapabilitiesTests
{
    private const ulong AtlasBytes = 2048UL * 2048 * 4;
    private const ulong InitialInstanceBytes = 1024UL * 112;

    public static void RunLogic(Action<string, Action> test, Action<bool, string> require)
    {
        test("renderer capability ABI has immutable 32-byte sequential layout", () =>
        {
            var type = typeof(RendererCapabilities);
            require(type.IsValueType && type.IsDefined(typeof(IsReadOnlyAttribute), false), "capabilities are an immutable value snapshot");
            require(type.StructLayoutAttribute!.Value == LayoutKind.Sequential && Marshal.SizeOf<RendererCapabilities>() == 32, "capability layout and size");
            var fields = new (string Name, Type Type, int Offset)[]
            {
                (nameof(RendererCapabilities.Backend), typeof(RendererBackend), 0),
                (nameof(RendererCapabilities.DeviceKind), typeof(RendererDeviceKind), 4),
                (nameof(RendererCapabilities.MaxTargetDimension2D), typeof(uint), 8),
                (nameof(RendererCapabilities.MaxImageDimension2D), typeof(uint), 12),
                (nameof(RendererCapabilities.AtlasDimension2D), typeof(uint), 16),
                (nameof(RendererCapabilities.Reserved), typeof(uint), 20),
                (nameof(RendererCapabilities.MaxInstanceBufferBytes), typeof(ulong), 24),
            };
            require(type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length == fields.Length, "exact public capability fields");
            foreach (var field in fields)
            {
                var actual = type.GetField(field.Name)!;
                require(actual.FieldType == field.Type && actual.IsInitOnly, $"readonly capability field {field.Name}");
                require(Marshal.OffsetOf<RendererCapabilities>(field.Name).ToInt32() == field.Offset, $"capability offset {field.Name}");
            }
            require(typeof(RendererBackend).GetEnumUnderlyingType() == typeof(uint) && typeof(RendererDeviceKind).GetEnumUnderlyingType() == typeof(uint), "enum storage is uint32");
            require(Enum.GetValues<RendererBackend>().SequenceEqual(new[] { RendererBackend.Other, RendererBackend.Vulkan, RendererBackend.Metal, RendererBackend.Dx12, RendererBackend.Gl, RendererBackend.BrowserWebGpu }) &&
                Enum.GetValues<RendererBackend>().Select(value => (uint)value).SequenceEqual(new uint[] { 0, 1, 2, 3, 4, 5 }), "backend enum values are stable ABI tags");
            require(Enum.GetValues<RendererDeviceKind>().SequenceEqual(new[] { RendererDeviceKind.Other, RendererDeviceKind.IntegratedGpu, RendererDeviceKind.DiscreteGpu, RendererDeviceKind.VirtualGpu, RendererDeviceKind.Cpu }) &&
                Enum.GetValues<RendererDeviceKind>().Select(value => (uint)value).SequenceEqual(new uint[] { 0, 1, 2, 3, 4 }), "device-kind enum values are stable ABI tags");
        });
        test("renderer capabilities expose additive size and 64-bit Cdecl query bindings", () =>
        {
            var property = typeof(NativeRenderer).GetProperty(nameof(NativeRenderer.Capabilities))!;
            require(property.PropertyType == typeof(RendererCapabilities) && property.GetMethod!.IsPublic && property.SetMethod is null, "public read-only capability property");
            var native = typeof(NativeRenderer).GetNestedType("Native", BindingFlags.NonPublic)!;
            foreach (string symbol in new[] { "youi_renderer_capabilities_size", "youi_renderer_capabilities" })
            {
                var binding = native.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                    .Single(method => method.GetCustomAttribute<DllImportAttribute>()?.EntryPoint == symbol);
                var import = binding.GetCustomAttribute<DllImportAttribute>()!;
                require(import.Value == "youi_render" && import.CallingConvention == CallingConvention.Cdecl, $"additive symbol and Cdecl convention: {symbol}");
                if (symbol.EndsWith("_size", StringComparison.Ordinal))
                    require(binding.ReturnType == typeof(uint) && binding.GetParameters().Length == 0, "capability-size binding returns uint32");
                else
                {
                    var parameters = binding.GetParameters();
                    require(binding.ReturnType == typeof(int) && parameters.Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(ulong), typeof(RendererCapabilities).MakeByRefType() }), "query uses status, uint64 renderer and capability output");
                    require(parameters[1].IsOut, "query marshals capabilities as an output snapshot");
                }
            }
        });
        test("renderer capabilities reject disposal before native access", () =>
        {
            var disposed = (NativeRenderer)RuntimeHelpers.GetUninitializedObject(typeof(NativeRenderer));
            require(Capture(() => _ = disposed.Capabilities) is ObjectDisposedException, "disposed query must not load or call native code");
        });
        test("renderer capabilities reject foreign threads before native access", () =>
        {
            var renderer = HandleFixture();
            var error = OnOtherThread(() => _ = renderer.Capabilities);
            require(error is InvalidOperationException && error.Message.Contains("creating thread", StringComparison.Ordinal), "foreign-thread query must fail in the managed ownership guard");
        });
    }

    // Use an older ABI-v1 library, or the capability fixture built with no size
    // export / a matching size but no query export. Neither path creates a device.
    public static void RunMissingApi()
    {
        var error = Capture(() => _ = HandleFixture().Capabilities);
        Require(error is InvalidOperationException && error.InnerException is EntryPointNotFoundException, "missing capability exports must have a helpful managed wrapper");
        Require(error!.Message.Contains("capabilit", StringComparison.OrdinalIgnoreCase) &&
            error.Message.Contains("matching native library", StringComparison.Ordinal) &&
            error.Message.Contains("additive", StringComparison.OrdinalIgnoreCase) &&
            error.Message.Contains("ABI v1", StringComparison.Ordinal), "diagnostic identifies the additive capability API and deployment remedy");
        Console.WriteLine("PASS managed missing additive renderer-capability API diagnostic (legacy native library; no GPU)");
    }

    // The fixture's query aborts if invoked. A mismatched size must be rejected
    // before native code can write a differently sized struct into managed memory.
    public static void RunIncompatibleApi()
    {
        var error = Capture(() => _ = HandleFixture().Capabilities);
        Require(error is InvalidOperationException && error.InnerException is not EntryPointNotFoundException &&
            error.Message.Contains("capabilit", StringComparison.OrdinalIgnoreCase) &&
            error.Message.Contains("layout", StringComparison.OrdinalIgnoreCase), "incompatible capability size is rejected before the query");
        Console.WriteLine("PASS managed incompatible renderer-capability layout guard (native fixture; no GPU)");
    }

    // Explicit opt-in: creates real devices and loads the matching native library.
    // Report CPU/software adapters verbatim; this is not native-window evidence.
    public static void RunGpu()
    {
        RendererCapabilities deviceLimits;
        using (var unlimited = new NativeRenderer(1, 1, 0, new(AtlasBytes + 4, ulong.MaxValue)))
        {
            deviceLimits = unlimited.Capabilities;
            RequireIdentity(unlimited, deviceLimits);
            Require(deviceLimits.MaxInstanceBufferBytes >= InitialInstanceBytes && deviceLimits.MaxInstanceBufferBytes < ulong.MaxValue, "unbounded caller budget is capped by the enabled device limit");
        }
        using (var defaults = new NativeRenderer(1, 1))
        {
            var capabilities = defaults.Capabilities;
            RequireIdentity(defaults, capabilities);
            Require(SameIdentityAndDimensions(deviceLimits, capabilities), "original constructor preserves selected device identity and dimensions");
            Require(capabilities.MaxInstanceBufferBytes == Math.Min(deviceLimits.MaxInstanceBufferBytes, ResourceBudget.Default.MaxInstanceBufferBytes), "default instance ceiling is the minimum of enabled device limit and default budget");
        }

        const uint width = 64, height = 32;
        const ulong outputBytes = (ulong)width * height * 4;
        // The extra 37 bytes catch accidental flooring to whole 112-byte instances.
        const ulong rawInstanceBudget = 2048UL * 112 + 37;
        var budget = new ResourceBudget(AtlasBytes + outputBytes * 2, rawInstanceBudget);
        using var renderer = new NativeRenderer(width, height, 0, budget);
        var snapshot = renderer.Capabilities;
        Console.WriteLine($"GPU renderer-capabilities adapter: {renderer.Adapter}");
        Console.WriteLine($"Capabilities: backend={snapshot.Backend}; device={snapshot.DeviceKind}; target={snapshot.MaxTargetDimension2D}; image={snapshot.MaxImageDimension2D}; atlas={snapshot.AtlasDimension2D}; instanceBytes={snapshot.MaxInstanceBufferBytes}");
        RequireIdentity(renderer, snapshot);
        Require(SameIdentityAndDimensions(deviceLimits, snapshot), "explicit budgets do not change selected device or dimension capabilities");
        Require(snapshot.MaxTargetDimension2D is >= 2048 and <= 8192 && snapshot.MaxImageDimension2D == 2046 && snapshot.AtlasDimension2D == 2048 && snapshot.Reserved == 0, "effective target/image/atlas dimensions and reserved field");
        Require(snapshot.MaxInstanceBufferBytes == Math.Min(deviceLimits.MaxInstanceBufferBytes, rawInstanceBudget) && snapshot.MaxInstanceBufferBytes == rawInstanceBudget, "capability reports the exact raw-byte budget rather than rounded allocation capacity");
        var initial = renderer.ResourceUsage;
        for (int query = 0; query < 8; query++) Require(Same(snapshot, renderer.Capabilities), "repeated capability snapshots are identical");
        Require(SameUsage(initial, renderer.ResourceUsage), "capability queries do not change retained GPU allocations");

        var dimensionError = Capture(() => renderer.Resize(snapshot.MaxTargetDimension2D + 1, 1));
        Require(dimensionError is InvalidOperationException && SameUsage(initial, renderer.ResourceUsage), "reported target dimension is an upper bound");
        var budgetError = Capture(() => renderer.Resize(snapshot.MaxTargetDimension2D, snapshot.MaxTargetDimension2D));
        Require(budgetError is InvalidOperationException && budgetError.Message.Contains("budget", StringComparison.OrdinalIgnoreCase), "reported dimensions do not promise admission beyond the texture budget");
        Require(Same(snapshot, renderer.Capabilities) && SameUsage(initial, renderer.ResourceUsage), "rejected resize leaves capabilities and allocations unchanged");
        var oversizedImage = new byte[checked((int)(snapshot.MaxImageDimension2D + 1) * 4)];
        Require(Capture(() => { using var rejected = renderer.UploadImage(snapshot.MaxImageDimension2D + 1, 1, oversizedImage); }) is InvalidOperationException, "image dimension above the reported ceiling is rejected");
        using (var image = renderer.UploadImage(snapshot.MaxImageDimension2D, 1, new byte[checked((int)snapshot.MaxImageDimension2D * 4)]))
            Require(Same(snapshot, renderer.Capabilities), "an admitted image at the dimension ceiling preserves capabilities");

        var frame = new DrawList();
        frame.BeginLayer(.5f);
        frame.Text(new(0, 0, width, height), "Caps", new(1, 1, 1), 16);
        frame.EndLayer();
        for (int i = 0; i < 1025; i++) frame.Rectangle(new(0, 0, 1, 1), new(1, 0, 0));
        var rendered = renderer.Render(frame);
        var grown = renderer.ResourceUsage;
        Require(rendered.AtlasGlyphs > 0 && grown.LayerCount == 1 && grown.InstanceBufferBytes == 2048UL * 112, "fixture grows instances, glyph residency and layer storage");
        Require(Same(snapshot, renderer.Capabilities), "drawing and resource growth preserve capabilities");
        renderer.PurgeGlyphResidency();
        Require(Same(snapshot, renderer.Capabilities), "glyph purge preserves capabilities");
        renderer.TrimOffscreenLayers();
        Require(renderer.ResourceUsage.LayerCount == 0 && Same(snapshot, renderer.Capabilities), "offscreen trim preserves capabilities");
        renderer.Resize(32, 32);
        Require(renderer.ResourceUsage.OutputBytes == 32UL * 32 * 4 && Same(snapshot, renderer.Capabilities), "successful resize preserves capabilities");
        renderer.Render(frame);
        Require(Same(snapshot, renderer.Capabilities), "recreated layers and glyphs preserve capabilities");
        var beforeThread = renderer.ResourceUsage;
        var wrongThread = OnOtherThread(() => _ = renderer.Capabilities);
        Require(wrongThread is InvalidOperationException && wrongThread.Message.Contains("creating thread", StringComparison.Ordinal), "live capability query enforces the creating thread");
        Require(Same(snapshot, renderer.Capabilities) && SameUsage(beforeThread, renderer.ResourceUsage), "rejected foreign query preserves capabilities and allocations");
        Require(renderer.ResourceBudget.MaxTextureBytes == budget.MaxTextureBytes && renderer.ResourceBudget.MaxInstanceBufferBytes == rawInstanceBudget, "queries and lifecycle operations preserve configured budgets");
        var copy = renderer.Capabilities;
        renderer.Dispose();
        Require(Capture(() => _ = renderer.Capabilities) is ObjectDisposedException && Same(snapshot, copy), "disposed renderer rejects fresh queries while earlier snapshots remain valid");
        Console.WriteLine("PASS managed renderer capabilities: selected adapter identity, effective dimensions, exact device/budget byte ceiling, query invariance, draw/resize/purge/trim stability, and thread/lifetime guards");
    }

    private static void RequireIdentity(NativeRenderer renderer, RendererCapabilities capabilities)
    {
        string backend = capabilities.Backend switch
        {
            RendererBackend.Vulkan => "Vulkan", RendererBackend.Metal => "Metal", RendererBackend.Dx12 => "Dx12",
            RendererBackend.Gl => "Gl", RendererBackend.BrowserWebGpu => "BrowserWebGpu", RendererBackend.Other => "Noop",
            _ => throw new Exception("Unknown renderer backend tag"),
        };
        string device = capabilities.DeviceKind switch
        {
            RendererDeviceKind.IntegratedGpu => "IntegratedGpu", RendererDeviceKind.DiscreteGpu => "DiscreteGpu",
            RendererDeviceKind.VirtualGpu => "VirtualGpu", RendererDeviceKind.Cpu => "Cpu", RendererDeviceKind.Other => "Other",
            _ => throw new Exception("Unknown renderer device-kind tag"),
        };
        Require(renderer.Adapter.Contains($" ({backend}; {device}; driver ", StringComparison.Ordinal), "capability identity matches the actual selected adapter diagnostic");
    }
    private static NativeRenderer HandleFixture()
    {
        var renderer = (NativeRenderer)RuntimeHelpers.GetUninitializedObject(typeof(NativeRenderer));
        typeof(NativeRenderer).GetField("handle", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, ulong.MaxValue);
        typeof(NativeRenderer).GetField("ownerThread", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, Environment.CurrentManagedThreadId);
        return renderer;
    }
    private static Exception? Capture(Action operation)
    {
        try { operation(); return null; } catch (Exception error) { return error; }
    }
    private static Exception? OnOtherThread(Action operation)
    {
        Exception? error = null;
        var thread = new Thread(() => error = Capture(operation)); thread.Start(); thread.Join();
        return error;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static bool SameIdentityAndDimensions(RendererCapabilities a, RendererCapabilities b) =>
        a.Backend == b.Backend && a.DeviceKind == b.DeviceKind && a.MaxTargetDimension2D == b.MaxTargetDimension2D &&
        a.MaxImageDimension2D == b.MaxImageDimension2D && a.AtlasDimension2D == b.AtlasDimension2D && a.Reserved == b.Reserved;
    private static bool Same(RendererCapabilities a, RendererCapabilities b) =>
        SameIdentityAndDimensions(a, b) && a.MaxInstanceBufferBytes == b.MaxInstanceBufferBytes;
    private static bool SameUsage(ResourceUsage a, ResourceUsage b) =>
        a.AtlasBytes == b.AtlasBytes && a.OutputBytes == b.OutputBytes && a.LayerBytes == b.LayerBytes &&
        a.InstanceBufferBytes == b.InstanceBufferBytes && a.LayerCount == b.LayerCount && a.Reserved == b.Reserved;
}
