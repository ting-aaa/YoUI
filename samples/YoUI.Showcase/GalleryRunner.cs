using System.Numerics;
using System.Text.Json;
using YoUI.Platform.Windows;

namespace YoUI.Showcase;

internal static class GalleryRunner
{
    public static void Run(string[] args, string artifactDir)
    {
        if (args.Any(arg => arg is not ("--gallery" or "--headless" or "--smoke")) || (args.Contains("--headless") && args.Contains("--smoke")))
            throw new ArgumentException("Use --gallery [--headless | --smoke].");
        var gallery = new ComponentGallery();
        if (args.Contains("--headless"))
        {
            using var renderer = new NativeRenderer(1000, 760);
            using var assets = new GalleryRenderingResources(renderer);
            gallery.Rendering.SetResources(assets);
            try
            {
                var document = new UiDocument(gallery, renderer, 1000, 760);
                var checks = new List<string>();
                void Require(bool condition, string message) { if (!condition) throw new Exception(message); checks.Add(message); }
                void Key(int key) => document.Dispatch(new(EventKind.KeyDown) { Key = key });
                void Click(Element element)
                {
                    document.BuildFrame(); var b = element.VisualBounds; var point = new Vector2(b.X + b.Width / 2, b.Y + b.Height / 2);
                    document.Dispatch(new(EventKind.PointerDown) { Position = point }); document.Dispatch(new(EventKind.PointerUp) { Position = point });
                }
                void Save(string name) { renderer.Render(document.BuildFrame()); renderer.SavePng(Path.Combine(artifactDir, name)); }
                Save("gallery-controls-dark.png");
                document.Focus(gallery.Rating); Key(35); Require(gallery.State.Value == 5, "Dispatched rating keyboard input updates the model");
                Click(gallery.Navigation[1]); document.Focus(gallery.Search);
                document.Dispatch(new(EventKind.TextInput) { Text = "Orbit" }); document.Focus(gallery.Items); Key(40);
                Require(gallery.Items.ItemCount == 60 && gallery.SelectedRecord == 1, "Dispatched text and list input selects a filtered record");
                Save("gallery-collection-dark.png");
                Click(gallery.Navigation[2]); Save("gallery-layout-dark.png");
                Click(gallery.Navigation[3]); Save("gallery-rendering-dark.png");
                var renderingViewport = (ScrollView)gallery.Rendering.Parent!.Parent!;
                renderingViewport.ScrollTo(float.MaxValue); Save("gallery-rendering-detail-dark.png");
                renderingViewport.ScrollTo(0);
                Click(gallery.RenderingClip); document.Focus(gallery.RenderingOpacity); Key(37);
                Require(!gallery.Rendering.Clipped && Math.Abs(gallery.Rendering.GroupOpacity - .60f) < .001f,
                    "Dispatched clip toggle and opacity key update the rendering page");
                renderingViewport.ScrollTo(float.MaxValue); Save("gallery-rendering-unclipped.png");
                renderingViewport.ScrollTo(0);
                gallery.SelectPage(0); Require(gallery.Rating.Value == 5, "Navigation preserves model state");
                Click(gallery.ResetButton); Require(gallery.ModalOpen, "Dispatched pointer opens modal");
                Save("gallery-modal-dark.png"); Key(27);
                Require(!gallery.ModalOpen && document.Focused == gallery.ResetButton && gallery.State.Value == 5, "Escape cancels and restores focus without reset");
                Click(gallery.ResetButton); Click(gallery.ConfirmButton); Require(gallery.State.Value == 3 && gallery.Rendering.Clipped && gallery.Rendering.GroupOpacity == .65f, "Confirm resets model and detached rendering page");
                Click(gallery.ThemeButton); Save("gallery-controls-light.png");
                gallery.SelectPage(3); Save("gallery-rendering-light.png");
                renderingViewport.ScrollTo(float.MaxValue); Save("gallery-rendering-detail-light.png");
                renderingViewport.ScrollTo(0);
                document.Resize(640, 600); renderer.Resize(640, 600);
                for (int page = 0; page < gallery.Navigation.Length; page++) { gallery.SelectPage(page); Save($"gallery-compact-{page}.png"); }
                renderingViewport.ScrollTo(float.MaxValue); Save("gallery-rendering-compact-detail.png");
                gallery.SelectPage(0); document.BuildFrame(); document.Focus(gallery.Rating); Save("gallery-compact-focus.png");
                Require(gallery.Rating.VisualBounds.Bottom <= 550, "Compact keyboard focus reveals the rating control");
                File.WriteAllText(Path.Combine(artifactDir, "gallery-smoke.json"), JsonSerializer.Serialize(new
                {
                    adapter = renderer.Adapter, input = "UiDocument.Dispatch (headless, not OS input)", checks,
                    sizes = new[] { "1000x760", "640x600" }, nativeWindowTested = false
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"PASS gallery headless rendering and dispatched-input smoke: {renderer.Adapter}\n{string.Join("\n", checks)}");
            }
            finally { gallery.Rendering.SetResources(null); gallery.Clear(); }
            return;
        }
        using var window = new DesktopWindow("YoUI — Component gallery", gallery);
        using var windowAssets = new GalleryRenderingResources(window.Renderer);
        gallery.Rendering.SetResources(windowAssets);
        try
        {
            if (!args.Contains("--smoke")) { window.Run(); return; }
            window.RenderNow();
            void NativeClick(Element element)
            {
                var b = element.VisualBounds; int x = (int)((b.X + b.Width / 2) * window.Scale), y = (int)((b.Y + b.Height / 2) * window.Scale);
                nint point = (nint)((y << 16) | (x & 0xffff));
                SmokeNative.PostMessage(window.Handle, 0x0200, 0, point); SmokeNative.PostMessage(window.Handle, 0x0201, 1, point); SmokeNative.PostMessage(window.Handle, 0x0202, 0, point);
                window.PumpEvents(); window.RenderNow();
            }
            for (int i = 0; i < gallery.Navigation.Length * 2; i++) NativeClick(gallery.Navigation[i % gallery.Navigation.Length]);
            if (gallery.Page != gallery.Navigation.Length - 1) throw new Exception("Native gallery navigation failed.");
            NativeClick(gallery.ResetButton); if (!gallery.ModalOpen) throw new Exception("Native gallery modal open failed.");
            NativeClick(gallery.CancelButton); if (gallery.ModalOpen) throw new Exception("Native gallery modal cancel failed.");
            window.Renderer.SavePng(Path.Combine(artifactDir, "gallery-window-smoke.png"));
            Console.WriteLine("PASS Windows gallery posted-message navigation and modal smoke.");
        }
        finally { gallery.Rendering.SetResources(null); gallery.Clear(); }
    }
}
