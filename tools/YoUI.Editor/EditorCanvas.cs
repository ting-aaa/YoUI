using System.Numerics;
using YoUI.Assets;

namespace YoUI.Editor.App;

public sealed class EditorCanvas : Element
{
    private readonly EditorSession session;
    private UiInstance? instance;
    private UiDocument? runtime;
    private bool fit = true, dragging, resizing, panning;
    private float zoom = 1;
    private Vector2 pan, origin, down, startPan;
    private Rect start;
    public bool Snap { get; set; } = true;
    public bool Preview { get; private set; }
    public float Zoom => zoom;
    public Rect ArtboardBounds { get { EnsureRuntime(); Viewport(); return new(origin.X, origin.Y, session.Asset.Root.Width * zoom, session.Asset.Root.Height * zoom); } }
    public UiInstance Instance { get { EnsureRuntime(); return instance!; } }
    public UiDocument Runtime { get { EnsureRuntime(); return runtime!; } }
    public event Action<string>? Status;
    public EditorCanvas(EditorSession session)
    {
        this.session = session; Focusable = HitTestVisible = ClipToBounds = true;
        session.Changed += model => { if (model) { runtime?.CancelPointer(); instance = null; runtime = null; } Invalidate(); };
    }
    private void EnsureRuntime()
    {
        if (instance != null || Document == null) return;
        instance = UiAssetRuntime.Build(session.Asset, name => Status?.Invoke("Preview command: " + name));
        runtime = instance.CreateDocument(Document.TextMeasurer, session.Asset.Root.Width, session.Asset.Root.Height, session.Asset.DarkTheme);
        runtime.Clipboard = Document.Clipboard; runtime.EnsureLayout();
    }
    private void Viewport()
    {
        if (fit) zoom = Math.Clamp(Math.Min((Bounds.Width - 80) / session.Asset.Root.Width, (Bounds.Height - 96) / session.Asset.Root.Height), .1f, 1);
        origin = new(Bounds.X + (Bounds.Width - session.Asset.Root.Width * zoom) / 2 + pan.X, Bounds.Y + (Bounds.Height - session.Asset.Root.Height * zoom) / 2 + pan.Y);
    }
    public void Fit() { fit = true; pan = Vector2.Zero; Invalidate(); }
    public void SetZoom(float value) { fit = false; zoom = Math.Clamp(value, .1f, 3); Invalidate(); }
    public void SetPreview(bool value)
    {
        CancelDrag(); runtime?.CancelPointer(); Preview = value; instance = null; runtime = null; Invalidate();
        Status?.Invoke(value ? "Preview active · controls are interactive · Esc to return" : "Edit mode · drag to move · bottom-right handle to resize");
    }
    public Rect NodeBounds(string id)
    {
        EnsureRuntime(); Viewport(); var b = instance!.Nodes[id].Bounds;
        return new(origin.X + b.X * zoom, origin.Y + b.Y * zoom, b.Width * zoom, b.Height * zoom);
    }
    public Rect ParentContentBounds(string id)
    {
        EnsureRuntime(); runtime!.EnsureLayout();
        var parent = session.ParentOf(id) ?? throw new InvalidDataException("Artboard has no parent.");
        return instance!.Nodes[parent.Id].Bounds.Inset(parent.Padding);
    }
    private UiNode? Hit(UiNode n, Vector2 point, Rect clip)
    {
        if (!n.Visible || !clip.Contains(point)) return null;
        var b = instance!.Nodes[n.Id].Bounds; if (n.Clip) clip = clip.Intersect(b);
        if (!clip.Contains(point)) return null;
        for (int i = n.Children.Count - 1; i >= 0; i--) { var hit = Hit(n.Children[i], point, clip); if (hit != null) return hit; }
        return b.Contains(point) ? n : null;
    }
    public bool CanManipulate => session.SelectedId != session.Asset.Root.Id && session.ParentOf(session.SelectedId)?.IsStack != true && session.Selected.Anchor != AnchorMode.Stretch;
    public void Forward(UiEvent e)
    {
        EnsureRuntime(); Viewport();
        if (e.Kind is EventKind.Blur or EventKind.PointerCancel) { runtime!.CancelPointer(); if (e.Kind == EventKind.Blur) runtime.Focus(null); }
        else runtime!.Dispatch(new(e.Kind) { Position = (e.Position - origin) / zoom, Delta = e.Delta, Text = e.Text, Key = e.Key, Shift = e.Shift, Control = e.Control });
        Invalidate(); e.Handled = true;
    }
    public void CancelDrag() { if (dragging) { dragging = false; session.EndTransaction(true); } panning = false; }
    protected override void OnEvent(UiEvent e)
    {
        EnsureRuntime(); Viewport();
        if (Preview) { if (e.Kind == EventKind.PointerDown) e.CapturePointer = true; Forward(e); return; }
        if (e.Kind == EventKind.Scroll) { SetZoom(zoom * (e.Delta > 0 ? 1.1f : 1 / 1.1f)); e.Handled = true; return; }
        if (e.Kind == EventKind.PointerDown)
        {
            down = e.Position; startPan = pan;
            var selected = NodeBounds(session.SelectedId); var handle = new Rect(selected.Right - 7, selected.Bottom - 7, 14, 14);
            resizing = CanManipulate && handle.Contains(e.Position);
            if (!resizing)
            {
                var hit = Hit(session.Asset.Root, (e.Position - origin) / zoom, new(0, 0, session.Asset.Root.Width, session.Asset.Root.Height));
                if (hit == null) { panning = true; e.CapturePointer = true; return; }
                session.Select(hit.Id);
            }
            if (CanManipulate)
            {
                var n = session.Selected; start = new(n.X, n.Y, n.Width, n.Height);
                dragging = true; session.BeginTransaction(); e.CapturePointer = true;
            }
            else Status?.Invoke(session.ParentOf(session.SelectedId)?.IsStack == true ? "Position is controlled by the parent stack. Edit size, grow and order in the inspector." : "Use the inspector for artboard size or stretch insets.");
        }
        if (e.Kind == EventKind.PointerMove)
        {
            if (panning) { pan = startPan + e.Position - down; Invalidate(); }
            if (dragging)
            {
                var d = (e.Position - down) / zoom; if (Snap) d = new(MathF.Round(d.X / 8) * 8, MathF.Round(d.Y / 8) * 8);
                // Bound updates before committing so invalid pointer coordinates cannot terminate the native host.
                session.UpdateSelected(n =>
                {
                    if (resizing)
                    {
                        n.Width = Math.Clamp(start.Width + d.X, 8, 4096); n.Height = Math.Clamp(start.Height + d.Y, 8, 4096);
                        n.X = Math.Clamp(start.X + (n.Width - start.Width) * n.Pivot.X, -32768, 32768);
                        n.Y = Math.Clamp(start.Y + (n.Height - start.Height) * n.Pivot.Y, -32768, 32768);
                    }
                    else { n.X = Math.Clamp(start.X + d.X, -32768, 32768); n.Y = Math.Clamp(start.Y + d.Y, -32768, 32768); }
                });
            }
        }
        if (e.Kind == EventKind.PointerUp) { if (dragging) { dragging = false; session.EndTransaction(); } panning = false; }
        if (e.Kind is EventKind.PointerCancel or EventKind.Blur) CancelDrag();
    }
    private static void Outline(DrawList list, Rect r, Color color)
    {
        list.Rectangle(new(r.X, r.Y, r.Width, 1), color); list.Rectangle(new(r.X, r.Bottom - 1, r.Width, 1), color);
        list.Rectangle(new(r.X, r.Y, 1, r.Height), color); list.Rectangle(new(r.Right - 1, r.Y, 1, r.Height), color);
    }
    protected override void Paint(DrawList list, Rect b, float opacity, Theme theme)
    {
        EnsureRuntime(); Viewport(); list.Rectangle(b, Color.Hex(0x0C1017));
        for (float y = b.Y + 16; y < b.Bottom; y += 24) for (float x = b.X + 16; x < b.Right; x += 24) list.Rectangle(new(x, y, 1, 1), Color.Hex(0x263040));
        var board = ArtboardBounds;
        list.Rectangle(new(board.X - 8, board.Y - 8, board.Width + 16, board.Height + 16), Color.Hex(0x05080E, .45f), 6);
        list.PushClip(board); list.Append(runtime!.BuildFrame(), origin, zoom); list.PopClip();
        list.Text(new(board.X, board.Y - 25, Math.Max(1, board.Width), 20), $"{session.Asset.Name}   /   {session.Asset.Root.Width:0} × {session.Asset.Root.Height:0}", theme.Muted, 11);
        Outline(list, board, Color.Hex(0x374357));
        if (!Preview && instance!.Nodes.TryGetValue(session.SelectedId, out var node) && node.Visible)
        {
            var selection = NodeBounds(session.SelectedId); Outline(list, selection, theme.Accent);
            if (CanManipulate) { list.Rectangle(new(selection.Right - 5, selection.Bottom - 5, 10, 10), theme.Accent, 2); }
            if (session.SelectedId != session.Asset.Root.Id) list.Text(new(selection.X, selection.Y - 20, Math.Max(200, selection.Width), 18), session.Selected.Name, theme.Accent, 11);
        }
        list.Rectangle(new(b.X + 16, b.Bottom - 35, 192, 23), theme.Surface, 5);
        list.Text(new(b.X + 25, b.Bottom - 31, 180, 18), $"{zoom:P0}   ·   {(Preview ? "LIVE PREVIEW" : Snap ? "SNAP 8 PX" : "FREE MOVE")}", theme.Muted, 10);
    }
}
