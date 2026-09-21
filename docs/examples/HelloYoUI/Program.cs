using YoUI;
using YoUI.Platform.Windows;

var root = new StackPanel { Padding = 24, Gap = 16 };
root.Add(new Label("Hello, YoUI") { FontSize = 28, Bold = true });
var count = root.Add(new Label("Clicked 0 times"));
var button = root.Add(new Button("Click me") { Width = 160, Primary = true });
int clicks = 0;
button.Clicked += () => count.Text = $"Clicked {++clicks} times";

if (args.Length == 2 && args[0] == "--headless")
{
    string output = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    using var renderer = new NativeRenderer(640, 360);
    var document = new UiDocument(root, renderer, 640, 360);
    var stats = renderer.Render(document.BuildFrame());
    renderer.SavePng(output);
    Console.WriteLine($"{renderer.Adapter}\n{stats}\n{output}");
    return;
}

if (args.Length != 0)
    throw new ArgumentException("Usage: HelloYoUI [--headless output.png]");

using var window = new DesktopWindow(
    "My application", root, 640, 360, minimumWidth: 480, minimumHeight: 280);
window.Run();
