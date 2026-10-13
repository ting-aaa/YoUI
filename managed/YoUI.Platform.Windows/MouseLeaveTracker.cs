using System.ComponentModel;
using System.Runtime.InteropServices;

namespace YoUI.Platform.Windows;

/// <summary>TrackMouseEvent is one-shot. Outside capture moves must not repeatedly arm it.</summary>
internal sealed class MouseLeaveTracker(Action<nint>? register = null)
{
    private readonly Action<nint> register = register ?? Register;
    private bool tracking;

    internal void Move(nint hwnd, bool insideClient)
    {
        if (tracking || !insideClient) return;
        register(hwnd); // Failure must not silently leave stale hover or claim tracking succeeded.
        tracking = true;
    }

    internal bool Leave(bool hasCapture)
    {
        tracking = false;
        // Captured drags already receive actual coordinates; never inject (-1,-1) into a drag.
        return !hasCapture;
    }

    private static void Register(nint hwnd)
    {
        var request = new Tracking { Size = (uint)Marshal.SizeOf<Tracking>(), Flags = 2 /* TME_LEAVE */, Window = hwnd };
        if (!TrackMouseEvent(ref request))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "TrackMouseEvent(TME_LEAVE) failed.");
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Tracking { internal uint Size, Flags; internal nint Window; internal uint HoverTime; }
    [DllImport("user32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(ref Tracking tracking);
}
