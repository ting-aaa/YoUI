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
            gallery.SelectPage(0); Require(gallery.Rating.Value == 5, "Navigation preserves model state");
            Click(gallery.ResetButton); Require(gallery.ModalOpen, "Dispatched pointer opens modal");
            Save("gallery-modal-dark.png"); Key(27);
            Require(!gallery.ModalOpen && document.Focused == gallery.ResetButton && gallery.State.Value == 5, "Escape cancels and restores focus without reset");
            Click(gallery.ResetButton); Click(gallery.ConfirmButton); Require(gallery.State.Value == 3, "Confirm resets the model");
            Click(gallery.ThemeButton); Save("gallery-controls-light.png");
            document.Resize(640, 600); renderer.Resize(640, 600);
            for (int page = 0; page < 3; page++) { gallery.SelectPage(page); Save($"gallery-compact-{page}.png"); }
            gallery.SelectPage(0); document.BuildFrame(); document.Focus(gallery.Rating); Save("gallery-compact-focus.png");
            Require(gallery.Rating.VisualBounds.Bottom <= 550, "Compact keyboard focus reveals the rating control");
            File.WriteAllText(Path.Combine(artifactDir, "gallery-smoke.json"), JsonSerializer.Serialize(new
            {
                adapter = renderer.Adapter, input = "UiDocument.Dispatch (headless, not OS input)", checks,
                sizes = new[] { "1000x760", "640x600" }, nativeWindowTested = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            gallery.Clear();
            Console.WriteLine($"PASS gallery headless rendering and dispatched-input smoke: {renderer.Adapter}\n{string.Join("\n", checks)}");
            return;
        }
        using var window = new DesktopWindow("YoUI — Component gallery", gallery);
        if (!args.Contains("--smoke")) { window.Run(); return; }
        window.RenderNow();
        void NativeClick(Element element)
        {
            var b = element.VisualBounds; int x = (int)((b.X + b.Width / 2) * window.Scale), y = (int)((b.Y + b.Height / 2) * window.Scale);
            nint point = (nint)((y << 16) | (x & 0xffff));
            SmokeNative.PostMessage(window.Handle, 0x0200, 0, point); SmokeNative.PostMessage(window.Handle, 0x0201, 1, point); SmokeNative.PostMessage(window.Handle, 0x0202, 0, point);
            window.PumpEvents(); window.RenderNow();
        }
        for (int i = 0; i < 6; i++) NativeClick(gallery.Navigation[i % 3]);
        if (gallery.Page != 2) throw new Exception("Native gallery navigation failed.");
        NativeClick(gallery.ResetButton); if (!gallery.ModalOpen) throw new Exception("Native gallery modal open failed.");
        NativeClick(gallery.CancelButton); if (gallery.ModalOpen) throw new Exception("Native gallery modal cancel failed.");
        window.Renderer.SavePng(Path.Combine(artifactDir, "gallery-window-smoke.png"));
        Console.WriteLine("PASS Windows gallery posted-message navigation and modal smoke.");
    }
}
