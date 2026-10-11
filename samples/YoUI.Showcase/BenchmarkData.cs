using System.Globalization;
using System.Text.RegularExpressions;

namespace YoUI.Showcase;

internal sealed record BenchmarkOptions(int WarmupFrames, int MeasuredFrames)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        int warmup = 30, frames = 240;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!seen.Add(arg)) throw new ArgumentException($"Duplicate benchmark option: {arg}");
            if (arg == "--benchmark") continue;
            if (arg is not ("--warmup" or "--frames") || ++i >= args.Length ||
                !int.TryParse(args[i], NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
                value < (arg == "--warmup" ? 0 : 1) || value > 100000)
                throw new ArgumentException("Use --benchmark [--warmup 0..100000] [--frames 1..100000].");
            if (arg == "--warmup") warmup = value; else frames = value;
        }
        if (!seen.Contains("--benchmark")) throw new ArgumentException("Missing --benchmark.");
        return new(warmup, frames);
    }
}

internal readonly record struct BenchmarkSample(int Frame, double ManagedBuildMs, double NativePrepareMs, double NativeSubmitMs,
    uint Commands, uint Instances, uint DrawCalls, uint Layers, ulong UploadBytes, uint AtlasGlyphs, uint TextCacheHits, int LayoutPasses);

internal static class BenchmarkData
{
    // Match the format appended by native Renderer, rather than guessing from an adapter's marketing name.
    public static (string Backend, string Kind) AdapterIdentity(string adapter)
    {
        var match = Regex.Match(adapter, @" \((Vulkan|Dx12|Gl|Metal|BrowserWebGpu); (Cpu|IntegratedGpu|DiscreteGpu|VirtualGpu|Other); driver ", RegexOptions.CultureInvariant);
        if (!match.Success) throw new ArgumentException("Unrecognized native adapter identity.");
        string kind = match.Groups[2].Value switch { "Cpu" => "cpu", "IntegratedGpu" => "integrated", "DiscreteGpu" => "discrete", "VirtualGpu" => "virtual", _ => "other" };
        return (match.Groups[1].Value, kind);
    }

    // The historical benchmark used floor((N - 1) * p); keep this definition explicit and reproducible.
    public static double Percentile(IEnumerable<double> samples, double p)
    {
        var values = samples.ToArray();
        if (values.Length == 0 || !double.IsFinite(p) || p < 0 || p > 1 || values.Any(v => !double.IsFinite(v) || v < 0))
            throw new ArgumentException("Require finite nonnegative samples and a percentile in [0,1].");
        Array.Sort(values);
        return values[(int)((values.Length - 1) * p)];
    }
}
