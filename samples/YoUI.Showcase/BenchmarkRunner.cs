using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace YoUI.Showcase;

internal static class BenchmarkRunner
{
    public static void Run(string[] args, string artifactDir)
    {
        var options = BenchmarkOptions.Parse(args); // Reject bad inputs before native creation.
        using var renderer = new NativeRenderer(1280, 820);
        var identity = BenchmarkData.AdapterIdentity(renderer.Adapter);
        var scene = new Showcase();
        var document = new UiDocument(scene, renderer, 1280, 820);
        scene.Motion.Value = true;
        for (int n = 0; n < options.WarmupFrames; n++) { scene.Step(n / 60f); renderer.Render(document.BuildFrame()); }
        // Allocate measurement storage before measuring allocation. No JSON, sorting, resource queries or readback in the measured loop.
        var samples = new BenchmarkSample[options.MeasuredFrames];
        int layouts = document.LayoutPasses;
        var usageBefore = renderer.ResourceUsage;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int n = 0; n < samples.Length; n++)
        {
            int frameLayouts = document.LayoutPasses;
            scene.Step((n + options.WarmupFrames) / 60f);
            long start = Stopwatch.GetTimestamp();
            var frame = document.BuildFrame();
            double managedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var stats = renderer.Render(frame);
            samples[n] = new(n, managedMs, stats.PrepareMicroseconds / 1000.0, stats.SubmitMicroseconds / 1000.0,
                stats.Commands, stats.Instances, stats.DrawCalls, stats.Layers, stats.UploadBytes, stats.AtlasGlyphs, stats.TextCacheHits,
                document.LayoutPasses - frameLayouts);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var usageAfter = renderer.ResourceUsage;
        var last = samples[^1];
        object Usage(ResourceUsage u) => new { atlasBytes = u.AtlasBytes, outputBytes = u.OutputBytes, layerBytes = u.LayerBytes, instanceBufferBytes = u.InstanceBufferBytes, layerCount = u.LayerCount };
        double P(Func<BenchmarkSample, double> select, double p) => BenchmarkData.Percentile(samples.Select(select), p);
        var report = new
        {
            schemaVersion = 2, adapter = renderer.Adapter, adapterKind = identity.Kind, backend = identity.Backend,
            requestedBackend = Environment.GetEnvironmentVariable("YOUI_BACKEND"),
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(), processorCount = Environment.ProcessorCount,
            configuration =
#if DEBUG
            "Debug",
#else
            "Release",
#endif
            resolution = "1280x820", dataItems = scene.Library.ItemCount, realizedRows = scene.Library.RealizedCount,
            warmupFrames = options.WarmupFrames, measuredFrames = samples.Length, simulationStepSeconds = 1.0 / 60,
            percentileMethod = "sorted[floor((N - 1) * p)]", layoutPassesDuringAnimation = document.LayoutPasses - layouts,
            managedBuildMedianMs = P(s => s.ManagedBuildMs, .5), managedBuildP95Ms = P(s => s.ManagedBuildMs, .95),
            nativePrepareMedianMs = P(s => s.NativePrepareMs, .5), nativePrepareP95Ms = P(s => s.NativePrepareMs, .95),
            nativeSubmitMedianMs = P(s => s.NativeSubmitMs, .5), nativeSubmitP95Ms = P(s => s.NativeSubmitMs, .95),
            managedAllocatedBytes = bytes, managedAllocatedBytesPerFrame = (double)bytes / samples.Length,
            // Preserve original case-sensitive summary keys for existing benchmark consumers.
            last.Commands, last.Instances, last.DrawCalls, last.Layers, last.UploadBytes,
            resourceUsageBefore = Usage(usageBefore), resourceUsageAfter = Usage(usageAfter),
            samples = samples.Select(s => new { frame = s.Frame, managedBuildMs = s.ManagedBuildMs, nativePrepareMs = s.NativePrepareMs, nativeSubmitMs = s.NativeSubmitMs, commands = s.Commands, instances = s.Instances, drawCalls = s.DrawCalls, layers = s.Layers, uploadBytes = s.UploadBytes, atlasGlyphs = s.AtlasGlyphs, textCacheHits = s.TextCacheHits, layoutPasses = s.LayoutPasses }),
            notes = "CPU measurements only. Build timing excludes scene.Step. Native submit includes encoding/submission CPU time and possible driver backpressure, not synchronized GPU execution or presentation FPS. No GPU timestamps, per-frame wait, pacing or readback. Allocation is current-thread managed bytes for scene.Step + BuildFrame + Render + sample assignment; sample storage and reporting are excluded; native/other-thread allocations are excluded. Resource snapshots are retained payload capacities, not total/peak VRAM. One full-size isolated layer. CPU adapters are software rendering; other/virtual types are not proof of hardware."
        };
        // Readback is deliberately outside all timings; export report only after it succeeds.
        renderer.SavePng(Path.Combine(artifactDir, "benchmark-frame.png"));
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(artifactDir, "benchmark.json"), json);
        Console.WriteLine(json);
    }
}
