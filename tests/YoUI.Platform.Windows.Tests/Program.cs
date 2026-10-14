using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using YoUI;
using YoUI.Platform.Windows;

if (args.Length == 1 && args[0] == "--native-mouse-leave") { NativeMouseLeaveTests.Run(); return; }
if (args.Length == 1 && args[0] == "--native-lifecycle") { NativeLifecycleTests.Run(); return; }
if (args.Length != 0) throw new ArgumentException("Usage: YoUI.Platform.Windows.Tests [--native-mouse-leave|--native-lifecycle]");

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
Check(Marshal.SizeOf<MouseLeaveTracker.Tracking>() == (IntPtr.Size == 8 ? 24 : 16), "TRACKMOUSEEVENT ABI size");
Check(Marshal.OffsetOf<MouseLeaveTracker.Tracking>("Window").ToInt32() == 8, "TRACKMOUSEEVENT HWND offset");
int registrations = 0;
var tracker = new MouseLeaveTracker(hwnd => { Check(hwnd == 42, "registration window"); registrations++; });
tracker.Move(42, false);
Check(registrations == 0, "outside captured move does not arm tracking");
for (int cycle = 0; cycle < 3; cycle++)
{
    tracker.Move(42, true); tracker.Move(42, true);
    Check(registrations == cycle + 1, "one registration per entry");
    Check(tracker.Leave(false), "uncaptured leave requests hover clear");
}
tracker.Move(42, true);
Check(!tracker.Leave(true), "captured leave does not inject a synthetic move");
tracker.Move(42, false); tracker.Move(42, false);
Check(registrations == 4, "drag outside does not cause immediate-leave registration loop");
tracker.Move(42, true);
Check(registrations == 5, "captured re-entry re-arms tracking");
int attempts = 0;
var failure = new MouseLeaveTracker(_ => { if (++attempts == 1) throw new Win32Exception(5, "injected TrackMouseEvent failure"); });
bool failed = false;
try { failure.Move(42, true); } catch (Win32Exception e) { failed = e.NativeErrorCode == 5; }
Check(failed, "registration failure propagates explicitly");
failure.Move(42, true);
Check(attempts == 2, "failed registration never claims an armed state");

var root = new Element();
var button = root.Add(new ProbeButton { Transform = RectTransform.Fixed(0, 0, 80, 60) });
var document = new UiDocument(root, new Measurer(), 200, 100);
int clicks = 0; button.Clicked += () => clicks++;
for (int cycle = 0; cycle < 3; cycle++)
{
    document.Dispatch(new(EventKind.PointerMove) { Position = new(5, 20) });
    Check(button.IsHovered, "edge button re-entry restores hover");
    document.BuildFrame();
    if (tracker.Leave(false)) document.Dispatch(new(EventKind.PointerMove) { Position = new(-1, -1) });
    Check(!button.IsHovered && document.NeedsRender, "leave clears hover and requests repaint");
}
document.Dispatch(new(EventKind.PointerDown) { Position = new(5, 20) });
int moves = button.Moves;
if (tracker.Leave(true)) document.Dispatch(new(EventKind.PointerMove) { Position = new(-1, -1) });
Check(button.Moves == moves, "leave does not route fake coordinates to captured button");
document.Dispatch(new(EventKind.PointerMove) { Position = new(-20, 20) });
Check(button.Moves == moves + 1 && button.LastMove == new Vector2(-20, 20), "captured move receives real outside coordinates");
document.Dispatch(new(EventKind.PointerUp) { Position = new(-20, 20) });
Check(button.Ups == 1 && clicks == 0, "outside release reaches capture without activation");
document.Dispatch(new(EventKind.PointerDown) { Position = new(5, 20) });
document.Dispatch(new(EventKind.PointerMove) { Position = new(-20, 20) });
document.Dispatch(new(EventKind.PointerMove) { Position = new(5, 20) });
document.Dispatch(new(EventKind.PointerUp) { Position = new(5, 20) });
Check(clicks == 1, "drag out and back preserves release-inside activation");
Console.WriteLine($"PASS Windows mouse-leave logic: {checks} checks; native Win32 delivery NotRun.");

internal sealed class Measurer : ITextMeasurer
{
    public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) => (Math.Min(width, text.Length * size / 2), size);
}
internal sealed class ProbeButton : Button
{
    internal int Moves, Ups, Cancels, SentinelMoves;
    internal Vector2 LastMove;
    internal ProbeButton() : base("Edge") { }
    protected override void OnEvent(UiEvent e)
    {
        if (e.Kind == EventKind.PointerMove) { Moves++; LastMove = e.Position; if (e.Position == new Vector2(-1, -1)) SentinelMoves++; }
        if (e.Kind == EventKind.PointerUp) Ups++;
        if (e.Kind == EventKind.PointerCancel) Cancels++;
        base.OnEvent(e);
    }
}
