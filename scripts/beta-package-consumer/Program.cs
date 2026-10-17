using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using YoUI;
using YoUI.Assets;
#if FULL_PACKAGES
using YoUI.Editor;
using YoUI.Platform.Windows;
#endif

nint native = 0;
try
{
    if (args.Length != 5) throw new ArgumentException("Expected output directory, version, configuration, native SHA256, and window flag.");
    string output = Path.GetFullPath(args[0]), expectedVersion = args[1], configuration = args[2], expectedHash = args[3];
    bool verifyWindow = bool.Parse(args[4]);
    Directory.CreateDirectory(output);
    var checks = new List<string>();
    void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
    string nativePath = Path.Combine(AppContext.BaseDirectory, "youi_render.dll");
    string nativeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nativePath)));
    Require(nativeHash == expectedHash, "Consumer native SHA256 matches source and package payload");
    native = NativeLibrary.Load(nativePath);
    NativeLibrary.SetDllImportResolver(typeof(NativeRenderer).Assembly, (name, _, _) => name == "youi_render" ? native : nint.Zero);
    uint abi = Marshal.GetDelegateForFunctionPointer<NativeQuery>(NativeLibrary.GetExport(native, "youi_abi_version"))();
    uint commandSize = Marshal.GetDelegateForFunctionPointer<NativeQuery>(NativeLibrary.GetExport(native, "youi_command_size"))();
    Require(abi == 0x00010000 && commandSize == 80 && Marshal.SizeOf<DrawCommand>() == 80, "Native ABI v1 and 80-byte command layout agree");
    Assembly[] assemblies = [typeof(NativeRenderer).Assembly
#if FULL_PACKAGES
        , typeof(DesktopWindow).Assembly, typeof(EditorSession).Assembly
#endif
    ];
    var versions = assemblies.ToDictionary(a => a.GetName().Name!, a => a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
    Require(versions.Values.All(v => v.Split('+')[0] == expectedVersion), "Every package assembly has the requested prerelease version");

    // Definitions exercise the versioned public runtime; no editor is needed to instantiate them.
    var heading = Node(NodeKind.Text, "YoUI Beta / package consumer", 24, 20, 900, 40); heading.FontSize = 26; heading.Bold = true;
    var note = Node(NodeKind.Text, expectedVersion + " · " + configuration + " · independent win-x64 PackageReference", 24, 65, 900, 28); note.FontSize = 14;
    var check = Node(NodeKind.CheckBox, "Enable notifications");
    var combo = Node(NodeKind.ComboBox); combo.Beta = new() { Items = ["Design", "Development", "Review"], SelectedIndex = 0 };
    var number = Node(NodeKind.NumericUpDown); number.Beta = new() { Number = 35, Maximum = 100, Step = 5 };
    var progress = Node(NodeKind.ProgressBar); progress.Value = .35f; progress.Height = 14;
    var input = Node(NodeKind.TextBox, "Aurora");
    var button = Node(NodeKind.Button, "Apply settings"); button.Primary = true; button.Command = "apply";
    var grid = Node(NodeKind.Grid, "", 24, 112, 540, 376); grid.Padding = 16; grid.Gap = 10; grid.Fill = "#1C2A3D";
    grid.Beta = new() { GridRows = ["44", "44", "44", "44", "24", "44"], GridColumns = ["126", "*"] };
    UiNode[] values = [input, combo, check, number, progress, button];
    string[] labels = ["Name", "Workspace", "Preferences", "Capacity", "Progress", "Action"];
    for (int row = 0; row < values.Length; row++)
    {
        var label = Node(NodeKind.Text, labels[row]); label.Beta = new() { Row = row }; label.FontSize = 14;
        values[row].Beta ??= new(); values[row].Beta!.Row = row; values[row].Beta!.Column = 1;
        grid.Children.Add(label); grid.Children.Add(values[row]);
    }
    var table = Node(NodeKind.DataGrid, "", 588, 112, 348, 236);
    table.Beta = new() { TableColumns = ["Project", "Status"], TableRows = [["Aurora", "Ready"], ["Boreal", "Running"], ["Cinder", "Ready"]] };
    var tabs = Node(NodeKind.TabControl, "", 588, 372, 348, 208);
    var content = Node(NodeKind.Content, "", 0, 0, 348, 164); content.Name = "Details"; content.Padding = 14; content.Fill = "#22344B";
    content.Children = [Node(NodeKind.Text, "Version 2 definitions\nRuntime without the editor", 0, 0, 320, 100)];
    var wrap = Node(NodeKind.Wrap, "", 0, 0, 348, 164); wrap.Name = "Layout"; wrap.Gap = 8; wrap.Padding = 10;
    wrap.Children = [Node(NodeKind.Button, "Auto"), Node(NodeKind.Button, "Grid"), Node(NodeKind.Button, "Wrap")];
    tabs.Children = [content, wrap];
    var radioA = Node(NodeKind.RadioButton, "Personal", 24, 510, 160, 38); radioA.Checked = true;
    var radioB = Node(NodeKind.RadioButton, "Team", 194, 510, 160, 38);
    var separator = Node(NodeKind.Separator, "", 24, 570, 540, 1);
    var asset = new UiAsset { Version = 2, Name = "Beta package reference", Root = Node(NodeKind.Canvas, "", 0, 0, 960, 660) };
    asset.Root.Fill = "#111827"; asset.Root.Clip = true;
    asset.Root.Children = [heading, note, grid, table, tabs, radioA, radioB, separator];
    string serialized = UiAssetJson.Serialize(asset);
    File.WriteAllText(Path.Combine(output, "screen.youi.json"), serialized);
    asset = UiAssetJson.Parse(serialized);
    Require(asset.Version == 2 && asset.Root.DescendantsAndSelf().Count() > 20, "V2 component definitions round-trip through the packaged JSON contract");
    int applied = 0;
    var instance = UiAssetRuntime.Build(asset, name => { if (name == "apply") applied++; });
    using var renderer = new NativeRenderer(960, 660);
    var capabilities = renderer.Capabilities;
    Require(capabilities.DeviceKind is RendererDeviceKind.DiscreteGpu or RendererDeviceKind.IntegratedGpu, "Selected adapter reports a physical integrated or discrete GPU");
    var doc = instance.CreateDocument(renderer, 960, 660);
    T Find<T>(UiNode node) where T : Element => (T)instance.Nodes[node.Id];
    var inputControl = Find<TextBox>(input); var numberControl = Find<NumericUpDown>(number); var progressControl = Find<ProgressBar>(progress);
    var nameModel = new ObservableValue<string>("Aurora"); var amountModel = new ObservableValue<float>(35); var checkModel = new ObservableValue<bool>(false);
    using var bindings = new BindingScope();
    bindings.BindText(inputControl, nameModel, value => string.IsNullOrWhiteSpace(value) ? "A name is required" : null);
    bindings.BindNumber(numberControl, amountModel); bindings.Bind(amountModel, value => progressControl.Value = value / 100);
    bindings.BindToggle(Find<CheckBox>(check), checkModel);
    var apply = Find<Button>(button); int commandRuns = 0;
    var command = new UiCommand(_ => commandRuns++, _ => nameModel.Value.Length > 0); bindings.BindCommand(apply, command);
    void Key(Element target, int key) { doc.Focus(target); doc.Dispatch(new(EventKind.KeyDown) { Key = key }); }
    void Click(Element target)
    {
        doc.BuildFrame(); var b = target.VisualBounds; var point = new Vector2(b.X + b.Width / 2, b.Y + b.Height / 2);
        doc.Dispatch(new(EventKind.PointerDown) { Position = point }); doc.Dispatch(new(EventKind.PointerUp) { Position = point });
    }
    doc.BuildFrame();
    Require(inputControl.Bounds.Width > 200 && inputControl.Bounds.X > grid.X + 126, "Packaged Grid fixed/star columns resolve the form layout");
    Click(Find<CheckBox>(check)); Require(checkModel.Value, "Routed checkbox click updates its two-way binding");
    Key(Find<RadioButton>(radioB), 32); Require(Find<RadioButton>(radioB).Value && !Find<RadioButton>(radioA).Value, "Packaged V2 radio group remains exclusive");
    var comboControl = Find<ComboBox>(combo);
    Click(comboControl); Require(comboControl.IsOpen, "Packaged ComboBox opens its popup");
    doc.Dispatch(new(EventKind.KeyDown) { Key = 27 }); Require(!comboControl.IsOpen, "Escape closes the popup");
    Key(comboControl, 40); Require(comboControl.SelectedIndex == 1, "ComboBox keyboard selection uses packaged input routing");
    doc.Focus(inputControl); doc.Dispatch(new(EventKind.KeyDown) { Key = 65, Control = true }); doc.Dispatch(new(EventKind.TextInput) { Text = "Boreal" });
    Require(nameModel.Value == "Boreal", "Packaged TextBox edits update the application model");
    amountModel.Value = 75; Require(numberControl.Value == 75 && progressControl.Value == .75f, "Model updates number and progress bindings");
    numberControl.Value = 80; Require(amountModel.Value == 80 && progressControl.Value == .8f, "Number edits update the model and dependent progress");
    Click(apply); Require(applied == 1 && commandRuns == 1, "Definition command and disposable application command execute");
    nameModel.Value = ""; command.Refresh(); Require(!apply.Enabled, "Command CanExecute disables its bound button");
    nameModel.Value = "Boreal"; command.Refresh();
    var tableControl = Find<DataGrid>(table); tableControl.Sort(0); tableControl.Filter = "Ready";
    Require(tableControl.RowCount == 2, "Packaged read-only DataGrid sorts and filters"); tableControl.Filter = "";
    var tabsControl = Find<TabControl>(tabs); tabsControl.SelectedIndex = 1; doc.BuildFrame(); tabsControl.SelectedIndex = 0; doc.BuildFrame();
    Require(inputControl.Text == "Boreal", "Tab navigation retains the edited form");
    var itemViews = new ItemsControl<string> { Key = value => value, ItemTemplate = value => new Label(value) };
    itemViews.SetItems(["A", "B"]); var retained = itemViews.ItemViews["B"]; itemViews.SetItems(["B", "A"]);
    Require(ReferenceEquals(retained, itemViews.Children[0]), "Keyed composition retains views across reorder");
    var constrained = new Element { Layout = new() { Width = UiLength.Percent(.5f), Height = UiLength.Fixed(22), Margin = new Thickness(4) } };
    var layoutRoot = new Element(); layoutRoot.Add(constrained); var layoutDoc = new UiDocument(layoutRoot, renderer, 200, 80); layoutDoc.BuildFrame();
    Require(Math.Abs(constrained.Bounds.Width - 96) < .01f && constrained.Bounds.Height == 22, "Packaged percent/fixed sizing accounts for margins");
    FrameStats stats = renderer.Render(doc.BuildFrame());
    Require(stats.Instances > 100 && stats.DrawCalls > 0, "Real GPU submits Beta component geometry");
    string screenshot = Path.Combine(output, "package-consumer.png"); renderer.SavePng(screenshot);
    bindings.Dispose(); amountModel.Value = 20; Require(numberControl.Value == 80, "Disposing the view releases model subscriptions");
    bool editorVerified = false, windowVerified = false;
    string? windowAdapter = null;
#if FULL_PACKAGES
    var editor = new EditorSession(asset); editor.Select(check.Id); editor.UpdateSelected(node => node.Checked = true);
    editor.Undo(); Require(!editor.Find(check.Id).Checked, "Packaged Editor.Core undo restores a Beta property"); editor.Redo();
    string saved = Path.Combine(output, "editor-saved.youi.json"); editor.Save(saved);
    var reopened = new EditorSession(); reopened.Open(saved);
    Require(reopened.Asset.Version == 2 && reopened.Find(check.Id).Checked, "Packaged Editor.Core saves and reopens V2 definitions");
    var exports = new CSharpExporter().Export(new ExportContext(reopened.Asset));
    var generated = exports.Single(f => f.RelativePath == "Screen.g.cs"); File.WriteAllText(Path.Combine(output, "Screen.g.cs.txt"), generated.Content);
    Require(generated.Content.Contains("new CheckBox") && generated.Content.Contains("new NumericUpDown") && generated.Content.Contains("new Grid"), "Packaged C# exporter emits Beta component construction");
    editorVerified = true;
    Require(typeof(DesktopWindow).Assembly.GetReferencedAssemblies().All(a => a.Name != "YoUI.Editor.Core"), "Windows host has no editor dependency");
    if (verifyWindow)
    {
        using var window = new DesktopWindow("YoUI Beta package consumer", UiAssetRuntime.Build(reopened.Asset).Root, 960, 660, 320, 240);
        window.RenderNow(); window.PumpEvents();
        Require(window.Handle != 0 && window.LastFrame.Instances > 100, "Packaged Windows host creates HWND and submits a native-window frame");
        window.Renderer.SavePng(Path.Combine(output, "package-window.png")); windowAdapter = window.Renderer.Adapter; windowVerified = true;
    }
#else
    Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "YoUI.Editor.Core" or "YoUI.Platform.Windows"), "Runtime-only consumer operates without editor or Windows host assemblies");
    if (verifyWindow) throw new ArgumentException("Runtime-only consumer cannot request a window.");
#endif
    var report = new { status = "passed", version = expectedVersion, configuration, rid = "win-x64", packageAssemblies = versions,
        abi = $"0x{abi:X8}", commandSize, adapter = renderer.Adapter, backend = capabilities.Backend.ToString(), deviceKind = capabilities.DeviceKind.ToString(),
        commands = stats.Commands, instances = stats.Instances, drawCalls = stats.DrawCalls, nativePath, nativeSha256 = nativeHash,
        screenshot, screenshotSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(screenshot))), checks,
        editorVerified, nativeWindowVerified = windowVerified, windowAdapter, physicalInputVerified = false, imeVerified = false,
        evidence = "Isolated exact PackageReference restore; routed logical input; real offscreen GPU; optional native HWND; no physical input or external animation SDKs." };
    File.WriteAllText(Path.Combine(output, "validation.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PASS {checks.Count} package-consumer checks; {renderer.Adapter}; native SHA256 {nativeHash}");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine("Beta package consumer failed: " + error); return 1; }
finally { if (native != 0) NativeLibrary.Free(native); }

static UiNode Node(NodeKind kind, string text = "", float x = 0, float y = 0, float width = 160, float height = 36)
    => new() { Kind = kind, Name = kind.ToString(), Text = text, X = x, Y = y, Width = width, Height = height };

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate uint NativeQuery();
