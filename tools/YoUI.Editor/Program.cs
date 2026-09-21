using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using YoUI;
using YoUI.Assets;
using YoUI.Editor;
using YoUI.Editor.App;
using YoUI.Platform.Windows;

string? Option(string name) { int i = Array.IndexOf(args, name); if (i < 0) return null; if (i + 1 >= args.Length || args[i + 1].StartsWith("--")) throw new ArgumentException("Missing value for " + name); return args[i + 1]; }
var artifacts = Path.GetFullPath(Environment.GetEnvironmentVariable("YOUI_ARTIFACT_DIR") ?? Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
Directory.CreateDirectory(artifacts);
var shell = new EditorShell();
if (Option("--open") is { } file) shell.Session.Open(file);
if (Option("--plugin") is { } plugin) shell.Exporters.LoadPlugin(plugin);
if (Option("--exporter") is { } exporter) shell.SelectExporter(exporter);
if (Option("--export") is { } destination)
{
    ExportCatalog.Write(shell.Exporter, shell.Session.Asset, destination); Console.WriteLine($"Exported {shell.Exporter.Id}: {Path.GetFullPath(destination)}"); return;
}
if (args.Contains("--headless"))
{
    using var renderer = new NativeRenderer(1440, 900);
    var doc = new UiDocument(shell, renderer, 1440, 900); renderer.Render(doc.BuildFrame()); renderer.SavePng(Path.Combine(artifacts, "editor.png"));
    shell.Session.Select(shell.Session.Asset.Root.DescendantsAndSelf().First(n => n.Kind == NodeKind.Button).Id); renderer.Render(doc.BuildFrame()); renderer.SavePng(Path.Combine(artifacts, "editor-selected.png"));
    string selectedId = shell.Session.SelectedId;
    shell.Session.SetPivot(selectedId, new(0, 1), shell.Canvas.ParentContentBounds(selectedId));
    shell.Session.SetAnchor(selectedId, AnchorMode.BottomRight, shell.Canvas.ParentContentBounds(selectedId));
    renderer.Render(doc.BuildFrame()); renderer.SavePng(Path.Combine(artifacts, "editor-anchor.png")); shell.Session.Undo(); shell.Session.Undo();
    shell.PropertySearch.Text = "Visibility"; renderer.Render(doc.BuildFrame()); renderer.SavePng(Path.Combine(artifacts, "editor-properties.png")); shell.PropertySearch.Text = "";
    shell.TogglePreview(); renderer.Render(doc.BuildFrame()); renderer.SavePng(Path.Combine(artifacts, "editor-preview.png")); shell.TogglePreview();
    doc.Resize(1100, 760); renderer.Resize(1100, 760); renderer.Render(doc.BuildFrame()); renderer.SavePng(Path.Combine(artifacts, "editor-compact.png"));
    Console.WriteLine(renderer.Adapter + "\nEditor GPU frames: edit, selection, compensated anchors, filtered properties, preview, compact."); return;
}
using var window = new DesktopWindow("YoUI Editor — Native UI authoring", shell, 1440, 900, 1100, 760);
bool Save(bool saveAs = false)
{
    shell.CommitInput(); string? target = !saveAs ? shell.Session.FilePath : null;
    target ??= FileDialogs.Save(window.Handle, "Save UI configuration", shell.Session.FilePath ?? "screen.youi.json");
    if (target == null) return false; shell.Session.Save(target); shell.SetStatus("Saved: " + target); return true;
}
bool GuardUnsaved()
{
    shell.CommitInput(); if (!shell.Session.IsDirty) return true;
    return FileDialogs.ConfirmUnsaved(window.Handle) switch { 6 => Save(), 7 => true, _ => false };
}
void Open(string? requestedPath = null)
{
    if (!GuardUnsaved()) return;
    string? path = requestedPath ?? FileDialogs.Open(window.Handle, "Open UI configuration"); if (path == null) return;
    if (shell.Canvas.Preview) shell.TogglePreview(); shell.Session.Open(path); shell.Canvas.Fit(); shell.SetStatus("Opened: " + path);
}
shell.NewRequested = () => shell.Try(() => { if (GuardUnsaved()) { if (shell.Canvas.Preview) shell.TogglePreview(); shell.Session.New(); shell.Canvas.Fit(); shell.SetStatus("New screen"); } });
shell.OpenRequested = () => shell.Try(() => Open());
shell.OpenFileRequested = path => shell.Try(() => Open(path));
shell.SaveRequested = () => shell.Try(() => Save()); shell.SaveAsRequested = () => shell.Try(() => Save(true));
shell.ExportRequested = () => shell.Try(() =>
{
    shell.CommitInput(); var path = FileDialogs.Save(window.Handle, "Export to a NEW folder (enter folder name)", "screen-export", "", "Export destination folder name", "*");
    if (path == null) return; ExportCatalog.Write(shell.Exporter, shell.Session.Asset, path); shell.SetStatus("Exported " + shell.Exporter.DisplayName + " → " + path);
});
shell.PluginRequested = () => shell.Try(() =>
{
    var path = FileDialogs.Open(window.Handle, "Load trusted local exporter plugin", "Exporter assembly", "*.dll"); if (path == null) return;
    var found = shell.Exporters.LoadPlugin(path); shell.SelectExporter(found[0].Id); shell.SetStatus("Loaded: " + string.Join(", ", found.Select(e => e.DisplayName)));
});
window.Closing = () => { bool result = false; shell.Try(() => result = GuardUnsaved()); return result; };
if (!args.Contains("--smoke")) { window.Run(); return; }

var checks = new List<string>();
void Require(bool condition, string name) { if (!condition) throw new Exception("Editor smoke failed: " + name); checks.Add(name); }
void Pointer(uint message, Vector2 point)
{
    int x = (int)(point.X * window.Scale), y = (int)(point.Y * window.Scale); Smoke.PostMessage(window.Handle, message, message == 0x201 ? 1u : 0u, (nint)((y << 16) | (x & 0xffff))); window.PumpEvents(); window.RenderNow();
}
void Click(Vector2 point) { Pointer(0x200, point); Pointer(0x201, point); Pointer(0x202, point); }
static Vector2 Center(Rect b) => new(b.X + b.Width / 2, b.Y + b.Height / 2);
window.RenderNow();
var heading = shell.Session.Asset.Root.Children.First(n => n.Name == "Heading");
Click(Center(shell.Canvas.NodeBounds(heading.Id))); Require(shell.Session.SelectedId == heading.Id, "Native canvas selects a runtime node");
var selected = shell.Canvas.NodeBounds(heading.Id); var from = Center(selected); float scale = shell.Canvas.Zoom;
Pointer(0x201, from); Pointer(0x200, from + new Vector2(32, 16) * scale); Pointer(0x202, from + new Vector2(32, 16) * scale);
Require(shell.Session.Find(heading.Id).X == heading.X + 32 && shell.Session.Find(heading.Id).Y == heading.Y + 16, "Native drag edits artboard coordinates with grid snapping");
Click(Center(shell.UndoButton.Bounds)); Require(shell.Session.Find(heading.Id).X == heading.X, "Undo restores the complete drag transaction");
Click(Center(shell.RedoButton.Bounds)); Require(shell.Session.Find(heading.Id).X == heading.X + 32, "Redo restores the move");
var handle = shell.Canvas.NodeBounds(heading.Id); from = new(handle.Right, handle.Bottom);
Pointer(0x201, from); Pointer(0x200, from + new Vector2(16, 8) * shell.Canvas.Zoom); Pointer(0x202, from + new Vector2(16, 8) * shell.Canvas.Zoom);
Require(shell.Session.Find(heading.Id).Width == heading.Width + 16, "Native resize handle changes dimensions");
IEnumerable<Element> Elements(Element root) { yield return root; foreach (var child in root.Children) foreach (var element in Elements(child)) yield return element; }
var priorBounds = shell.Canvas.Instance.Nodes[heading.Id].Bounds;
var references = Elements(shell.Inspector).OfType<ReferencePicker>().ToArray();
Click(Center(references[1].Presets[6].VisualBounds));
Require(shell.Session.Selected.Pivot == new Vector2(0, 1) && shell.Canvas.Instance.Nodes[heading.Id].Bounds == priorBounds, "Native pivot grid keeps the component fixed and compensates XY");
Click(Center(references[0].Presets[8].VisualBounds));
Require(shell.Session.Selected.Anchor == AnchorMode.BottomRight && shell.Canvas.Instance.Nodes[heading.Id].Bounds == priorBounds, "Native parent anchor grid preserves the rendered rectangle");
Click(Center(shell.UndoButton.Bounds)); Require(shell.Session.Selected.Anchor == AnchorMode.TopLeft && shell.Canvas.Instance.Nodes[heading.Id].Bounds == priorBounds, "Anchor and position undo together");
Click(Center(shell.RedoButton.Bounds)); Require(shell.Session.Selected.Anchor == AnchorMode.BottomRight && shell.Canvas.Instance.Nodes[heading.Id].Bounds == priorBounds, "Anchor and position redo together");
string pathSaved = Path.Combine(artifacts, "editor-smoke.youi.json"); shell.Session.Save(pathSaved); shell.Session.Open(pathSaved);
Require(!shell.Session.IsDirty && shell.Session.Find(heading.Id).Width == heading.Width + 16, "Save/reopen preserves authored geometry");
bool releasedBeforeClick = false; shell.PreviewButton.Clicked += () => releasedBeforeClick = Smoke.GetCapture() == 0;
Click(Center(shell.PreviewButton.Bounds)); Require(shell.Canvas.Preview, "Native preview button enters the same runtime");
Require(releasedBeforeClick, "OS capture is released before click handlers can open modal dialogs");
var toggle = shell.Session.Asset.Root.DescendantsAndSelf().First(n => n.Kind == NodeKind.Toggle);
Click(Center(shell.Canvas.NodeBounds(toggle.Id))); Require(((Toggle)shell.Canvas.Instance.Nodes[toggle.Id]).Value != toggle.Checked && !shell.Session.IsDirty, "Preview toggle is interactive without changing the asset");
var button = shell.Session.Asset.Root.DescendantsAndSelf().First(n => n.Kind == NodeKind.Button);
Click(Center(shell.Canvas.NodeBounds(button.Id))); Require(shell.Status.Contains(button.Command), "Preview command reaches the host binding");
var input = shell.Session.Asset.Root.DescendantsAndSelf().First(n => n.Kind == NodeKind.TextBox);
Click(Center(shell.Canvas.NodeBounds(input.Id))); Smoke.PostMessage(window.Handle, 0x102, '!', 0); window.PumpEvents(); window.RenderNow();
Require(((TextBox)shell.Canvas.Instance.Nodes[input.Id]).Text.EndsWith('!'), "Native text input reaches the embedded runtime");
Smoke.PostMessage(window.Handle, 0x100, 9, 0); window.PumpEvents(); Require(shell.Canvas.Runtime.Focused is Toggle, "Preview Tab navigates embedded controls");
Click(Center(shell.PreviewButton.Bounds)); Require(!shell.Canvas.Preview && shell.Canvas.Instance.Nodes[input.Id] is TextBox box && box.Text == input.Text, "Leaving preview resets transient control state");
window.RenderNow(); window.Renderer.SavePng(Path.Combine(artifacts, "editor-window-smoke.png"));
File.WriteAllText(Path.Combine(artifacts, "editor-window-smoke.json"), JsonSerializer.Serialize(new { adapter = window.Renderer.Adapter, checks, scale = window.Scale, stats = window.LastFrame.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(string.Join("\n", checks));

internal static class Smoke
{
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern bool PostMessage(nint h, uint m, nuint w, nint l);
    [DllImport("user32")] internal static extern nint GetCapture();
}
