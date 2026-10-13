using YoUI;

static class PresentationRetryTests
{
    public static void RunGpu()
    {
        using var renderer = new NativeRenderer(64, 32);
        Console.WriteLine($"Presentation API adapter: {renderer.Adapter}; offscreen API coverage only");
        if (renderer.PresentationPending || renderer.RetryPresentation()) throw new Exception("empty offscreen renderer must be idle");
        var list = new DrawList(); list.Rectangle(new(0, 0, 64, 32), new(1, 0, 0));
        renderer.Render(list);
        var usage = renderer.ResourceUsage;
        for (int i = 0; i < 3; i++)
            if (renderer.PresentationPending || renderer.RetryPresentation()) throw new Exception("offscreen output must not retry");
        if (!usage.Equals(renderer.ResourceUsage)) throw new Exception("idle retries changed resource residency");
        renderer.Resize(32, 16);
        if (renderer.PresentationPending || renderer.RetryPresentation()) throw new Exception("resize must leave offscreen renderer idle");
        Console.WriteLine("PASS additive presentation API offscreen no-op; real surface timeout acceptance NotRun");
    }
    public static void Run(Action<string, Action> test, Action<bool, string> require)
    {
        {
            test("Shared host presentation loop retries static output without dirty layout/paint", () =>
            {
                long time = 0;
                var loop = new PresentationLoop(() => time);
                var doc = new UiDocument(new Element(), new TestMeasurer(), 200, 100);
                int rendered = 0, acquired = 0, presented = 0;
                bool Render() { doc.BuildFrame(); rendered++; acquired++; return true; } // Timeout.
                bool Retry() { acquired++; presented++; return false; } // Success.
                loop.Step(doc.NeedsRender, false, Render, Retry);
                int layouts = doc.LayoutPasses, paints = doc.PaintPasses;
                require(!doc.NeedsRender && loop.Pending && acquired == 1, "timeout must outlive consumed UI dirtiness");
                for (time = 0; time < 16; time++) loop.Step(doc.NeedsRender, false, Render, Retry);
                require(acquired == 1, "no immediate retries before deadline");
                loop.Step(doc.NeedsRender, false, Render, Retry);
                require(acquired == 2 && presented == 1 && rendered == 1 && !loop.Pending, "static frame retried and pending cleared");
                require(doc.LayoutPasses == layouts && doc.PaintPasses == paints, "retry must not rebuild document");
                require(loop.WaitMilliseconds(false) == null, "idle can block on native input");
            });
            test("Shared host presentation loop bounds repeated timeout and handles suspend/resume", () =>
            {
                long time = 0; int attempts = 0;
                var loop = new PresentationLoop(() => time);
                bool Timeout() { attempts++; time += 3; return true; }
                loop.Step(true, false, Timeout, Timeout);
                require(loop.WaitMilliseconds(false) == 16, "delay starts at completion, not before acquire");
                for (int i = 0; i < 5; i++)
                {
                    time += 15; loop.Step(false, false, Timeout, Timeout);
                    require(attempts == i + 1 && loop.WaitMilliseconds(false) == 1, "timeout cannot busy-loop");
                    time++; loop.Step(false, false, Timeout, Timeout);
                }
                int before = attempts;
                time += 1000; loop.Step(true, true, Timeout, Timeout);
                require(attempts == before && loop.Pending && loop.WaitMilliseconds(true) == null, "suspended host pauses retries and preserves pending");
                int renders = 0;
                loop.Step(true, false, () => { renders++; return false; }, Timeout);
                require(renders == 1 && attempts == before && !loop.Pending, "resume with dirty UI supersedes cached output");
            });
        }
        test("new dirty UI supersedes a pending presentation before retry deadline", () =>
        {
            long time = 0; int retries = 0, renders = 0;
            var loop = new PresentationLoop(() => time);
            loop.CompleteAttempt(true);
            loop.Step(true, false, () => { renders++; return false; }, () => { retries++; return false; });
            require(renders == 1 && retries == 0 && !loop.Pending, "new UI is not delayed or replaced by old pixels");
        });
    }
}
