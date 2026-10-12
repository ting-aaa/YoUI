using System.Runtime.InteropServices;
using YoUI;

static class ResourceBudgetTests
{
    private const ulong AtlasBytes = 2048UL * 2048 * 4;
    private const ulong InitialInstanceBytes = 1024UL * 112;

    public static void RunLogic(Action<string, Action> test, Action<bool, string> require)
    {
        test("resource ABI sizes and offsets", () =>
        {
            require(Marshal.SizeOf<ResourceBudget>() == 16, "resource budget size");
            require(Marshal.OffsetOf<ResourceBudget>(nameof(ResourceBudget.MaxTextureBytes)).ToInt32() == 0, "texture budget offset");
            require(Marshal.OffsetOf<ResourceBudget>(nameof(ResourceBudget.MaxInstanceBufferBytes)).ToInt32() == 8, "instance budget offset");
            require(Marshal.SizeOf<ResourceUsage>() == 40, "resource usage size");
            require(Marshal.OffsetOf<ResourceUsage>(nameof(ResourceUsage.AtlasBytes)).ToInt32() == 0, "atlas offset");
            require(Marshal.OffsetOf<ResourceUsage>(nameof(ResourceUsage.OutputBytes)).ToInt32() == 8, "output offset");
            require(Marshal.OffsetOf<ResourceUsage>(nameof(ResourceUsage.LayerBytes)).ToInt32() == 16, "layer offset");
            require(Marshal.OffsetOf<ResourceUsage>(nameof(ResourceUsage.InstanceBufferBytes)).ToInt32() == 24, "instance offset");
            require(Marshal.OffsetOf<ResourceUsage>(nameof(ResourceUsage.LayerCount)).ToInt32() == 32, "layer count offset");
            require(Marshal.OffsetOf<ResourceUsage>(nameof(ResourceUsage.Reserved)).ToInt32() == 36, "reserved offset");
        });
        test("resource budget defaults and explicit ceilings", () =>
        {
            var defaults = ResourceBudget.Default;
            require(defaults.MaxTextureBytes == 528UL * 1024 * 1024, "default texture budget");
            require(defaults.MaxInstanceBufferBytes == 256UL * 1024 * 1024, "default instance budget");
            require(default(ResourceBudget).MaxTextureBytes == 0 && default(ResourceBudget).MaxInstanceBufferBytes == 0, "zero-initialized budget stays explicit");
            var custom = new ResourceBudget(ulong.MaxValue, ulong.MaxValue);
            require(custom.MaxTextureBytes == ulong.MaxValue && custom.MaxInstanceBufferBytes == ulong.MaxValue, "64-bit ceilings must not be truncated");
            // These source-compatible calls must keep compiling, without loading native code.
            Func<NativeRenderer> originalTwoArguments = () => new(1, 1);
            Func<NativeRenderer> originalDefaultWindow = () => new(1, 1, default);
            Func<NativeRenderer> explicitBudget = () => new(1, 1, 0, ResourceBudget.Default);
            require(originalTwoArguments is not null && originalDefaultWindow is not null && explicitBudget is not null, "constructor signatures");
        });
        test("invalid resource configurations fail before loading native code", () =>
        {
            static void Reject(uint width, uint height, ResourceBudget budget, string parameter)
            {
                try { using var renderer = new NativeRenderer(width, height, 0, budget); }
                catch (ArgumentOutOfRangeException ex) when (ex.ParamName == parameter) { return; }
                throw new Exception($"Expected managed rejection for {parameter}.");
            }
            Reject(0, 1, ResourceBudget.Default, "width");
            Reject(8193, 1, ResourceBudget.Default, "width");
            Reject(1, 0, ResourceBudget.Default, "height");
            Reject(1, uint.MaxValue, ResourceBudget.Default, "height");
            Reject(1, 1, default, "budget");
            Reject(1, 1, new(AtlasBytes + 3, InitialInstanceBytes), "budget");
            Reject(1, 1, new(AtlasBytes + 4, InitialInstanceBytes - 1), "budget");
            Reject(8192, 8192, new(AtlasBytes + 8192UL * 8192 * 4 - 1, InitialInstanceBytes), "budget");
        });
    }

    // Explicit opt-in: creates actual GPU devices and loads the native library.
    public static void RunGpu()
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Rejected(Action operation)
        {
            try { operation(); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("budget", StringComparison.OrdinalIgnoreCase)) { return; }
            throw new Exception("Expected native resource-budget rejection.");
        }
        static bool SameUsage(ResourceUsage a, ResourceUsage b) =>
            a.AtlasBytes == b.AtlasBytes && a.OutputBytes == b.OutputBytes && a.LayerBytes == b.LayerBytes &&
            a.InstanceBufferBytes == b.InstanceBufferBytes && a.LayerCount == b.LayerCount && a.Reserved == b.Reserved;

        const ulong outputBytes = 64UL * 32 * 4;
        var budget = new ResourceBudget(AtlasBytes + outputBytes * 2, InitialInstanceBytes);
        using var renderer = new NativeRenderer(64, 32, 0, budget);
        Console.WriteLine($"GPU resource-budget adapter: {renderer.Adapter}");
        var actualBudget = renderer.ResourceBudget;
        Require(actualBudget.MaxTextureBytes == budget.MaxTextureBytes && actualBudget.MaxInstanceBufferBytes == budget.MaxInstanceBufferBytes, "configured budget round-trip");
        var initial = renderer.ResourceUsage;
        Require(initial.AtlasBytes == AtlasBytes && initial.OutputBytes == outputBytes && initial.LayerBytes == 0 && initial.LayerCount == 0 && initial.InstanceBufferBytes == InitialInstanceBytes && initial.Reserved == 0, "initial resource usage");

        var frame = new DrawList();
        frame.BeginLayer(.5f); frame.Rectangle(new(0, 0, 16, 16), new(1, 0, 0)); frame.EndLayer();
        renderer.Render(frame);
        var retained = renderer.ResourceUsage;
        Require(retained.LayerCount == 1 && retained.LayerBytes == outputBytes, "retained layer accounting");

        frame.Clear(); frame.BeginLayer(.5f); frame.BeginLayer(.5f);
        frame.Rectangle(new(0, 0, 16, 16), new(1, 0, 0)); frame.EndLayer(); frame.EndLayer();
        Rejected(() => renderer.Render(frame));
        Require(SameUsage(retained, renderer.ResourceUsage), "layer rejection leaves allocations intact");
        Rejected(() => renderer.Resize(256, 32));
        Require(SameUsage(retained, renderer.ResourceUsage), "resize rejection leaves allocations intact");

        frame.Clear();
        for (int i = 0; i < 1025; i++) frame.Rectangle(new(0, 0, 1, 1), new(1, 0, 0));
        Rejected(() => renderer.Render(frame));
        Require(SameUsage(retained, renderer.ResourceUsage), "instance rejection leaves allocations intact");
        Require(renderer.ResourceBudget.MaxTextureBytes == budget.MaxTextureBytes, "budget readable after rejection");

        frame.Clear(); frame.Rectangle(new(0, 0, 16, 16), new(0, 1, 0));
        Require(renderer.Render(frame).Instances == 1, "valid frame recovers after rejection");
        Require(SameUsage(retained, renderer.ResourceUsage), "unused pooled layers remain counted");
        renderer.Resize(32, 32);
        var resized = renderer.ResourceUsage;
        Require(resized.OutputBytes == 32UL * 32 * 4 && resized.LayerCount == 0 && resized.LayerBytes == 0, "successful resize resets pooled layers");

        // Nine-slice shares the guarded textured-triangle path and the 112-byte payload.
        using (var image = renderer.UploadImage(3, 3, Enumerable.Repeat((byte)255, 3 * 3 * 4).ToArray()))
        {
            frame.Clear();
            for (int i = 0; i < 56; i++) frame.NineSlice(new(0, 0, 32, 32), image, 1, 1, 1, 1);
            Require(renderer.Render(frame).Instances == 1008, "nine-slice instances fit exact initial allocation");
            var meshUsage = renderer.ResourceUsage;
            Require(meshUsage.InstanceBufferBytes == InitialInstanceBytes, "textured payload accounting");
            frame.NineSlice(new(0, 0, 32, 32), image, 1, 1, 1, 1);
            Rejected(() => renderer.Render(frame));
            Require(SameUsage(meshUsage, renderer.ResourceUsage), "nine-slice rejection leaves allocations intact");
            frame.Clear(); frame.NineSlice(new(0, 0, 32, 32), image, 1, 1, 1, 1);
            Require(renderer.Render(frame).Instances == 18, "nine-slice recovers after budget rejection");
        }

        renderer.Dispose();
        bool disposedUsage = false, disposedBudget = false;
        try { _ = renderer.ResourceUsage; } catch (ObjectDisposedException) { disposedUsage = true; }
        try { _ = renderer.ResourceBudget; } catch (ObjectDisposedException) { disposedBudget = true; }
        Require(disposedUsage && disposedBudget, "resource getters reject disposed renderers");
        using var defaultRenderer = new NativeRenderer(1, 1, default);
        Require(defaultRenderer.ResourceBudget.MaxTextureBytes == ResourceBudget.Default.MaxTextureBytes && defaultRenderer.ResourceBudget.MaxInstanceBufferBytes == ResourceBudget.Default.MaxInstanceBufferBytes, "original constructor keeps default budget");
        Console.WriteLine("PASS managed GPU resource-budget bindings, rejection recovery and disposal");
    }
}
