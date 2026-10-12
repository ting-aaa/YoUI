using System.Diagnostics;
using System.Text.Json;
using YoUI;
using YoUI.Platform.Linux;

string? output = null;
if (args.Length == 2 && args[0] == "--acceptance")
{
    output = Path.GetFullPath(args[1]);
    if (Directory.Exists(output) || File.Exists(output)) throw new ArgumentException("Acceptance output must not exist.");
    Directory.CreateDirectory(output);
}
else if (args.Length != 0) throw new ArgumentException("Usage: YoUI.Linux.Sample [--acceptance new-output-directory]");

var root = new StackPanel { Padding = 24, Gap = 16 };
root.Add(new Label("YoUI · Linux X11 prototype") { FontSize = 26, Bold = true });
var count = root.Add(new Label("Clicked 0 times"));
var button = root.Add(new Button("Click me") { Name = "button", Width = 160, Primary = true });
var input = root.Add(new TextBox { Name = "textbox", Width = 440 });
root.Add(new Label("SDL2 native input · wgpu surface\nX11 pixels; no automatic DPI scaling"));
int clicks = 0, closeRequests = 0, pointerCancels = 0;
button.Clicked += () => count.Text = $"Clicked {++clicks} times";
button.Event += e => { if (e.Phase == EventPhase.Target && e.Kind == EventKind.PointerCancel) pointerCancels++; };
var events = new Dictionary<string, int>();
using (var window = new DesktopWindow("YoUI Linux X11", root, 640, 400))
{
    window.Document.PreviewInput += e => events[e.Kind.ToString()] = events.GetValueOrDefault(e.Kind.ToString()) + 1;
    window.Closing = () => { closeRequests++; return output == null || closeRequests > 1; };
    var timer = Stopwatch.StartNew();
    window.RenderNow();
    bool threadGuard = false, duplicateGuard = false;
    if (output != null)
    {
        Task.Run(() => { try { window.PumpEvents(); } catch (InvalidOperationException) { threadGuard = true; } }).GetAwaiter().GetResult();
        try { using var duplicate = new DesktopWindow("duplicate", new StackPanel()); }
        catch (InvalidOperationException) { duplicateGuard = true; }
        if (!threadGuard || !duplicateGuard) throw new InvalidOperationException("Host ownership checks failed.");
    }
    object Bounds(Rect rect) => new { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height };
    void State()
    {
        if (output == null) return;
        var value = new
        {
            xid = (ulong)window.X11Window, adapter = window.Renderer.Adapter, sdlVersion = window.SdlVersion,
            width = window.Width, height = window.Height, frames = window.RenderedFrames,
            clicks, text = input.Text, focus = window.Document.Focused?.Name,
            hasInputFocus = window.HasInputFocus, suspended = window.IsSuspended,
            closeRequests, closed = window.IsClosed, pointerCancels, threadGuard, duplicateGuard,
            eventCounts = events, button = Bounds(button.VisualBounds), textbox = Bounds(input.VisualBounds)
        };
        string temp = Path.Combine(output, "state.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(value));
        File.Move(temp, Path.Combine(output, "state.json"), true);
    }
    State();
    Console.WriteLine($"X11 window {window.X11Window}; SDL {window.SdlVersion}; {window.Renderer.Adapter}");
    window.Run(output == null ? null : () =>
    {
        if (timer.Elapsed > TimeSpan.FromMinutes(2)) throw new TimeoutException("Native acceptance input did not finish within two minutes.");
        State();
    });
    if (output != null) { window.Renderer.SavePng(Path.Combine(output, "frame.png")); State(); }
}
// Actual second construction verifies that global SDL ownership and native resources were released.
if (output != null)
{
    using var reopened = new DesktopWindow("YoUI Linux lifecycle", new Label("Reopened"));
    reopened.PumpEvents(); reopened.RenderNow();
    File.WriteAllText(Path.Combine(output, "lifecycle.json"), JsonSerializer.Serialize(new { reopened = true, frames = reopened.RenderedFrames }));
}
