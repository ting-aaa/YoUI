namespace YoUI;

// Shared by both native hosts. UI dirtiness and unpresented output have separate lifetimes.
internal sealed class PresentationLoop(Func<long>? clock = null)
{
    internal const int RetryDelayMs = 16;
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);
    private long retryAt;
    internal bool Pending { get; private set; }
    internal void CompleteAttempt(bool pending)
    {
        Pending = pending;
        retryAt = now() + RetryDelayMs;
    }
    internal void Step(bool dirty, bool suspended, Func<bool> render, Func<bool> retry)
    {
        if (suspended) return;
        // Fresh UI always wins over replaying old pixels, even during the retry delay.
        if (dirty) CompleteAttempt(render());
        else if (Pending && now() >= retryAt) CompleteAttempt(retry());
    }
    // null means that only external input needs to wake an idle host.
    internal int? WaitMilliseconds(bool suspended)
        => Pending && !suspended ? (int)Math.Clamp(retryAt - now(), 0, RetryDelayMs) : null;
}
