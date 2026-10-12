using System.Reflection;
using System.Runtime.InteropServices;
using YoUI;
using YoUI.Platform.Linux;

internal static class ClosePolicyTests
{
    [DllImport("libSDL2-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_ResetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    internal static void Run(Action<bool, string> check, Func<Element, DesktopWindow> create)
    {
        if (Sdl.SDL_GetHint(SdlClosePolicy.Hint) != 0)
            throw new InvalidOperationException("Native failure probe requires SDL_QUIT_ON_LAST_WINDOW_CLOSE unset.");
        void Process(DesktopWindow host, Sdl.Event value) =>
            typeof(DesktopWindow).GetMethod("Process", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, [value]);
        void LastWindowClose(DesktopWindow host)
        {
            // Model SDL_SendWindowEvent's CLOSE + conditional QUIT production.
            // This is a hint/consumer contract check, not native X11 delivery.
            bool synthesizeQuit = Sdl.SDL_GetHintBoolean(SdlClosePolicy.Hint, 1) != 0;
            Process(host, new Sdl.Event { Type = 0x200, WindowEvent = 14 });
            if (synthesizeQuit && !host.IsClosed) Process(host, new Sdl.Event { Type = 0x100 });
        }
        using (var host = create(new StackPanel()))
        {
            int requests = 0;
            host.Closing = () => ++requests > 1;
            LastWindowClose(host);
            check(requests == 2 && host.IsClosed, "unconfigured SDL pair reproduces the close-veto bypass");
        }
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using (var host = create(new StackPanel()))
            {
                typeof(DesktopWindow).GetField("closePolicy", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, new SdlClosePolicy());
                int requests = 0;
                host.Closing = () => ++requests > 1;
                LastWindowClose(host);
                check(requests == 1 && !host.IsClosed, "one last-window close respects its first veto");
                LastWindowClose(host);
                check(requests == 2 && host.IsClosed, "independent second close is accepted");
            }
            check(Sdl.SDL_GetHint(SdlClosePolicy.Hint) == 0, "repeated host disposal restores absent hint");
        }
        foreach (bool windowCloseFirst in new[] { true, false })
        {
            using var policy = new SdlClosePolicy();
            using var host = create(new StackPanel());
            int requests = 0;
            host.Closing = () => ++requests > 1;
            if (windowCloseFirst) LastWindowClose(host);
            else Process(host, new Sdl.Event { Type = 0x100 });
            check(requests == 1 && !host.IsClosed, "first close/standalone quit can be vetoed");
            Process(host, new Sdl.Event { Type = 0x100 });
            check(requests == 2 && host.IsClosed, "explicit quit immediately after veto is not discarded");
        }
        foreach (uint eventType in new uint[] { 0x100, 0x200 })
        {
            using var host = create(new StackPanel());
            host.Closing = () => { host.Dispose(); return false; };
            Process(host, new Sdl.Event { Type = eventType, WindowEvent = 14 });
            check(host.IsClosed, "disposal inside close callback remains terminal despite veto");
        }
        Sdl.SDL_SetHintWithPriority(SdlClosePolicy.Hint, "1", 0);
        using (var policy = new SdlClosePolicy())
            check(Sdl.SDL_GetHintBoolean(SdlClosePolicy.Hint, 1) == 0, "default enabled hint temporarily disabled");
        check(Marshal.PtrToStringUTF8(Sdl.SDL_GetHint(SdlClosePolicy.Hint)) == "1", "default value restored");
        check(Sdl.SDL_SetHintWithPriority(SdlClosePolicy.Hint, "0", 0) != 0, "default priority restored");
        foreach (int priority in new[] { 1, 2 })
        {
            SDL_ResetHint(SdlClosePolicy.Hint);
            Sdl.SDL_SetHintWithPriority(SdlClosePolicy.Hint, "false", priority);
            using (var policy = new SdlClosePolicy()) { }
            check(Marshal.PtrToStringUTF8(Sdl.SDL_GetHint(SdlClosePolicy.Hint)) == "false", "borrowed hint value preserved");
            check(Sdl.SDL_SetHintWithPriority(SdlClosePolicy.Hint, "1", priority - 1) == 0, "borrowed hint priority preserved");
            Sdl.SDL_SetHintWithPriority(SdlClosePolicy.Hint, "1", priority);
            bool rejected = false;
            try { using var policy = new SdlClosePolicy(); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected && Sdl.SDL_GetHintBoolean(SdlClosePolicy.Hint, 1) != 0, "explicit enabled hint rejected without mutation");
            check(Sdl.SDL_SetHintWithPriority(SdlClosePolicy.Hint, "0", priority - 1) == 0, "rejected hint priority preserved");
        }
        SDL_ResetHint(SdlClosePolicy.Hint);
        using (var policy = new SdlClosePolicy())
            Sdl.SDL_SetHintWithPriority(SdlClosePolicy.Hint, "false", 2);
        check(Marshal.PtrToStringUTF8(Sdl.SDL_GetHint(SdlClosePolicy.Hint)) == "false", "cleanup preserves a later higher-priority setting");
        SDL_ResetHint(SdlClosePolicy.Hint);
    }
}
