using System.Runtime.CompilerServices;
using YoUI;
using YoUI.Showcase;

internal static class ShowcaseStateTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> require)
    {
        test("showcase attaches the current plain state snapshot", () =>
        {
            var defaults = new ShowcaseState();
            require(!defaults.MotionEnabled && defaults.Opacity == 0.86f, "sample defaults match the existing controls");
            var state = new ShowcaseState { MotionEnabled = true, Opacity = 0.24f };
            var scene = new Showcase(state);
            var doc = Document(scene);
            require(scene.Motion.Value && scene.Opacity.Value == 0.24f && scene.PreviewOpacity == 0.24f, "initial state reaches controls and preview");
            scene.Step(2);
            require(scene.PreviewTime == 2, "initial motion state drives the preview");
            require(scene.Status.StartsWith("Ready to explore."), "initial synchronization preserves the existing status");
        });
        test("showcase synchronizes controls and state in both directions", () =>
        {
            var scene = new Showcase();
            var doc = Document(scene);
            int motionNotifications = 0;
            scene.MotionChanged += _ => motionNotifications++;
            scene.Motion.Value = true;
            scene.Opacity.Value = 0.42f;
            require(scene.State.MotionEnabled && scene.State.Opacity == 0.42f, "control input updates state");
            require(scene.PreviewOpacity == 0.42f && scene.Status.StartsWith("Group opacity"), "control input updates the preview and status");
            scene.Step(3);
            scene.State.MotionEnabled = false;
            scene.State.Opacity = 0.68f;
            scene.Step(4);
            require(!scene.Motion.Value && scene.Opacity.Value == 0.68f && scene.PreviewOpacity == 0.68f, "state updates controls and preview");
            require(scene.PreviewTime == 3 && motionNotifications == 2, "disabled motion freezes the preview and notifies the host once per change");
        });
        test("showcase preserves newer opacity state written by a control callback", () =>
        {
            var scene = new Showcase();
            var doc = Document(scene);
            scene.Opacity.Changed += value => { if (value > 0.5f) scene.State.Opacity = 0.5f; };
            scene.State.Opacity = 0.9f;
            require(scene.State.Opacity == 0.5f && scene.Opacity.Value == 0.5f && scene.PreviewOpacity == 0.5f,
                "nested state wins in the state, slider and preview");
            require(scene.Status.StartsWith($"Group opacity {0.5f:P0}"), "status describes the latest state");
        });
        test("showcase preserves newer motion state written by a control callback", () =>
        {
            var scene = new Showcase();
            var doc = Document(scene);
            bool hostMotion = false;
            scene.MotionChanged += value => hostMotion = value;
            scene.Motion.Changed += value => { if (value) scene.State.MotionEnabled = false; };
            scene.State.MotionEnabled = true;
            require(!scene.State.MotionEnabled && !scene.Motion.Value && !hostMotion,
                "host animation follows the latest nested state");
        });
        test("showcase ignores stale snapshots from earlier state subscribers", () =>
        {
            var state = new ShowcaseState();
            state.OpacityChanged += value => { if (value > 0.5f) state.Opacity = 0.5f; };
            state.MotionChanged += value => { if (value) state.MotionEnabled = false; };
            var scene = new Showcase(state);
            var doc = Document(scene);
            bool hostMotion = false;
            scene.MotionChanged += value => hostMotion = value;
            state.Opacity = 0.9f;
            state.MotionEnabled = true;
            require(state.Opacity == 0.5f && scene.Opacity.Value == 0.5f && scene.PreviewOpacity == 0.5f,
                "earlier state subscribers cannot replay an obsolete opacity snapshot");
            require(scene.Status.StartsWith($"Group opacity {0.5f:P0}"), "status describes the latest state");
            require(!state.MotionEnabled && !scene.Motion.Value && !hostMotion,
                "earlier state subscribers cannot replay an obsolete motion snapshot");
        });
        test("showcase control corrections converge before and after reattachment", () =>
        {
            foreach (bool reattach in new[] { false, true })
            {
                var root = new Element();
                var scene = root.Add(new Showcase());
                var doc = Document(root);
                int opacityCalls = 0, motionCalls = 0;
                scene.Opacity.Changed += value =>
                {
                    require(++opacityCalls < 10, "opacity corrections converge without repeated stale input replay");
                    if (value > 0.5f) scene.State.Opacity = 0.5f;
                };
                scene.Motion.Changed += value =>
                {
                    require(++motionCalls < 10, "motion corrections converge without repeated stale input replay");
                    if (value) scene.State.MotionEnabled = false;
                };
                if (reattach) { root.Remove(scene); root.Add(scene); }
                scene.State.Opacity = 0.9f;
                scene.State.MotionEnabled = true;
                scene.State.Opacity = 0.2f;
                scene.Opacity.Value = 0.9f;
                require(scene.State.Opacity == 0.5f && scene.Opacity.Value == 0.5f && scene.PreviewOpacity == 0.5f,
                    "both callback orders preserve newer opacity state for state and control input");
                require(scene.Status.StartsWith($"Group opacity {0.5f:P0}"), "status describes the converged opacity");
                require(!scene.State.MotionEnabled && !scene.Motion.Value,
                    "both callback orders preserve newer motion state");
            }
        });
        test("showcase observes state corrections during initial and repeat attachment", () =>
        {
            var state = new ShowcaseState { Opacity = 0.9f, MotionEnabled = true };
            var scene = new Showcase(state);
            int opacityCalls = 0, motionCalls = 0;
            scene.Opacity.Changed += value =>
            {
                require(++opacityCalls < 10, "attachment opacity correction converges");
                if (value > 0.5f) state.Opacity = 0.5f;
            };
            scene.Motion.Changed += value =>
            {
                require(++motionCalls < 10, "attachment motion correction converges");
                if (value) state.MotionEnabled = false;
            };
            var root = new Element();
            var doc = Document(root);
            for (int i = 0; i < 2; i++)
            {
                root.Add(scene);
                require(state.Opacity == 0.5f && scene.Opacity.Value == 0.5f && scene.PreviewOpacity == 0.5f,
                    "initialization observes nested state changes before connecting control input");
                require(!state.MotionEnabled && !scene.Motion.Value, "initialization observes nested motion changes");
                root.Remove(scene);
                state.Opacity = 0.9f;
                state.MotionEnabled = true;
            }
        });
        test("showcase detachment during initial synchronization leaves no subscriptions", () =>
        {
            var state = new ShowcaseState { Opacity = 0.9f };
            var scene = new Showcase(state);
            var root = new Element();
            var doc = Document(root);
            scene.Opacity.Changed += _ => root.Remove(scene);
            root.Add(scene);
            require(scene.Document == null, "callback detached the scene during attachment");
            scene.Opacity.Value = 0.2f;
            scene.Motion.Value = true;
            require(state.Opacity == 0.9f && !state.MotionEnabled, "interrupted attachment cannot subscribe detached controls");
            state.Opacity = 0.7f;
            require(scene.Opacity.Value == 0.2f, "interrupted attachment releases state listeners");
        });
        test("showcase suppresses same-value state and control feedback", () =>
        {
            var scene = new Showcase();
            var doc = Document(scene);
            int motion = 0, opacity = 0, host = 0;
            scene.State.MotionChanged += _ => motion++;
            scene.State.OpacityChanged += _ => opacity++;
            scene.MotionChanged += _ => host++;
            scene.State.MotionEnabled = true;
            scene.State.Opacity = 2;
            for (int i = 0; i < 4 && doc.NeedsRender; i++) doc.BuildFrame();
            require(!doc.NeedsRender, "the initial scene settles before same-value writes");
            scene.State.MotionEnabled = true;
            scene.Motion.Value = true;
            scene.State.Opacity = 2;
            scene.Opacity.Value = 1;
            require(motion == 1 && opacity == 1 && host == 1, "effective changes each emit once without feedback loops");
            require(scene.State.Opacity == 1 && !doc.NeedsRender, "clamped same values do not invalidate an idle document");
        });
        test("showcase detaches state subscriptions and reloads exactly once", () =>
        {
            var state = new ShowcaseState();
            var scene = new Showcase(state);
            var root = new Element();
            root.Add(scene);
            var doc = Document(root);
            int host = 0;
            scene.MotionChanged += _ => host++;
            for (int i = 0; i < 3; i++)
            {
                state.MotionEnabled = true;
                state.Opacity = 0.42f;
                scene.Step(3);
                int beforeDetach = host;
                require(root.Remove(scene), "scene is removed");
                state.MotionEnabled = false;
                state.Opacity = 0.72f;
                scene.Step(5);
                require(scene.Motion.Value && scene.Opacity.Value == 0.42f && scene.PreviewOpacity == 0.42f, "detached state changes do not reach controls or preview");
                require(scene.PreviewTime == 3 && host == beforeDetach, "detached scene neither animates nor notifies the host");
                scene.Motion.Value = false;
                scene.Motion.Value = true;
                scene.Opacity.Value = 0.2f;
                require(!state.MotionEnabled && state.Opacity == 0.72f, "detached control changes do not reach state");
                root = new Element();
                doc = Document(root);
                root.Add(scene);
                require(!scene.Motion.Value && scene.Opacity.Value == 0.72f && scene.PreviewOpacity == 0.72f, "reattachment applies the current state");
                require(host == beforeDetach + 1, "reattachment sends one current motion snapshot");
                int beforeChange = host;
                state.MotionEnabled = true;
                require(host == beforeChange + 1, "reattachment does not duplicate state subscriptions");
            }
        });
        test("detached showcase is not retained by an external state object", () =>
        {
            var state = new ShowcaseState();
            var weak = DetachedScene(state);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            require(!weak.IsAlive, "external state must not retain an unloaded view through either event");
            GC.KeepAlive(state);
        });
    }

    private static UiDocument Document(Element root) => new(root, new TestMeasurer(), 1280, 820);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DetachedScene(ShowcaseState state)
    {
        var root = new Element();
        var scene = root.Add(new Showcase(state));
        var doc = Document(root);
        root.Remove(scene);
        return new WeakReference(scene);
    }
}
