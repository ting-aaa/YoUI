namespace YoUI.Editor.App;

/// <summary>Read-only file navigation. Opening is routed through the host's unsaved-document guard.</summary>
public sealed class ProjectBrowser : Element
{
    public TreeView Files { get; }
    public TextBox Search { get; }
    public Button OpenButton { get; }
    public string DirectoryPath { get; private set; }
    public event Action<string>? OpenRequested;
    public event Action<string>? Failed;
    private readonly Button refresh;
    private readonly Label folder;
    private List<string> paths = [];
    public ProjectBrowser(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory); ClipToBounds = true;
        folder = Add(new Label { FontSize = 11, Color = Theme.Dark.Muted });
        Search = Add(new TextBox { Placeholder = "Filter UI files…", Height = 30 }); Search.Changed += _ => Filter();
        Files = Add(new TreeView { RowHeight = 27 });
        OpenButton = Add(new Button("Open selected") { Height = 28, Enabled = false }); OpenButton.Caption.FontSize = 11;
        OpenButton.Clicked += () => { if (Files.SelectedId is { } path && paths.Contains(path)) OpenRequested?.Invoke(path); };
        Files.SelectionChanged += path => OpenButton.Enabled = paths.Contains(path);
        refresh = Add(new Button("↻") { Height = 28 }); refresh.Caption.FontSize = 12; refresh.Clicked += Refresh;
        Refresh();
    }
    public void SetDirectory(string directory) { var full = Path.GetFullPath(directory); if (full == DirectoryPath) return; DirectoryPath = full; Refresh(); }
    public void Refresh()
    {
        paths.Clear(); int visited = 0;
        void Visit(string directory, int depth)
        {
            if (depth > 6 || ++visited > 500 || paths.Count >= 500) return;
            foreach (var path in Directory.EnumerateFiles(directory, "*.youi.json").Order(StringComparer.OrdinalIgnoreCase).Take(500 - paths.Count)) paths.Add(Path.GetFullPath(path));
            foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                var info = new DirectoryInfo(child);
                if (info.Name.StartsWith('.') || info.Name is "bin" or "obj" or "target" or "artifacts" or "node_modules" || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                Visit(child, depth + 1);
            }
        }
        try { Visit(DirectoryPath, 0); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Failed?.Invoke(error.Message); }
        folder.Text = new DirectoryInfo(DirectoryPath).Name + " /  " + paths.Count + " UI files"; Filter();
    }
    private void Filter()
    {
        if (Files == null) return;
        var matches = paths.Where(p => Path.GetRelativePath(DirectoryPath, p).Contains(Search.Text, StringComparison.OrdinalIgnoreCase));
        var items = matches.GroupBy(p => Path.GetDirectoryName(Path.GetRelativePath(DirectoryPath, p)) ?? "")
            .Select(group => new TreeItem("folder:" + group.Key, string.IsNullOrEmpty(group.Key) ? "./" : group.Key, "Folder", true,
                group.Select(p => new TreeItem(p, Path.GetFileName(p), "UI configuration", false, [])).ToArray())).ToArray();
        Files.SetItems(items.Length == 0 ? [new("empty", "No UI files in this folder", "", false, [])] : items);
        OpenButton.Enabled = Files.SelectedId is { } id && matches.Contains(id);
    }
    protected override void ArrangeChildren(Rect b)
    {
        folder.Arrange(new(b.X + 8, b.Y, b.Width - 16, 18)); Search.Arrange(new(b.X + 8, b.Y + 22, b.Width - 16, 30));
        Files.Arrange(new(b.X, b.Y + 58, b.Width, Math.Max(0, b.Height - 94)));
        OpenButton.Arrange(new(b.X + 8, b.Bottom - 30, b.Width - 56, 28)); refresh.Arrange(new(b.Right - 42, b.Bottom - 30, 34, 28));
    }
}
