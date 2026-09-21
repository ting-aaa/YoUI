using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using YoUI;
using YoUI.Assets;
using YoUI.Editor;
using YoUI.Editor.App;

var results = new List<string>();
string rootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string output = Path.GetFullPath(Path.Combine(rootPath, "artifacts/editor-validation", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(output);
void Test(string name, Action run)
{
    try { run(); results.Add(name); Console.WriteLine("PASS " + name); }
    catch (Exception error)
    {
        // Report assertion failures without an unhandled CLR exception / Windows crash dialog.
        string failure = "FAIL " + name + Environment.NewLine + error;
        Console.Error.WriteLine(failure); File.WriteAllText(Path.Combine(output, "failure.log"), failure); Environment.Exit(1);
    }
}
void Require(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action action) { bool rejected = false; try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException or ArgumentException or IOException) { rejected = true; } Require(rejected, "Expected rejected operation"); }
UiDocument Doc(Element root, float width = 1440, float height = 900) => new(root, new Measurer(), width, height);
void Click(UiDocument doc, Vector2 p) { doc.Dispatch(new(EventKind.PointerDown) { Position = p }); doc.Dispatch(new(EventKind.PointerUp) { Position = p }); }
IEnumerable<Element> Elements(Element root) { yield return root; foreach (var child in root.Children) foreach (var e in Elements(child)) yield return e; }
Vector2 Mid(Element e) => new(e.VisualBounds.X + e.Bounds.Width / 2, e.VisualBounds.Y + e.Bounds.Height / 2);

Test("Versioned assets round-trip Unicode and reject invalid structure, colors, IDs and limits", () =>
{
    var a = EditorSession.Starter(); a.Root.Children[0].Text = "你好 👨‍👩‍👧‍👦 \"quoted\"\nsecond line";
    string json = UiAssetJson.Serialize(a); Require(UiAssetJson.Serialize(UiAssetJson.Parse(json)) == json);
    Reject(() => UiAssetJson.Parse("{}")); Reject(() => UiAssetJson.Parse(json.Replace("\"version\": 1", "\"version\": 2")));
    Reject(() => UiAssetJson.Parse(json.Replace("\"darkTheme\": true", "\"unknown\": true")));
    a.Root.Children[0].Fill = "bad"; Reject(() => UiAssetJson.Validate(a)); a.Root.Children[0].Fill = "#ffffff";
    a.Root.Children[0].X = float.NaN; Reject(() => UiAssetJson.Validate(a)); a.Root.Children[0].X = 0;
    a.Root.Children[0].Id = a.Root.Id; Reject(() => UiAssetJson.Validate(a));
    a = new(); var n = a.Root; for (int i = 0; i < 25; i++) { var c = new UiNode(); n.Children.Add(c); n = c; } Reject(() => UiAssetJson.Validate(a));
    a = new(); a.Root.Children = Enumerable.Range(0, 300).Select(_ => new UiNode { Kind = NodeKind.Text, Text = new string('x', 16000) }).ToList(); Reject(() => UiAssetJson.Serialize(a));
});
Test("Selection, duplicate subtree IDs, order, reparent cycles and deletion are undoable", () =>
{
    var s = new EditorSession(); var panel = s.Asset.Root.Children.Last(); s.Select(panel.Id); s.Duplicate();
    var all = s.Asset.Root.DescendantsAndSelf().ToArray(); Require(all.Select(n => n.Id).Distinct().Count() == all.Length && s.Selected.Children.Count == panel.Children.Count);
    string copy = s.SelectedId; s.Move(-1); Require(s.Asset.Root.Children[^2].Id == copy); s.Undo(); Require(s.Asset.Root.Children[^1].Id == copy);
    Reject(() => s.Reparent(copy, s.Selected.Children[0].Id)); Reject(() => s.Reparent(copy, copy));
    string heading = s.Asset.Root.Children[1].Id; s.Reparent(heading, copy); Require(s.ParentOf(heading)!.Id == copy); s.Undo(); Require(s.ParentOf(heading)!.Id == s.Asset.Root.Id);
    s.Select(copy); s.Delete(); Require(s.Asset.Root.Children.Count == 4); s.Undo(); Require(s.SelectedId == copy); s.Redo(); Require(s.Asset.Root.Children.Count == 4);
});
Test("Drag transactions coalesce, cancel and reject invalid edits without changing the model", () =>
{
    var s = new EditorSession(); s.Select(s.Asset.Root.Children[0].Id); float x = s.Selected.X;
    s.BeginTransaction(); for (int i = 1; i <= 10; i++) s.UpdateSelected(n => n.X = x + i); s.EndTransaction(); s.Undo(); Require(s.Selected.X == x && !s.CanUndo); s.Redo(); Require(s.Selected.X == x + 10);
    s.BeginTransaction(); s.UpdateSelected(n => n.X = 300); s.EndTransaction(true); Require(s.Selected.X == x + 10);
    string before = UiAssetJson.Serialize(s.Asset); Reject(() => s.UpdateSelected(n => n.Width = -2)); Require(UiAssetJson.Serialize(s.Asset) == before);
});
Test("Atomic save/open preserves state on invalid input and dirty state follows undo", () =>
{
    var s = new EditorSession(); string path = Path.Combine(output, "saved.youi.json"); s.Save(path); Require(!s.IsDirty);
    s.UpdateSelected(n => n.Width = 720); Require(s.IsDirty); s.Undo(); Require(!s.IsDirty); s.Redo(); s.Save();
    var other = new EditorSession(); other.Open(path); Require(other.Asset.Root.Width == 720 && other.FilePath == path);
    string bad = Path.Combine(output, "bad.json"); File.WriteAllText(bad, "{}"); Reject(() => other.Open(bad)); Require(other.Asset.Root.Width == 720 && other.FilePath == path);
});
Test("Runtime layout owns stack child positions and resolves center/stretch anchors", () =>
{
    var a = new UiAsset(); var stack = new UiNode { Kind = NodeKind.VerticalStack, X = 10, Y = 10, Width = 200, Height = 200, Padding = 5, Gap = 8, Children = [new() { X = 500, Width = 100, Height = 30 }, new() { Width = 100, Height = 40 }] };
    var centered = new UiNode { Anchor = AnchorMode.Center, Width = 100, Height = 50 }; var stretch = new UiNode { Anchor = AnchorMode.Stretch, X = 10, Y = 20, Width = 30, Height = 40 };
    a.Root.Children = [stack, centered, stretch]; var ui = UiAssetRuntime.Build(a); var doc = ui.CreateDocument(new Measurer(), 640, 480); doc.EnsureLayout();
    Require(ui.Nodes[stack.Children[0].Id].Bounds == new Rect(15, 15, 100, 30)); Require(ui.Nodes[stack.Children[1].Id].Bounds.Y == 53);
    Require(ui.Nodes[centered.Id].Bounds == new Rect(270, 215, 100, 50)); Require(ui.Nodes[stretch.Id].Bounds == new Rect(10, 20, 600, 420));
});
Test("Viewport append transforms text, clip and all triangle vertices without losing scopes", () =>
{
    var source = new DrawList(); source.PushClip(new(1, 2, 30, 40)); source.Text(new(3, 4, 50, 20), "你好", Color.Hex(0xffffff), 12); source.BeginLayer(.5f); source.Triangle(new(1, 2), new(3, 4), new(5, 6), Color.Hex(0xff0000)); source.EndLayer(); source.PopClip();
    var target = new DrawList(); target.Text(new(0, 0, 20, 20), "prefix", Color.Hex(0xffffff)); target.Append(source, new(10, 20), 2);
    Require(target.Commands[1].Rect == new Rect(12, 24, 60, 80)); Require(target.Commands[4].Rect == new Rect(12, 24, 16, 28)); Require(target.Commands[4].Reserved0 == 20 && target.Commands[4].Reserved1 == 32);
    var c = target.Commands[2]; Require(c.FontSize == 24 && Encoding.UTF8.GetString(target.TextBytes.Slice((int)c.TextOffset, (int)c.TextLength)) == "你好"); Require(target.Commands[^1].Kind == 3 && target.Commands[^2].Kind == 5);
});
Test("Reusable ScrollView and PropertyGrid commit, validate and reveal keyboard focus", () =>
{
    var grid = new PropertyGrid(); int value = 12; string error = ""; grid.ValidationFailed += message => error = message;
    grid.SetEntries(Enumerable.Range(0, 12).Select(i => new PropertyEntry("Number " + i, () => value.ToString(), s => value = int.Parse(s))));
    var doc = Doc(grid, 300, 180); doc.BuildFrame(); var first = Elements(grid).OfType<TextBox>().First();
    doc.Focus(first); first.Text = "42"; doc.Dispatch(new(EventKind.KeyDown) { Key = 13 }); Require(value == 42);
    first.Text = "bad"; doc.Dispatch(new(EventKind.KeyDown) { Key = 13 }); Require(value == 42 && error.Length > 0);
    var last = Elements(grid).OfType<TextBox>().Last(); doc.Focus(last); Require(grid.ScrollOffset > 0 && last.VisualBounds.Bottom <= grid.Bounds.Bottom + .1f);
    var compact = Doc(new TextBox { Text = "Readable", Height = 32 }, 200, 32).BuildFrame();
    var clip = compact.Commands.ToArray().Last(c => c.Kind == 2).Rect; Require(clip.Height >= 20, "Compact input must leave enough vertical clip space for glyphs and caret");
});
Test("Independent pivots preserve the exact top-left to bottom-left example and undo atomically", () =>
{
    var a = new UiAsset(); var n = new UiNode { X = 0, Y = 0, Width = 120, Height = 60 }; a.Root.Children.Add(n);
    var s = new EditorSession(a); var parent = new Rect(0, 0, 640, 480); s.Select(n.Id);
    s.SetPivot(n.Id, new(0, 1), parent);
    Require(s.Selected.X == 0 && s.Selected.Y == 60 && s.Selected.Transform.Resolve(parent) == new Rect(0, 0, 120, 60));
    s.Undo(); Require(s.Selected.X == 0 && s.Selected.Y == 0 && !s.CanUndo); s.Redo(); Require(s.Selected.Y == 60);
    string path = Path.Combine(output, "pivot.youi.json"); s.Save(path); s.Open(path); s.Select(n.Id); Require(s.Selected.Pivot == new Vector2(0, 1) && s.Selected.Y == 60);
    Reject(() => s.SetPivot(n.Id, new(float.NaN, 0), parent));
});
Test("Every anchor and pivot transition preserves runtime bounds in a padded nested parent", () =>
{
    var a = new UiAsset(); var n = new UiNode { X = -24, Y = 37, Width = 360, Height = 90 };
    var parent = new UiNode { X = 50, Y = 60, Width = 300, Height = 220, Padding = 12, Children = [n] }; a.Root.Children.Add(parent);
    var s = new EditorSession(a); s.Select(n.Id);
    Rect Bounds()
    {
        var ui = UiAssetRuntime.Build(s.Asset); ui.CreateDocument(new Measurer(), 640, 480).EnsureLayout(); return ui.Nodes[n.Id].Bounds;
    }
    var content = new Rect(50, 60, 300, 220).Inset(12); var expected = Bounds();
    foreach (var anchor in Enum.GetValues<AnchorMode>())
    {
        s.SetAnchor(n.Id, anchor, content); Require(Bounds() == expected, "Anchor " + anchor);
        if (anchor == AnchorMode.Stretch) { Require(s.Selected.Width < 0, "Off-parent stretch must allow negative trailing inset"); continue; }
        for (int i = 0; i < 9; i++) { s.SetPivot(n.Id, new(i % 3 * .5f, i / 3 * .5f), content); Require(Bounds() == expected, "Pivot " + i); }
    }
    s.SetAnchor(n.Id, AnchorMode.TopLeft, content); s.SetPivot(n.Id, Vector2.Zero, content); Require(s.Selected.X == -24 && s.Selected.Y == 37 && s.Selected.Width == 360);
    var centered = new UiNode { Anchor = AnchorMode.Center, Width = 100, Height = 50 }; Require(centered.Pivot == new Vector2(.5f));
    s.Edit(asset => asset.Root.Children[0].Kind = NodeKind.VerticalStack);
    Reject(() => s.SetAnchor(n.Id, AnchorMode.Center, content)); Reject(() => s.SetPivot(n.Id, Vector2.One, content));
});
Test("Inspector spatial controls, search, folding and enum choices respond to real routed input", () =>
{
    var shell = new EditorShell(); var doc = Doc(shell); shell.Session.Select(shell.Session.Asset.Root.Children[1].Id); doc.BuildFrame();
    var id = shell.Session.SelectedId; var before = shell.Canvas.Instance.Nodes[id].Bounds;
    shell.PropertySearch.Text = "pivot"; doc.BuildFrame();
    var picker = Elements(shell.Inspector).OfType<ReferencePicker>().Last(); shell.Inspector.ScrollIntoView(picker); doc.BuildFrame();
    Click(doc, Mid(picker.Presets[6])); doc.BuildFrame();
    Require(shell.Session.Selected.Pivot == new Vector2(0, 1)); Require(shell.Canvas.Instance.Nodes[id].Bounds == before);
    shell.PropertySearch.Text = "Position"; doc.BuildFrame();
    Require(Elements(shell.Inspector).OfType<TextBox>().First(e => e.AccessibleName == "Position Y").Text == shell.Session.Selected.Y.ToString());
    shell.PropertySearch.Text = ""; doc.BuildFrame(); shell.Inspector.ScrollTo(0);
    shell.PropertySearch.Text = "Node"; doc.BuildFrame();
    var header = Elements(shell.Inspector).OfType<Button>().First(b => b.Text == "▾  Node"); shell.PropertySearch.Text = ""; doc.BuildFrame();
    Require(header.Text == "▸  Node"); shell.Inspector.ScrollIntoView(header); doc.BuildFrame(); Click(doc, Mid(header)); doc.BuildFrame(); Require(header.Text == "▾  Node"); Click(doc, Mid(header)); doc.BuildFrame(); Require(header.Text == "▸  Node");
    shell.PropertySearch.Text = "Name"; doc.BuildFrame(); Require(header.Text == "▾  Node");
    var grid = new PropertyGrid(); string choice = "A"; grid.SetEntries([new("Choice", () => choice, value => choice = value, Choices: ["A", "B", "C"])]);
    var choicesDoc = Doc(grid, 300, 240); choicesDoc.BuildFrame(); var choices = Elements(grid).OfType<PropertyChoice>().Single();
    Click(choicesDoc, Mid(choices.Children[0])); choicesDoc.BuildFrame(); Click(choicesDoc, Mid(choices.Children[3])); Require(choice == "C");
});
Test("Bottom-right pivot canvas resizing keeps the opposite corner fixed", () =>
{
    var shell = new EditorShell(); var doc = Doc(shell); shell.Session.Select(shell.Session.Asset.Root.Children[1].Id); doc.BuildFrame();
    var id = shell.Session.SelectedId; shell.Session.SetPivot(id, Vector2.One, shell.Canvas.ParentContentBounds(id)); doc.BuildFrame();
    var before = shell.Canvas.Instance.Nodes[id].Bounds; var b = shell.Canvas.NodeBounds(id); var p = new Vector2(b.Right, b.Bottom);
    doc.Dispatch(new(EventKind.PointerDown) { Position = p }); doc.Dispatch(new(EventKind.PointerMove) { Position = p + new Vector2(16, 8) * shell.Canvas.Zoom }); doc.Dispatch(new(EventKind.PointerUp) { Position = p }); doc.BuildFrame();
    var after = shell.Canvas.Instance.Nodes[id].Bounds; Require(after.X == before.X && after.Y == before.Y && after.Width == before.Width + 16 && after.Height == before.Height + 8);
});
Test("Project files filter and request guarded opening; resource references select their owning node", () =>
{
    var s = new EditorSession(); string path = Path.Combine(output, "project.youi.json"); s.Save(path);
    var browser = new ProjectBrowser(output); var doc = Doc(browser, 240, 300); string opened = ""; browser.OpenRequested += p => opened = p;
    browser.Search.Text = "project.youi"; doc.BuildFrame(); Click(doc, new(70, 98)); doc.BuildFrame(); Click(doc, Mid(browser.OpenButton)); Require(opened == path);
    var shell = new EditorShell(); var shellDoc = Doc(shell); shellDoc.BuildFrame();
    var colors = shell.Resources.Bounds; Click(shellDoc, new(colors.X + 70, colors.Y + 42)); Require(shell.Session.SelectedId == shell.Session.Asset.Root.Id);
    shell.ComponentSearch.Text = "Button"; shellDoc.BuildFrame(); var add = shell.Children.OfType<Button>().Single(b => b.Text == "Button"); Click(shellDoc, Mid(add)); Require(shell.Session.Selected.Kind == NodeKind.Button);
    shell.TogglePreview(); Require(!shell.Inspector.Enabled && !add.Enabled); shell.TogglePreview();
    shellDoc.Resize(1100, 760); shellDoc.BuildFrame(); Require(shell.Canvas.Bounds.Right <= shell.Inspector.Bounds.X && shell.Project.Bounds.Bottom < 760 && shell.Canvas.Bounds.Height > 300);
});
Test("Virtualized TreeView selects, navigates, collapses and requests container drops", () =>
{
    var children = Enumerable.Range(0, 200).Select(i => new TreeItem("child" + i, "Node " + i, "Panel", true, [])).ToArray();
    var tree = new TreeView { AllowReparent = true }; tree.SetItems([new("root", "Root", "Canvas", true, children)]); var doc = Doc(tree, 230, 300); var frame = doc.BuildFrame();
    Require(frame.Commands.ToArray().Count(c => c.Kind == 1) < 30); // only visible rows produce text commands
    Click(doc, new(60, 50)); Require(tree.SelectedId == "child0"); doc.Dispatch(new(EventKind.KeyDown) { Key = 40 }); Require(tree.SelectedId == "child1");
    string dropped = ""; tree.ReparentRequested += (a, b) => dropped = a + ":" + b;
    doc.Dispatch(new(EventKind.PointerDown) { Position = new(60, 50) }); doc.Dispatch(new(EventKind.PointerMove) { Position = new(60, 110) }); doc.Dispatch(new(EventKind.PointerUp) { Position = new(60, 110) }); Require(dropped == "child0:child2");
});
Test("Editor preview is the runtime and does not mutate authored state; idle reuses its frame", () =>
{
    var shell = new EditorShell(); var doc = Doc(shell); doc.BuildFrame(); int paints = doc.PaintPasses; doc.BuildFrame(); Require(doc.PaintPasses == paints);
    string before = UiAssetJson.Serialize(shell.Session.Asset); shell.TogglePreview(); doc.BuildFrame();
    var toggle = shell.Session.Asset.Root.DescendantsAndSelf().First(n => n.Kind == NodeKind.Toggle); var bounds = shell.Canvas.NodeBounds(toggle.Id);
    Click(doc, new(bounds.X + 20, bounds.Y + 15)); Require(((Toggle)shell.Canvas.Instance.Nodes[toggle.Id]).Value != toggle.Checked); Require(UiAssetJson.Serialize(shell.Session.Asset) == before);
    shell.TogglePreview(); Require(((Toggle)shell.Canvas.Instance.Nodes[toggle.Id]).Value == toggle.Checked);
});
Test("Global undo during a canvas drag cancels the transaction and subsequent moves remain idle", () =>
{
    var shell = new EditorShell(); var doc = Doc(shell); doc.BuildFrame(); var n = shell.Session.Asset.Root.Children[1]; var b = shell.Canvas.NodeBounds(n.Id); var p = new Vector2(b.X + 20, b.Y + 20);
    doc.Dispatch(new(EventKind.PointerDown) { Position = p }); doc.Dispatch(new(EventKind.PointerMove) { Position = p + new Vector2(32, 16) }); Require(shell.Session.InTransaction);
    doc.Dispatch(new(EventKind.KeyDown) { Key = 90, Control = true }); Require(!shell.Session.InTransaction && shell.Session.Find(n.Id).X == n.X);
    doc.Dispatch(new(EventKind.PointerMove) { Position = p + new Vector2(80, 40) }); doc.Dispatch(new(EventKind.PointerUp) { Position = p }); Require(!shell.Session.IsDirty && !shell.Session.CanUndo);
});
Test("Export host loads a real plugin, rejects duplicate IDs and contains returned paths", () =>
{
#if DEBUG
    const string configuration = "Debug";
#else
    const string configuration = "Release";
#endif
    var catalog = new ExportCatalog(); string plugin = Path.Combine(rootPath, $"samples/YoUI.Exporter.Sample/bin/{configuration}/net9.0/YoUI.Exporter.Sample.dll");
    var found = catalog.LoadPlugin(plugin); Require(found.Count == 1 && found[0].Id == "sample.inventory"); Reject(() => catalog.LoadPlugin(plugin));
    string directory = Path.Combine(output, "plugin-output"); ExportCatalog.Write(found[0], EditorSession.Starter(), directory); Require(File.ReadAllText(Path.Combine(directory, "inventory.md")).Contains("project.create"));
    Reject(() => ExportCatalog.Write(found[0], EditorSession.Starter(), directory));
    Reject(() => ExportCatalog.Write(new BadExporter(), EditorSession.Starter(), Path.Combine(output, "bad-output"))); Require(!File.Exists(Path.Combine(output, "escape.txt")));
});
Test("Exported C# compiles and matches JSON runtime command IR and behavior with no editor dependency", () =>
{
    var a = EditorSession.Starter(); a.Root.Children[0].Text = "中文 \"quoted\" \\ path 👋";
    a.Root.Children.Add(new() { Kind = NodeKind.HorizontalStack, Width = 360, Height = 48, Anchor = AnchorMode.Center, Padding = 4, Children = [new() { Kind = NodeKind.Button, Text = "Test", Width = 120, Height = 40, Foreground = "#FF8844", Fill = "#223344", Bold = true }, new() { Kind = NodeKind.Text, Text = "Stack", Width = 140, Height = 30 }] });
    a.Root.Children.Add(new() { Kind = NodeKind.Panel, Anchor = AnchorMode.Stretch, X = 8, Y = 8, Width = 8, Height = 8, Opacity = .5f });
    foreach (var anchor in Enum.GetValues<AnchorMode>()) a.Root.Children.Add(new() { Kind = NodeKind.Panel, Anchor = anchor, X = 8, Y = 16, Width = 30, Height = 22, PivotX = .25f, PivotY = 1, Fill = "#AACCFF" });
    string folder = Path.Combine(output, "compiled"); ExportCatalog.Write(new CSharpExporter(), a, folder);
    string runtimeProject = System.Security.SecurityElement.Escape(Path.Combine(rootPath, "managed/YoUI/YoUI.csproj"));
    File.WriteAllText(Path.Combine(folder, "Generated.csproj"), $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"{runtimeProject}\" /></ItemGroup></Project>");
    File.WriteAllText(Path.Combine(folder, "Program.cs"), """
using System;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using YoUI;
using YoUI.Assets;
var a = UiAssetJson.Load(Path.Combine(AppContext.BaseDirectory, "../../../screen.youi.json"));
string generatedCommand = "", loadedCommand = "";
var generated = YoUI.Generated.Screen.Create(s => generatedCommand = s);
var loaded = UiAssetRuntime.Build(a, s => loadedCommand = s);
var d1 = generated.CreateDocument(new GeneratedMeasurer(), a.Root.Width, a.Root.Height, a.DarkTheme);
var d2 = loaded.CreateDocument(new GeneratedMeasurer(), a.Root.Width, a.Root.Height, a.DarkTheme);
var f1 = d1.BuildFrame(); var f2 = d2.BuildFrame();
if (!MemoryMarshal.AsBytes(f1.Commands).SequenceEqual(MemoryMarshal.AsBytes(f2.Commands)) || !f1.TextBytes.SequenceEqual(f2.TextBytes)) throw new Exception("Generated/config IR mismatch");
var node = a.Root.DescendantsAndSelf().First(n => n.Command == "project.create");
d1.Focus(generated.Nodes[node.Id]); d2.Focus(loaded.Nodes[node.Id]);
d1.Dispatch(new(EventKind.KeyDown) { Key = 13 }); d2.Dispatch(new(EventKind.KeyDown) { Key = 13 });
if (generatedCommand != "project.create" || generatedCommand != loadedCommand) throw new Exception("Binding mismatch");
Console.WriteLine("Generated source compiled; full command IR, UTF-8 and command binding match JSON runtime.");
sealed class GeneratedMeasurer : ITextMeasurer { public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) => (Math.Min(width, text.Length * size * .55f), size * 1.35f); }
""");
    var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = folder };
    foreach (string arg in new[] { "run", "--project", Path.Combine(folder, "Generated.csproj"), "-c", "Release" }) start.ArgumentList.Add(arg);
    using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(60000)) { process.Kill(true); throw new Exception("Generated code validation timed out"); }
    Task.WaitAll(stdout, stderr); Require(process.ExitCode == 0, stdout.Result + stderr.Result); Console.Write(stdout.Result);
});
File.WriteAllText(Path.Combine(rootPath, "artifacts/editor-tests.json"), JsonSerializer.Serialize(new { checks = results, generatedArtifacts = output }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{results.Count} editor tests passed. {output}");

sealed class Measurer : ITextMeasurer { public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) => (Math.Min(width, text.Length * size * .55f), size * 1.35f); }
sealed class BadExporter : IUiExporter
{
    public string Id => "bad"; public string DisplayName => "Bad";
    public IReadOnlyList<ExportFile> Export(ExportContext context) => [new("../escape.txt", "bad")];
}
