using System.Numerics;
using System.Runtime.InteropServices;
using YoUI;
using YoUI.Assets;
using YoUI.Editor;
using YoUI.Editor.App;

internal static class BetaAssetTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> require)
    {
        void Reject(Action run) { try { run(); } catch (InvalidDataException) { return; } throw new Exception("Invalid definition was accepted."); }
        test("Beta definitions enforce version, track bounds, table shape and integer selections", () =>
        {
            var a = new UiAsset { Version = 2 };
            var combo = new UiNode { Kind = NodeKind.ComboBox, Beta = new() { Items = ["你好", "Second"], SelectedIndex = 1 } };
            a.Root.Children.Add(combo); UiAssetJson.Validate(a);
            a.Version = 1; Reject(() => UiAssetJson.Validate(a)); a.Version = 2;
            combo.Beta!.SelectedIndex = 2; Reject(() => UiAssetJson.Validate(a)); combo.Beta.SelectedIndex = 1;
            var grid = new UiNode { Kind = NodeKind.Grid, Children = [new() { Beta = new() { ColumnSpan = 3 } }] };
            a.Root.Children.Add(grid); Reject(() => UiAssetJson.Validate(a)); grid.Children[0].Beta!.ColumnSpan = 2;
            grid.Beta = new() { GridColumns = ["NaN*"] }; Reject(() => UiAssetJson.Validate(a)); grid.Beta = null;
            var table = new UiNode { Kind = NodeKind.DataGrid, Beta = new() { TableRows = [["only one cell"]] } };
            a.Root.Children.Add(table); Reject(() => UiAssetJson.Validate(a));
            table.Beta!.TableRows = [["one", "two"]]; UiAssetJson.Validate(a);
            var tabs = new UiNode { Kind = NodeKind.TabControl, Children = [new()], Beta = new() { SelectedIndex = -1 } };
            a.Root.Children.Add(tabs); Reject(() => UiAssetJson.Validate(a)); tabs.Beta!.SelectedIndex = 0;
            UiAssetJson.Validate(a); _ = UiAssetRuntime.Build(a);
        });
        test("All Beta palette definitions survive editor undo save reload and runtime composition", () =>
        {
            foreach (var kind in Enum.GetValues<NodeKind>().Where(k => k > NodeKind.HorizontalStack))
            {
                var session = new EditorSession(new UiAsset()); session.Add(kind);
                string id = session.SelectedId; require(session.Asset.Version == 2, "Beta addition must upgrade definition version.");
                session.Undo(); require(session.Asset.Version == 1 && session.Asset.Root.Children.Count == 0, "Upgrade must be undoable."); session.Redo();
                var copy = UiAssetJson.Parse(UiAssetJson.Serialize(session.Asset));
                var instance = UiAssetRuntime.Build(copy); var doc = instance.CreateDocument(new Measurer(), 640, 480); doc.BuildFrame();
                require(instance.Nodes.ContainsKey(id), $"Missing {kind} instance.");
                session.Duplicate(); require(session.Asset.Root.Children.Count == 2, $"Duplicate {kind} failed.");
            }
        });
        test("Beta grid spans tabs content and radio groups retain saved behavior and drawing", () =>
        {
            var a = new UiAsset { Version = 2 };
            var grid = new UiNode { Kind = NodeKind.Grid, Width = 400, Height = 160, Gap = 8, Beta = new() { GridRows = ["*", "*"], GridColumns = ["100", "*"] }, Children =
                [new() { Kind = NodeKind.CheckBox, Text = "Ready", Checked = true, Width = 100, Height = 30 }, new() { Kind = NodeKind.ProgressBar, Width = 80, Height = 12, Beta = new() { Row = 1, ColumnSpan = 2 } }] };
            var tabs = new UiNode { Kind = NodeKind.TabControl, X = 0, Y = 180, Width = 400, Height = 200, Beta = new() { SelectedIndex = 1 }, Children =
                [new() { Kind = NodeKind.Panel, Name = "One", Children = [new() { Kind = NodeKind.TextBox, Text = "retained" }] }, new() { Kind = NodeKind.Content, Name = "Two", Children = [new() { Kind = NodeKind.Text, Text = "second" }] }] };
            var r1 = new UiNode { Kind = NodeKind.RadioButton, Text = "A", Checked = true, X = 450, Y = 20 };
            var r2 = new UiNode { Kind = NodeKind.RadioButton, Text = "B", X = 450, Y = 80 };
            a.Root.Children = [grid, tabs, r1, r2];
            var one = UiAssetRuntime.Build(a); var two = UiAssetRuntime.Build(UiAssetJson.Parse(UiAssetJson.Serialize(a)));
            var d1 = one.CreateDocument(new Measurer(), 640, 480); var d2 = two.CreateDocument(new Measurer(), 640, 480);
            var f1 = d1.BuildFrame(); var f2 = d2.BuildFrame();
            require(MemoryMarshal.AsBytes(f1.Commands).SequenceEqual(MemoryMarshal.AsBytes(f2.Commands)) && f1.TextBytes.SequenceEqual(f2.TextBytes), "V2 round trip changed drawing.");
            require(((TabControl)one.Nodes[tabs.Id]).SelectedIndex == 1, "Saved selected tab lost.");
            d1.Focus(one.Nodes[r2.Id]); d1.Dispatch(new(EventKind.KeyDown) { Key = 32 });
            require(((RadioButton)one.Nodes[r2.Id]).Value && !((RadioButton)one.Nodes[r1.Id]).Value, "Radio group is not exclusive.");
            require(one.Nodes[grid.Children[1].Id].Bounds.Width > one.Nodes[grid.Children[0].Id].Bounds.Width, "Grid span not applied.");
        });
        test("Beta editor palette search and component properties remain reachable", () =>
        {
            var shell = new EditorShell(new EditorSession(new UiAsset()));
            var doc = new UiDocument(shell, new Measurer(), 1100, 760);
            shell.ComponentSearch.Text = "NumericUpDown"; doc.BuildFrame();
            Element? Find(Element e) => e is Button b && b.Text == "NumericUpDown" ? e : e.Children.Select(Find).FirstOrDefault(c => c != null);
            var button = Find(shell); require(button != null && button.Visible && button.VisualBounds.Y < 760, "Searched component inaccessible.");
            var p = button!.VisualBounds; doc.Dispatch(new(EventKind.PointerDown) { Position = new(p.X + 10, p.Y + 10) }); doc.Dispatch(new(EventKind.PointerUp) { Position = new(p.X + 10, p.Y + 10) });
            require(shell.Session.Selected.Kind == NodeKind.NumericUpDown, "Palette click did not create control."); doc.BuildFrame();
        });
        test("V2 button content is authored exported and version upgrade is atomic", () =>
        {
            var session = new EditorSession(new UiAsset()); session.Add(NodeKind.Button); string buttonId = session.SelectedId;
            session.Add(NodeKind.Text); string textId = session.SelectedId;
            require(session.Asset.Version == 2, "Adding legacy-button content must upgrade the file.");
            var ui = UiAssetRuntime.Build(session.Asset);
            require(((Button)ui.Nodes[buttonId]).Content == ui.Nodes[textId], "Button slot lost content.");
            session.Undo(); require(session.Asset.Version == 1 && session.Find(buttonId).Children.Count == 0, "Content upgrade must undo atomically."); session.Redo();
            session.Asset.Version = 1; Reject(() => UiAssetJson.Validate(session.Asset)); session.Asset.Version = 2;
            var copy = UiAssetJson.Parse(UiAssetJson.Serialize(session.Asset));
            require(copy.Root.Children[0].Children.Single().Id == textId, "Button content not saved.");
        });
        test("Checked-in Beta example loads through the public runtime", () =>
        {
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../docs/examples/beta.youi.json"));
            var asset = UiAssetJson.Load(path); var ui = UiAssetRuntime.Build(asset);
            var doc = ui.CreateDocument(new Measurer(), asset.Root.Width, asset.Root.Height, asset.DarkTheme); doc.BuildFrame();
            require(asset.Version == 2 && ui.Nodes.Count == 7, "Beta fixture changed unexpectedly.");
            require(ui.Nodes.Values.OfType<ComboBox>().Single().SelectedIndex == 1 && ui.Nodes.Values.OfType<NumericUpDown>().Single().Value == 35, "Beta fixture configuration lost.");
        });
    }
    private sealed class Measurer : ITextMeasurer
    {
        public (float Width, float Height) Measure(string text, float fontSize, float maxWidth, bool bold = false) => (Math.Min(maxWidth, text.Length * fontSize * .55f), fontSize * 1.35f);
    }
}
