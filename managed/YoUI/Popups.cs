using System.Numerics;
using System.Runtime.ExceptionServices;

namespace YoUI;

/// <summary>Document-owned overlay lifetime. Dispose closes it once, detaches its content
/// and restores the prior active focus. Popup content is not a child of its anchor.</summary>
public sealed class PopupHandle : IDisposable
{
    internal PopupHandle(UiDocument document, Element owner, Element content, Vector2 size, Element? previousFocus, Action? closed)
    { Document = document; Owner = owner; Content = content; Size = size; PreviousFocus = previousFocus; Closed = closed; }
    internal UiDocument Document { get; }
    public Element Owner { get; }
    public Element Content { get; }
    public bool IsOpen { get; internal set; } = true;
    internal Vector2 Size { get; }
    internal Rect Anchor { get; set; }
    internal Element? PreviousFocus { get; }
    internal Action? Closed { get; }
    public void Dispose() => Document.ClosePopup(this);
}

public sealed partial class UiDocument
{
    private readonly List<PopupHandle> popups = [];
    private bool pruningPopups;
    public int PopupCount => popups.Count;
    public Vector2 ViewportSize => size;
    public PopupHandle ShowPopup(Element owner, Element content, Vector2 preferredSize, Action? closed = null)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(content);
        if (updating) throw new InvalidOperationException("Cannot open a popup during layout, painting or semantic callbacks.");
        if (!float.IsFinite(preferredSize.X) || !float.IsFinite(preferredSize.Y) || preferredSize.X <= 0 || preferredSize.Y <= 0) throw new ArgumentOutOfRangeException(nameof(preferredSize));
        if (!Active(owner)) throw new InvalidOperationException("Popup owner must be active in this document.");
        if (content.Parent != null || content.Document != null || ReferenceEquals(content, owner)) throw new InvalidOperationException("Popup content must be detached.");
        CancelPointer();
        var popup = new PopupHandle(this, owner, content, preferredSize, Focused, closed);
        popups.Add(popup); Invalidate(DirtyFlags.All);
        try
        {
            if (popup.IsOpen) content.Attach(this);
            if (popup.IsOpen) { EnsureLayout(); Focus(Traverse(content).FirstOrDefault(n => n.Focusable)); }
        }
        catch
        {
            try { popup.Dispose(); } catch { }
            throw;
        }
        return popup;
    }
    internal void ClosePopup(PopupHandle popup)
    {
        if (!popup.IsOpen || !popups.Remove(popup)) return;
        popup.IsOpen = false;
        Exception? failure = null;
        void Cleanup(Action action) { try { action(); } catch (Exception error) { failure ??= error; } }
        Cleanup(() => Detached(popup.Content));
        Cleanup(() => popup.Content.Attach(null));
        Cleanup(() => Invalidate(DirtyFlags.All));
        if (Focused == null && popup.PreviousFocus is { } previous && Active(previous)) Cleanup(() => Focus(previous));
        Cleanup(() => popup.Closed?.Invoke());
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private bool PopupOwnerActive(Element owner)
    {
        if (owner.Document != this) return false;
        if (Traverse(modal ?? Root).Contains(owner)) return true;
        // Owners may be nested in an earlier popup. New content is detached on entry,
        // so this dependency graph cannot have cycles.
        return popups.Any(p => p.IsOpen && Traverse(p.Content).Contains(owner) && PopupOwnerActive(p.Owner));
    }
    private void PrunePopups()
    {
        if (pruningPopups || popups.Count == 0) return;
        pruningPopups = true;
        Exception? failure = null;
        try
        {
            foreach (var popup in popups.ToArray().Reverse())
                if (popup.IsOpen && !PopupOwnerActive(popup.Owner))
                    try { popup.Dispose(); } catch (Exception error) { failure ??= error; }
        }
        finally { pruningPopups = false; }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private void ArrangePopups()
    {
        foreach (var popup in popups.ToArray())
        {
            if (!popup.IsOpen) continue;
            var anchor = popup.Owner.VisualBounds;
            popup.Anchor = anchor;
            float width = Math.Min(size.X, popup.Size.X), height = Math.Min(size.Y, popup.Size.Y);
            float x = Math.Clamp(anchor.X, 0, Math.Max(0, size.X - width));
            float below = size.Y - anchor.Bottom, above = anchor.Y;
            float y = height <= below || below >= above ? anchor.Bottom : anchor.Y - height;
            y = Math.Clamp(y, 0, Math.Max(0, size.Y - height));
            popup.Content.Measure(new(width, height));
            popup.Content.Arrange(new(x, y, width, height));
        }
    }
    private Element? HitTestPopups(Vector2 position)
    {
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var hit = popups[i].Content.HitTest(position, new(0, 0, size.X, size.Y), Vector2.Zero);
            if (hit != null) return hit;
            if (popups[i].Content.Visible && popups[i].Content.Enabled && popups[i].Content.VisualBounds.Contains(position)) return popups[i].Content;
        }
        return null;
    }
}
