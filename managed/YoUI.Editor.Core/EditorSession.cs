using YoUI.Assets;
using System.Numerics;

namespace YoUI.Editor;

/// <summary>Transactional authoring model, independent of windows, rendering and exporter plugins.</summary>
public sealed class EditorSession
{
    private sealed record Snapshot(string Json, string Selection);
    private readonly List<Snapshot> undo = [], redo = [];
    private Snapshot? transaction;
    private string saved;
    public UiAsset Asset { get; private set; }
    public string SelectedId { get; private set; }
    public string? FilePath { get; private set; }
    public bool IsDirty => UiAssetJson.Serialize(Asset) != saved;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public bool InTransaction => transaction != null;
    public UiNode Selected => Find(SelectedId);
    public event Action<bool>? Changed; // true = model changed; false = only selection/history/save state
    public EditorSession(UiAsset? asset = null)
    {
        Asset = asset ?? Starter(); saved = UiAssetJson.Serialize(Asset); SelectedId = Asset.Root.Id;
    }
    public UiNode Find(string id) => Asset.Root.DescendantsAndSelf().First(n => n.Id == id);
    public UiNode? ParentOf(string id) => Asset.Root.DescendantsAndSelf().FirstOrDefault(n => n.Children.Any(c => c.Id == id));
    private Snapshot Capture() => new(UiAssetJson.Serialize(Asset), SelectedId);
    private void Restore(Snapshot s) { Asset = UiAssetJson.Parse(s.Json); SelectedId = Asset.Root.DescendantsAndSelf().Any(n => n.Id == s.Selection) ? s.Selection : Asset.Root.Id; }
    private static void Push(List<Snapshot> list, Snapshot s) { list.Add(s); if (list.Count > 100) list.RemoveAt(0); }
    public void Select(string id) { _ = Find(id); if (SelectedId == id) return; SelectedId = id; Changed?.Invoke(false); }
    public void Edit(Action<UiAsset> change)
    {
        var before = Capture(); var next = UiAssetJson.Parse(before.Json); change(next); UiAssetJson.Validate(next);
        if (UiAssetJson.Serialize(next) == before.Json) return;
        Asset = next; if (!Asset.Root.DescendantsAndSelf().Any(n => n.Id == SelectedId)) SelectedId = Asset.Root.Id;
        if (transaction == null) { Push(undo, before); redo.Clear(); }
        Changed?.Invoke(true);
    }
    public void UpdateSelected(Action<UiNode> change) { string id = SelectedId; Edit(a => change(a.Root.DescendantsAndSelf().First(n => n.Id == id))); }
    /// <summary>Parent content bounds are resolved by the UI runtime, in artboard coordinates, with padding removed.</summary>
    public void SetAnchor(string id, AnchorMode anchor, Rect parentContent) => SetReference(id, anchor, null, parentContent);
    public void SetPivot(string id, Vector2 pivot, Rect parentContent) => SetReference(id, null, pivot, parentContent);
    private void SetReference(string id, AnchorMode? anchor, Vector2? pivot, Rect parentContent)
    {
        var current = Find(id);
        if (id == Asset.Root.Id || ParentOf(id)?.IsStack == true) throw new InvalidDataException("The parent layout owns this node's position.");
        if (pivot.HasValue && current.Anchor == AnchorMode.Stretch) throw new InvalidDataException("Stretch uses edge insets; choose a fixed anchor to edit the pivot.");
        if ((anchor.HasValue && anchor.Value == current.Anchor) || (pivot.HasValue && pivot.Value == current.Pivot)) return;
        var bounds = current.Transform.Resolve(parentContent);
        var reference = pivot ?? current.Pivot;
        Edit(a =>
        {
            var n = a.Root.DescendantsAndSelf().First(n => n.Id == id);
            n.Anchor = anchor ?? n.Anchor; n.PivotX = reference.X; n.PivotY = reference.Y;
            if (n.Anchor == AnchorMode.Stretch)
            {
                n.X = bounds.X - parentContent.X; n.Y = bounds.Y - parentContent.Y;
                n.Width = parentContent.Right - bounds.Right; n.Height = parentContent.Bottom - bounds.Bottom;
            }
            else
            {
                var point = UiNode.AnchorPoint(n.Anchor);
                n.Width = bounds.Width; n.Height = bounds.Height;
                n.X = bounds.X - parentContent.X - parentContent.Width * point.X + bounds.Width * reference.X;
                n.Y = bounds.Y - parentContent.Y - parentContent.Height * point.Y + bounds.Height * reference.Y;
            }
        });
    }
    public void BeginTransaction() { if (transaction != null) throw new InvalidOperationException("An edit transaction is already active."); transaction = Capture(); }
    public void EndTransaction(bool cancel = false)
    {
        if (transaction is not { } before) return; transaction = null;
        if (cancel) Restore(before);
        else if (before.Json != UiAssetJson.Serialize(Asset)) { Push(undo, before); redo.Clear(); }
        Changed?.Invoke(true);
    }
    public void Undo() { EndTransaction(); if (!CanUndo) return; Push(redo, Capture()); var s = undo[^1]; undo.RemoveAt(undo.Count - 1); Restore(s); Changed?.Invoke(true); }
    public void Redo() { EndTransaction(); if (!CanRedo) return; Push(undo, Capture()); var s = redo[^1]; redo.RemoveAt(redo.Count - 1); Restore(s); Changed?.Invoke(true); }
    public void Add(NodeKind kind)
    {
        if (kind == NodeKind.Canvas) throw new InvalidOperationException("Use New for an artboard.");
        string parent = Selected.IsContainer ? SelectedId : ParentOf(SelectedId)!.Id;
        var node = new UiNode { Kind = kind, Name = kind.ToString(), Text = kind switch { NodeKind.Text => "Your text", NodeKind.Button => "Continue", NodeKind.Toggle => "Option", _ => "" }, X = 24, Y = 24, Width = kind is NodeKind.Panel or NodeKind.VerticalStack or NodeKind.HorizontalStack ? 280 : 180, Height = kind is NodeKind.Panel or NodeKind.VerticalStack or NodeKind.HorizontalStack ? 180 : 44, Fill = kind is NodeKind.Panel or NodeKind.VerticalStack or NodeKind.HorizontalStack ? "#1F2937" : "#00000000", Primary = kind == NodeKind.Button };
        Edit(a => a.Root.DescendantsAndSelf().First(n => n.Id == parent).Children.Add(node)); Select(node.Id);
    }
    public void Delete()
    {
        if (SelectedId == Asset.Root.Id) return;
        string id = SelectedId, parent = ParentOf(id)!.Id;
        Edit(a => a.Root.DescendantsAndSelf().First(n => n.Id == parent).Children.RemoveAll(n => n.Id == id)); Select(parent);
    }
    public void Duplicate()
    {
        if (SelectedId == Asset.Root.Id) return;
        string id = SelectedId, parent = ParentOf(id)!.Id, next = "";
        Edit(a =>
        {
            var p = a.Root.DescendantsAndSelf().First(n => n.Id == parent); int i = p.Children.FindIndex(n => n.Id == id);
            var copy = UiAssetJson.Parse(UiAssetJson.Serialize(new UiAsset { Root = new UiNode { Kind = NodeKind.Canvas, Width = 640, Height = 480, Children = [p.Children[i]] } })).Root.Children[0];
            foreach (var n in copy.DescendantsAndSelf()) n.Id = Guid.NewGuid().ToString("N");
            copy.Name += " copy"; if (!p.IsStack) { copy.X += 16; copy.Y += 16; } next = copy.Id; p.Children.Insert(i + 1, copy);
        }); Select(next);
    }
    public void Move(int direction)
    {
        if (SelectedId == Asset.Root.Id) return; string id = SelectedId, parent = ParentOf(id)!.Id;
        Edit(a => { var list = a.Root.DescendantsAndSelf().First(n => n.Id == parent).Children; int i = list.FindIndex(n => n.Id == id), j = Math.Clamp(i + Math.Sign(direction), 0, list.Count - 1); (list[i], list[j]) = (list[j], list[i]); });
    }
    public void Reparent(string id, string parentId)
    {
        var node = Find(id); var parent = Find(parentId);
        if (id == Asset.Root.Id || !parent.IsContainer || node.DescendantsAndSelf().Any(n => n.Id == parentId)) throw new InvalidDataException("Drop onto a container outside this subtree.");
        string old = ParentOf(id)!.Id; if (old == parentId) return;
        Edit(a => { var all = a.Root.DescendantsAndSelf().ToDictionary(n => n.Id); var n = all[id]; all[old].Children.Remove(n); all[parentId].Children.Add(n); }); Select(id);
    }
    public void New() => Replace(Starter(), null);
    public void Open(string path) { var asset = UiAssetJson.Load(path); Replace(asset, Path.GetFullPath(path)); }
    private void Replace(UiAsset asset, string? path) { string json = UiAssetJson.Serialize(asset); transaction = null; Asset = asset; FilePath = path; SelectedId = asset.Root.Id; saved = json; undo.Clear(); redo.Clear(); Changed?.Invoke(true); }
    public void Save(string? path = null)
    {
        EndTransaction(); string target = Path.GetFullPath(path ?? FilePath ?? throw new InvalidOperationException("Choose a file path."));
        string json = UiAssetJson.Serialize(Asset), temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, json); File.Move(temp, target, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
        FilePath = target; saved = json; Changed?.Invoke(false);
    }
    public static UiAsset Starter()
    {
        var a = new UiAsset { Name = "Welcome screen" };
        a.Root.Fill = "#101722"; a.Root.Children =
        [
            new() { Kind = NodeKind.Text, Name = "Eyebrow", Text = "YOUI / START SOMETHING NEW", X = 48, Y = 42, Width = 540, Height = 24, FontSize = 12, Foreground = "#78DEBF", Bold = true },
            new() { Kind = NodeKind.Text, Name = "Heading", Text = "Make room for your ideas.", X = 48, Y = 84, Width = 550, Height = 56, FontSize = 32, Bold = true, Foreground = "#EFF5FC" },
            new() { Kind = NodeKind.Text, Name = "Description", Text = "A native interface, built with the same UI library as this editor.", X = 48, Y = 146, Width = 510, Height = 52, FontSize = 16, Foreground = "#94A3B8" },
            new() { Kind = NodeKind.Panel, Name = "Form card", X = 48, Y = 218, Width = 544, Height = 208, Fill = "#1B2637", Radius = 16, Children =
                [
                    new() { Kind = NodeKind.TextBox, Name = "Project name", Text = "My first interface", X = 24, Y = 24, Width = 496, Height = 44 },
                    new() { Kind = NodeKind.Toggle, Name = "Sync", Text = "Keep in sync", X = 24, Y = 88, Width = 218, Height = 40, Checked = true },
                    new() { Kind = NodeKind.Button, Name = "Create button", Text = "Create project  →", X = 280, Y = 88, Width = 240, Height = 40, Primary = true, Command = "project.create" },
                    new() { Kind = NodeKind.Slider, Name = "Intensity", X = 24, Y = 150, Width = 496, Height = 24, Value = .65f }
                ] }
        ]; return a;
    }
}
