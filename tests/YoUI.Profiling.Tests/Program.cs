using System.Text.Json;
using YoUI.Showcase;

int checks = 0;
void Require(bool condition) { if (!condition) throw new Exception("Profiling assertion failed"); checks++; }
void Reject(Action action) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("Expected argument rejection"); }
Require(BenchmarkOptions.Parse(["--benchmark"]) == new BenchmarkOptions(30, 240));
Require(BenchmarkOptions.Parse(["--benchmark", "--warmup", "0", "--frames", "1"]) == new BenchmarkOptions(0, 1));
Require(BenchmarkOptions.Parse(["--frames", "100000", "--benchmark", "--warmup", "100000"]) == new BenchmarkOptions(100000, 100000));
foreach (string[] bad in new string[][] { [], ["--benchmark", "--frames"], ["--benchmark", "--frames", "0"], ["--benchmark", "--frames", "-1"], ["--benchmark", "--frames", "100001"], ["--benchmark", "--frames", "2147483648"], ["--benchmark", "--frames", "1.5"], ["--benchmark", "--frames", "NaN"], ["--benchmark", "--warmup", "-1"], ["--benchmark", "--frames", "1", "--frames", "2"], ["--benchmark", "--benchmark"], ["--benchmark", "--headless"], ["--frames", "2"] }) Reject(() => BenchmarkOptions.Parse(bad));
Require(BenchmarkData.Percentile([3, 1, 2, 4], .5) == 2);
Require(BenchmarkData.Percentile([3, 1, 2, 4], .95) == 3);
Require(BenchmarkData.Percentile([8], .95) == 8);
foreach (var values in new double[][] { [], [double.NaN], [double.PositiveInfinity], [-1] }) Reject(() => BenchmarkData.Percentile(values, .5));
foreach (double p in new[] { -1d, 2d, double.NaN, double.NegativeInfinity }) Reject(() => BenchmarkData.Percentile([1], p));
Require(BenchmarkData.AdapterIdentity("llvmpipe (LLVM) (Vulkan; Cpu; driver Mesa 1)") == ("Vulkan", "cpu"));
Require(BenchmarkData.AdapterIdentity("GPU (Dx12; DiscreteGpu; driver x y)") == ("Dx12", "discrete"));
Require(BenchmarkData.AdapterIdentity("GPU (Metal; IntegratedGpu; driver x y)") == ("Metal", "integrated"));
Require(BenchmarkData.AdapterIdentity("GPU (Gl; VirtualGpu; driver x y)") == ("Gl", "virtual"));
Require(BenchmarkData.AdapterIdentity("GPU (Vulkan; Other; driver x y)") == ("Vulkan", "other"));
Reject(() => BenchmarkData.AdapterIdentity("llvmpipe only"));
string escaped = "GPU \"quote\"\\path\n(Vulkan; Cpu; driver x y)";
string json = JsonSerializer.Serialize(new { adapter = escaped });
Require(JsonDocument.Parse(json).RootElement.GetProperty("adapter").GetString() == escaped);
Console.WriteLine($"PASS {checks} profiling options, percentile, adapter identity and JSON escaping checks (CPU only)");
