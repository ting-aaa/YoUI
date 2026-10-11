using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using YoUI;
using YoUI.Platform.Windows;
using YoUI.Showcase;

var artifactDir = Path.GetFullPath(Environment.GetEnvironmentVariable("YOUI_ARTIFACT_DIR") ?? Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
Directory.CreateDirectory(artifactDir);
if (args.Contains("--gallery")) { GalleryRunner.Run(args, artifactDir); return; }
if (args.Contains("--benchmark")) { BenchmarkRunner.Run(args, artifactDir); return; }
var scene = new Showcase();
if (args.Contains("--headless"))
{
    using var renderer = new NativeRenderer(1280, 820);
    var document = new UiDocument(scene, renderer, 1280, 820);
    var stats = renderer.Render(document.BuildFrame());
    renderer.SavePng(Path.Combine(artifactDir, "showcase-dark.png"));
    document.Theme = Theme.Light; scene.ThemeButton.Text = "Switch to dark"; renderer.Render(document.BuildFrame()); renderer.SavePng(Path.Combine(artifactDir, "showcase-light.png"));
    document.Theme = Theme.Dark; scene.ThemeButton.Text = "Switch to light"; scene.OpenModal(); renderer.Render(document.BuildFrame()); renderer.SavePng(Path.Combine(artifactDir, "showcase-modal.png")); scene.CloseModal();
    document.Resize(860, 820); renderer.Resize(860, 820); renderer.Render(document.BuildFrame()); renderer.SavePng(Path.Combine(artifactDir, "showcase-compact.png"));
    Console.WriteLine($"{renderer.Adapter}\n{stats}\nrealized rows: {scene.Library.RealizedCount}\n{artifactDir}");
    return;
}
using var window = new DesktopWindow("YoUI — The interface lab", scene);
scene.MotionChanged += window.SetAnimationActive;
var stopwatch = Stopwatch.StartNew();
window.AnimationFrame += () => scene.Step((float)stopwatch.Elapsed.TotalSeconds);
if (args.Contains("--smoke"))
{
    window.RenderNow();
    var checks = new List<string>();
    void Require(bool condition, string name) { if (!condition) throw new Exception("Smoke failed: " + name); checks.Add(name); }
    void Click(Element element)
    {
        var b = element.VisualBounds; int x = (int)((b.X + b.Width / 2) * window.Scale), y = (int)((b.Y + b.Height / 2) * window.Scale); nint point = (nint)((y << 16) | (x & 0xffff));
        SmokeNative.PostMessage(window.Handle, 0x0200, 0, point); SmokeNative.PostMessage(window.Handle, 0x0201, 1, point); SmokeNative.PostMessage(window.Handle, 0x0202, 0, point); window.PumpEvents(); window.RenderNow();
    }
    Click(scene.ThemeButton); Require(window.Document.Theme == Theme.Light, "Win32 click changes theme");
    Click(scene.Search);
    foreach (char c in "Aurora") SmokeNative.PostMessage(window.Handle, 0x0102, c, 0);
    window.PumpEvents(); window.RenderNow();
    Require(scene.Search.Text == "Aurora" && scene.Library.ItemCount == 16667, "Win32 text input filters 100000 rows");
    scene.Library.ScrollOffset = 5400; window.RenderNow(); Require(scene.Library.FirstVisibleIndex == 100 && scene.Library.RealizedCount < 12, "virtualization after scrolling");
    Click(scene.LaunchButton); Require(scene.ModalOpen, "modal opens through native input");
    Click(scene.ThemeButton); Require(window.Document.Theme == Theme.Light, "modal blocks background input");
    Click(scene.ConfirmButton); Require(!scene.ModalOpen && scene.Status.StartsWith("Applied:"), "confirm updates workspace and restores focus");
    SmokeNative.SetWindowPos(window.Handle, 0, 0, 0, 1000, 760, 0x0006); window.PumpEvents(); window.RenderNow(); Require(window.Document.Root.Bounds.Width < 1280, "native resize updates layout and GPU target");
    window.Renderer.SavePng(Path.Combine(artifactDir, "window-smoke.png"));
    File.WriteAllText(Path.Combine(artifactDir, "window-smoke.json"), JsonSerializer.Serialize(new { adapter = window.Renderer.Adapter, checks, stats = window.LastFrame.ToString(), scale = window.Scale }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(string.Join("\n", checks)); Console.WriteLine(window.Renderer.Adapter); Console.WriteLine(window.LastFrame);
}
else window.Run();

internal static class SmokeNative
{
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern bool PostMessage(nint h, uint m, nuint w, nint l);
    [DllImport("user32")] internal static extern bool SetWindowPos(nint h, nint after, int x, int y, int w, int height, uint flags);
}
