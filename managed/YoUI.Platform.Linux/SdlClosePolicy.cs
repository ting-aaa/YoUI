using System.Runtime.InteropServices;

namespace YoUI.Platform.Linux;

// SDL 2.0.22+ otherwise emits BOTH WINDOWEVENT_CLOSE and QUIT for the last
// window. Disable that synthesis at its source, not by discarding a later QUIT:
// explicit quit requests must remain independently vetoable.
internal sealed class SdlClosePolicy : IDisposable
{
    internal const string Hint = "SDL_QUIT_ON_LAST_WINDOW_CLOSE";
    private readonly string? previous;
    private bool ownsHint;

    internal SdlClosePolicy()
    {
        if (Sdl.SDL_GetHintBoolean(Hint, 1) == 0) return;
        previous = Marshal.PtrToStringUTF8(Sdl.SDL_GetHint(Hint));
        // DEFAULT priority can replace only another default. SDL has no public
        // priority getter: overriding an application/environment setting would
        // make it impossible to restore its original priority reliably.
        if (Sdl.SDL_SetHintWithPriority(Hint, "0", 0) == 0)
            throw new InvalidOperationException($"Vetoable close requires {Hint} to be unset or disabled (0); an explicit enabled setting cannot be overridden.");
        ownsHint = true;
    }

    public void Dispose()
    {
        if (!ownsHint) return;
        // Do not overwrite a higher-priority setting installed by the caller.
        Sdl.SDL_SetHintWithPriority(Hint, previous, 0);
        ownsHint = false;
    }
}
