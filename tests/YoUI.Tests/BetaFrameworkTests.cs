using System.ComponentModel;
using System.Numerics;
using YoUI;

static class BetaFrameworkTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> require)
    {
        void Check(bool condition, string message = "Beta framework assertion failed") => require(condition, message);
        static bool Near(float a, float b) => Math.Abs(a - b) < .01f;
        static UiDocument Doc(Element root, float width = 300, float height = 200) => new(root, new TestMeasurer(), width, height);
        static void Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }

        test("Beta box percent, margins, minmax and alignment use padded content", () =>
        {
            var root = new Element { Padding = 10 };
            var child = root.Add(new Element { Layout = new BoxLayout { Width = UiLength.Percent(.5f), Height = UiLength.Fixed(40), MinWidth = 130, MaxWidth = 150, Margin = new(5, 10, 25, 10), Horizontal = Alignment.Center, Vertical = Alignment.Center } });
            var doc = Doc(root); doc.EnsureLayout();
            Check(Near(child.Bounds.Width, 130) && Near(child.Bounds.Height, 40));
            Check(Near(child.Bounds.X, 75) && Near(child.Bounds.Y, 80));
            child.Layout = child.Layout! with { Width = UiLength.Auto, Horizontal = Alignment.Stretch }; doc.EnsureLayout();
            Check(Near(child.Bounds.Width, 150) && Near(child.Bounds.X, 15));
        });
        test("Beta box finite validation and transform ownership are atomic", () =>
        {
            var child = new Element { Layout = new BoxLayout() }; var original = child.Layout;
            Throws<InvalidDataException>(() => child.Layout = new() { MaxWidth = float.PositiveInfinity });
            Throws<InvalidDataException>(() => child.Layout = new() { MinHeight = 20, MaxHeight = 10 });
            Throws<InvalidDataException>(() => child.Layout = new() { Margin = new(-1) });
            Throws<InvalidDataException>(() => child.Layout = new() { Width = new((SizeUnit)50) });
            Throws<InvalidOperationException>(() => child.Transform = RectTransform.Stretch()); Check(child.Layout == original && child.Transform == null);
            child.Layout = null; child.Transform = RectTransform.Stretch(); Throws<InvalidOperationException>(() => child.Layout = new());
            var bounded = new BoxLayout { Width = UiLength.Percent(32768), Height = UiLength.Fixed(32768) }.Constrain(new(float.MaxValue), Vector2.Zero);
            Check(bounded.X == 32768 && bounded.Y == 32768);
            Throws<ArgumentOutOfRangeException>(() => new BoxLayout().Constrain(new(float.NaN, 0), Vector2.Zero));
        });
        test("Beta stack accounts for margins and percent once", () =>
        {
            var root = new StackPanel { Gap = 7 };
            var child = root.Add(new Element { Layout = new() { Width = UiLength.Percent(.5f), Height = UiLength.Percent(.5f), Margin = new(5), Horizontal = Alignment.Center } });
            var next = root.Add(new Element { Width = 20, Height = 20 }); var doc = Doc(root); doc.EnsureLayout();
            Check(Near(child.Bounds.Width, 145) && Near(child.Bounds.Height, 95));
            Check(Near(child.Bounds.X, 77.5f) && Near(child.Bounds.Y, 5) && Near(next.Bounds.Y, 112));
            child.Visible = false; doc.EnsureLayout(); Check(next.Bounds.Y == 0);
        });
        test("Beta nested stacks allocate percent children from the arranged extent", () =>
        {
            foreach (bool horizontal in new[] { true, false })
            {
                var outer = new StackPanel { Horizontal = horizontal };
                var inner = outer.Add(new StackPanel { Horizontal = horizontal, Layout = horizontal ? new() { Height = UiLength.Fixed(40) } : new() { Width = UiLength.Fixed(40) } });
                BoxLayout ChildLayout() => horizontal ? new() { Width = UiLength.Percent(.25f), Height = UiLength.Fixed(40) } : new() { Width = UiLength.Fixed(40), Height = UiLength.Percent(.25f) };
                var first = inner.Add(new Element { Layout = ChildLayout() }); var second = inner.Add(new Element { Layout = ChildLayout() });
                var doc = Doc(outer, horizontal ? 600 : 200, horizontal ? 200 : 600); doc.EnsureLayout();
                Check(Near(horizontal ? inner.Bounds.Width : inner.Bounds.Height, 300));
                Check(Near(horizontal ? first.Bounds.Width : first.Bounds.Height, 75));
                Check(Near(horizontal ? second.Bounds.X : second.Bounds.Y, 75), "later percent child retained a slot from the earlier available extent");
            }
        });
        test("Beta nested stack recalculates grow space after percent minmax remeasurement", () =>
        {
            var outer = new StackPanel { Horizontal = true };
            var inner = outer.Add(new StackPanel { Horizontal = true, Padding = 5, Layout = new() { Height = UiLength.Fixed(50) } });
            var first = inner.Add(new Element { Layout = new() { Width = UiLength.Percent(.25f), MaxWidth = 50, Height = UiLength.Fixed(40) } });
            var second = inner.Add(new Element { Layout = new() { Width = UiLength.Percent(.25f), MinWidth = 100, MaxWidth = 110, Height = UiLength.Fixed(40) } });
            var fill = inner.Add(new Element { Grow = 1, Layout = new() { MinWidth = 20, MaxWidth = 40, Height = UiLength.Fixed(40) } });
            var doc = Doc(outer, 600, 200); doc.EnsureLayout();
            Check(Near(inner.Bounds.Width, 190) && Near(first.Bounds.Width, 45) && Near(second.Bounds.Width, 100));
            Check(Near(second.Bounds.X, 50) && Near(fill.Bounds.X, 150) && Near(fill.Bounds.Width, 35) && Near(fill.Bounds.Right, inner.Bounds.Right - inner.Padding));
        });
        test("Beta stack growth remains finite for large valid weights", () =>
        {
            foreach (bool boxed in new[] { true, false })
            {
                var stack = new StackPanel { Horizontal = true };
                var first = stack.Add(new Element { Grow = float.MaxValue, Height = 20, Layout = boxed ? new() { Height = UiLength.Fixed(20) } : null });
                var second = stack.Add(new Element { Grow = float.MaxValue, Height = 20, Layout = boxed ? new() { Height = UiLength.Fixed(20) } : null });
                Doc(stack).EnsureLayout(); Check(first.Bounds.Width == 150 && second.Bounds.Width == 150 && second.Bounds.X == 150);
            }
        });
        test("Beta grid fixed auto weighted star and padding allocate nonnegative extents", () =>
        {
            var grid = new Grid { Padding = 10, Gap = 5, Columns = new[] { GridTrack.Fixed(40), GridTrack.Auto, GridTrack.Star(1), GridTrack.Star(2) }, Rows = new[] { GridTrack.Star() } };
            var fixedChild = grid.Add(new Element()); var auto = grid.Add(new Element { Width = 30, Height = 20 }); var first = grid.Add(new Element()); var second = grid.Add(new Element());
            grid.SetCell(auto, 0, 1); grid.SetCell(first, 0, 2); grid.SetCell(second, 0, 3);
            var doc = Doc(grid, 315, 100); doc.EnsureLayout();
            Check(fixedChild.Bounds == new Rect(10, 10, 40, 80)); Check(auto.Bounds.Width == 30);
            Check(Near(first.Bounds.Width, 70) && Near(second.Bounds.Width, 140) && Near(second.Bounds.Right, 305));
            doc.Resize(40, 40); doc.EnsureLayout(); Check(first.Bounds.Width == 0 && second.Bounds.Width == 0 && float.IsFinite(second.Bounds.X));
            auto.Visible = false; doc.Resize(315, 100); doc.EnsureLayout(); Check(Near(first.Bounds.Width, 80) && Near(second.Bounds.Width, 160));
        });
        test("Beta grid spans distribute auto demand and include inter-track gaps", () =>
        {
            var grid = new Grid { Gap = 4, Columns = new[] { GridTrack.Fixed(30), GridTrack.Auto, GridTrack.Auto }, Rows = new[] { GridTrack.Auto, GridTrack.Auto, GridTrack.Star() } };
            var spanning = grid.Add(new Element { Width = 100, Height = 64 }); grid.SetCell(spanning, 0, 0, 2, 3);
            var narrow = grid.Add(new Element { Width = 20, Height = 12 }); grid.SetCell(narrow, 0, 1);
            var tail = grid.Add(new Element()); grid.SetCell(tail, 2, 2);
            var doc = Doc(grid, 200, 100); doc.EnsureLayout();
            Check(Near(spanning.Bounds.Width, 100) && Near(spanning.Bounds.Height, 64));
            Check(Near(narrow.Bounds.X, 34) && Near(narrow.Bounds.Width, 41));
            Check(Near(tail.Bounds.X, 79) && Near(tail.Bounds.Width, 21) && Near(tail.Bounds.Y, 68) && Near(tail.Bounds.Height, 32));
        });
        test("Beta grid remeasures wrapping at resolved width and respects box constraints", () =>
        {
            var grid = new Grid { Columns = new[] { GridTrack.Star(), GridTrack.Fixed(50) }, Rows = new[] { GridTrack.Auto, GridTrack.Star() }, Padding = 5 };
            var label = grid.Add(new Label(new string('x', 40)) { Layout = new() { MaxWidth = 90, Margin = new(2), Horizontal = Alignment.Center } });
            var below = grid.Add(new Element()); grid.SetCell(below, 1, 0);
            var doc = Doc(grid, 160, 300); doc.EnsureLayout();
            Check(label.Bounds.Width == 90 && label.Bounds.Height > 38 && below.Bounds.Y >= label.Bounds.Bottom + 2);
        });
        test("Beta grid validates spans, track snapshots and placement ownership", () =>
        {
            var tracks = new[] { GridTrack.Fixed(10), GridTrack.Star() }; var grid = new Grid { Columns = tracks }; tracks[0] = GridTrack.Fixed(999);
            Check(grid.Columns[0].Value == 10);
            var child = grid.Add(new Element()); grid.SetCell(child, 0, 1);
            Throws<ArgumentException>(() => grid.Columns = new[] { GridTrack.Auto });
            Throws<ArgumentOutOfRangeException>(() => grid.SetCell(child, 0, 0, int.MaxValue, 1));
            Throws<ArgumentException>(() => grid.SetCell(new Element(), 0, 0));
            Throws<ArgumentException>(() => grid.Rows = new[] { GridTrack.Star(0) });
            Throws<ArgumentOutOfRangeException>(() => grid.Gap = float.NaN);
            child.Transform = RectTransform.Stretch(); Throws<InvalidOperationException>(() => grid.Measure(new(100, 100)));
            var doc = Doc(grid); Throws<InvalidOperationException>(doc.EnsureLayout); child.Transform = null;
            Check(doc.UpdateLayout(), "failed grid layout should be retryable");
        });
        test("Beta grid resets invalid detached cells and spans on reattachment", () =>
        {
            foreach (bool vertical in new[] { false, true })
            foreach (bool spanning in new[] { false, true })
            foreach (bool hidden in new[] { false, true })
            {
                var grid = new Grid { Columns = new[] { GridTrack.Star(), GridTrack.Star() }, Rows = new[] { GridTrack.Star(), GridTrack.Star() } };
                var child = grid.Add(new Element());
                grid.SetCell(child, vertical && !spanning ? 1 : 0, !vertical && !spanning ? 1 : 0, vertical && spanning ? 2 : 1, !vertical && spanning ? 2 : 1);
                var doc = Doc(grid, 200, 100); doc.EnsureLayout();
                grid.Remove(child);
                if (vertical) grid.Rows = new[] { GridTrack.Star() }; else grid.Columns = new[] { GridTrack.Star() };
                child.Visible = !hidden; grid.Add(child); doc.EnsureLayout();
                child.Visible = true; doc.EnsureLayout();
                Check(child.Bounds == new Rect(0, 0, vertical ? 100 : 200, vertical ? 100 : 50), "stale cell did not return to the default placement");
                // A valid occupied placement still rejects track removal atomically.
                grid.SetCell(child, vertical ? 0 : 1, vertical ? 1 : 0);
                Throws<ArgumentException>(() => { if (vertical) grid.Columns = new[] { GridTrack.Star() }; else grid.Rows = new[] { GridTrack.Star() }; });
                doc.EnsureLayout();
                Check(vertical ? child.Bounds.X == 100 : child.Bounds.Y == 50);
            }
        });
        test("Beta grid retains valid cell metadata across removal and reattachment", () =>
        {
            var grid = new Grid { Columns = new[] { GridTrack.Fixed(40), GridTrack.Star() } };
            var child = grid.Add(new Element()); grid.SetCell(child, 0, 1);
            var doc = Doc(grid, 200, 100); doc.EnsureLayout(); grid.Remove(child); grid.Add(child); doc.EnsureLayout();
            Check(child.Bounds == new Rect(40, 0, 160, 100));
        });
        test("Beta wrap recomputes lines on resize and skips hidden items", () =>
        {
            var wrap = new WrapPanel { Gap = 5, Padding = 5 }; var a = wrap.Add(new Element { Width = 60, Height = 20 }); var b = wrap.Add(new Element { Width = 60, Height = 20 }); var c = wrap.Add(new Element { Width = 60, Height = 20 });
            var doc = Doc(wrap, 140, 200); doc.EnsureLayout(); Check(a.Bounds.Y == 5 && b.Bounds.Y == 5 && c.Bounds.Y == 30);
            doc.Resize(75, 200); doc.EnsureLayout(); Check(b.Bounds.Y == 30 && c.Bounds.Y == 55);
            b.Visible = false; doc.EnsureLayout(); Check(c.Bounds.Y == 30);
            a.Layout = new() { Width = UiLength.Percent(.5f), Height = UiLength.Fixed(20), Margin = new(2) }; doc.EnsureLayout();
            Check(Near(a.Bounds.Width, 30.5f) && Near(a.Bounds.X, 7));
            c.Transform = RectTransform.Fixed(0, 0, 10, 10); Throws<InvalidOperationException>(doc.EnsureLayout);
        });
        test("Beta child reorder retains attachment, focus and local coordinate conversion", () =>
        {
            var root = new Element { VisualOffset = new(3, 4) }; var a = root.Add(new Button("A") { VisualOffset = new(2, 1) }); var b = root.Add(new Button("B")); var probe = a.Add(new AttachmentProbe());
            var doc = Doc(root); doc.EnsureLayout(); doc.Focus(a); int loads = probe.Loads;
            root.ReorderChildren(new[] { b, a }); Check(doc.Focused == a && a.Document == doc && probe.Loads == loads && probe.Unloads == 0 && root.Children[0] == b);
            Check(a.WindowToLocal(new(10, 10)) == new Vector2(5, 5));
            Throws<ArgumentException>(() => root.ReorderChildren(new[] { a, a })); Check(root.Children[0] == b);
        });
        test("Beta one-way bindings observe wildcard properties and roll back initial failure", () =>
        {
            var model = new NotifyProbe(); using var scope = new BindingScope(); int result = 0;
            using var binding = scope.Observe(model, "Count", () => model.Count, value => result = value);
            Check(model.Subscribers == 1 && result == 0); model.Count = 4; model.Notify(null); Check(result == 4);
            model.Count = 8; model.Notify("Other"); Check(result == 4); model.Notify(""); Check(result == 8);
            binding.Dispose(); Check(model.Subscribers == 0); model.Count = 9; model.Notify("Count"); Check(result == 8);
            Throws<InvalidOperationException>(() => scope.Observe(model, "Count", () => model.Count, _ => throw new InvalidOperationException("initial")));
            Check(model.Subscribers == 0, "failed setup leaked model listener");
        });
        test("Beta validated text binding preserves invalid edit and releases both directions", () =>
        {
            var model = new ObservableValue<string>("valid"); var box = new TextBox(); using var scope = new BindingScope(); string? error = null;
            var binding = scope.BindText(box, model, value => value.Length < 3 ? "Too short" : null, value => error = value);
            Check(box.Text == "valid" && error == null); box.Text = "x"; Check(model.Value == "valid" && box.Text == "x" && error == "Too short");
            box.Text = "edited"; Check(model.Value == "edited" && error == null); model.Value = "new model"; Check(box.Text == "new model");
            binding.Dispose(); model.Value = "detached"; Check(box.Text == "new model"); box.Text = "later edit"; Check(model.Value == "detached");
        });
        test("Beta two-way toggle number and selection bind without feedback", () =>
        {
            using var scope = new BindingScope(); var toggle = new Toggle("T"); var flag = new ObservableValue<bool>(true); scope.BindToggle(toggle, flag);
            Check(toggle.Value); toggle.Value = false; Check(!flag.Value); flag.Value = true; Check(toggle.Value);
            var number = new NumericUpDown { Minimum = 0, Maximum = 100 }; var amount = new ObservableValue<float>(5); scope.BindNumber(number, amount);
            amount.Value = 9; Check(number.Value == 9); number.Value = 12; Check(amount.Value == 12);
            var combo = new ComboBox(); combo.SetItems(new[] { "a", "b", "c" }); var selection = new ObservableValue<int>(1); scope.BindSelection(combo, selection);
            Check(combo.SelectedIndex == 1); combo.SelectedIndex = 2; Check(selection.Value == 2); selection.Value = 0; Check(combo.SelectedIndex == 0);
            var list = new VirtualList { ItemCount = 5 }; var row = new ObservableValue<int>(2); scope.BindSelection(list, row); list.SelectedIndex = 3; Check(row.Value == 3);
            scope.Dispose(); flag.Value = false; amount.Value = 20; selection.Value = 2; row.Value = 0;
            Check(toggle.Value && number.Value == 12 && combo.SelectedIndex == 0 && list.SelectedIndex == 3);
        });
        test("Beta two-way model transfers retain effective number text and selection values", () =>
        {
            using var scope = new BindingScope(); var number = new NumericUpDown { Maximum = 100 }; var amount = new ObservableValue<float>(200); scope.BindNumber(number, amount);
            Check(number.Value == 100 && amount.Value == 100, "initial numeric clamp was not reflected in the model");
            amount.Value = 300; Check(number.Value == 100 && amount.Value == 100, "unchanged clamped control still needs model correction");
            amount.Value = -10; Check(number.Value == 0 && amount.Value == 0);
            var text = new ObservableValue<string>("A\nB"); var box = new TextBox(); string? validated = null; int validations = 0;
            scope.BindText(box, text, value => { validated = value; validations++; return null; });
            Check(box.Text == "AB" && text.Value == "AB" && validated == "AB" && validations == 1);
            text.Value = "C\r\nD\t"; Check(box.Text == "CD" && text.Value == "CD" && validated == "CD");
            var list = new VirtualList { ItemCount = 5 }; var selection = new ObservableValue<int>(99); scope.BindSelection(list, selection);
            Check(list.SelectedIndex == 4 && selection.Value == 4); selection.Value = -20; Check(list.SelectedIndex == -1 && selection.Value == -1);
        });
        test("Beta normalization preserves newer model writes and corrected control events", () =>
        {
            using var scope = new BindingScope(); var number = new NumericUpDown { Maximum = 100 }; var amount = new ObservableValue<float>(5);
            number.ValueChanged += value => { if (value == 100) amount.Value = 60; if (value == 10) number.Value = 11; };
            scope.BindNumber(number, amount); amount.Value = 200;
            Check(amount.Value == 60 && number.Value == 60, "coercion replaced a newer application model write");
            number.Value = 10; Check(amount.Value == 11 && number.Value == 11, "an earlier event overwrote the corrected control value");
            var text = new ObservableValue<string>("start"); var box = new TextBox();
            scope.BindText(box, text, value => { if (value == "edit") text.Value = "newer"; return null; });
            box.Text = "edit"; Check(text.Value == "newer" && box.Text == "newer");
        });
        test("Beta contradictory coercion callbacks are bounded and setup releases both sides", () =>
        {
            using var scope = new BindingScope(); var number = new NumericUpDown { Maximum = 100 }; var amount = new ObservableValue<float>(200); int corrections = 0;
            PropertyChangedEventHandler reject = (_, _) => { if (amount.Value == 100) { corrections++; amount.Value = 200; } };
            amount.PropertyChanged += reject;
            Throws<InvalidOperationException>(() => scope.BindNumber(number, amount)); Check(corrections == 32);
            amount.PropertyChanged -= reject; amount.Value = 7; Check(number.Value == 100);
            number.Value = 8; Check(amount.Value == 7, "failed setup retained a control-to-model listener");
            var disposing = new BindingScope(); var other = new NumericUpDown { Maximum = 100 }; var source = new ObservableValue<float>(5);
            disposing.BindNumber(other, source); source.PropertyChanged += (_, _) => { if (source.Value == 100) disposing.Dispose(); };
            source.Value = 200; Check(source.Value == 100 && other.Value == 100); source.Value = 20; Check(other.Value == 100);
        });
        test("Beta binding coalesces reentrancy and bounds nonsettling callbacks", () =>
        {
            using var scope = new BindingScope(); var model = new ObservableValue<int>(0); int latest = -1, calls = 0;
            using var finite = scope.Bind(model, value => { latest = value; calls++; if (value < 3) model.Value++; });
            Check(latest == 3 && calls == 4);
            var endless = new ObservableValue<int>(0); int attempts = 0;
            Throws<InvalidOperationException>(() => scope.Bind(endless, value => { attempts++; endless.Value = value + 1; }));
            Check(attempts == 32); endless.Value = 100; Check(attempts == 32, "nonsettling initial binding leaked its listener");
            var text = new ObservableValue<string>("initial"); var box = new TextBox();
            text.PropertyChanged += (_, _) => { if (text.Value == "lower") text.Value = "UPPER"; };
            scope.BindText(box, text); box.Text = "lower"; Check(text.Value == "UPPER" && box.Text == "UPPER");
        });
        test("Beta binding disposal attempts all releases and failed two-way setup is clean", () =>
        {
            var scope = new BindingScope(); var releases = new List<int>();
            scope.Track(() => releases.Add(1)); scope.Track(() => { releases.Add(2); throw new InvalidOperationException(); }); scope.Track(() => releases.Add(3));
            Throws<AggregateException>(scope.Dispose); Check(releases.SequenceEqual(new[] { 3, 2, 1 })); scope.Dispose(); Throws<ObjectDisposedException>(() => scope.Track(() => { }));
            using var other = new BindingScope(); var source = new ObservableValue<string>("A"); var box = new TextBox(); int validationCalls = 0;
            Throws<InvalidOperationException>(() => other.BindText(box, source, _ => { validationCalls++; throw new InvalidOperationException(); }));
            source.Value = "B"; box.Text = "C"; Check(validationCalls == 1 && source.Value == "B");
        });
        test("Beta bindings stop callbacks when initial read or transfer disposes the owner", () =>
        {
            var scope = new BindingScope(); var notifier = new NotifyProbe(); int callbacks = 0;
            scope.Observe(notifier, "Count", () => { scope.Dispose(); return 1; }, _ => callbacks++);
            Check(callbacks == 0 && notifier.Subscribers == 0);
            var other = new BindingScope(); var source = new ObservableValue<string>("initial"); var box = new TextBox();
            box.Changed += _ => other.Dispose(); other.BindText(box, source, _ => { callbacks++; return null; });
            source.Value = "later"; Check(box.Text == "initial" && callbacks == 0);
            var commandScope = new BindingScope(); var button = new Button("Run"); var command = new CommandProbe();
            command.BeforeSubscribe = commandScope.Dispose;
            commandScope.BindCommand(button, command); Check(command.Subscribers == 0 && command.Queries == 0);
        });
        test("Beta commands refresh enabled state, honor parameters and release handlers", () =>
        {
            var root = new Element(); var button = root.Add(new Button("Run")); var doc = Doc(root); doc.Focus(button);
            bool allowed = false; int total = 0, parameter = 2; var command = new UiCommand(value => total += (int)value!, _ => allowed); using var scope = new BindingScope();
            var binding = scope.BindCommand(button, command, () => parameter); Check(!button.Enabled);
            allowed = true; command.Refresh(); Check(button.Enabled); doc.Focus(button); doc.Dispatch(new(EventKind.KeyDown) { Key = 13 }); Check(total == 2);
            parameter = 3; doc.Dispatch(new(EventKind.KeyDown) { Key = 13 }); Check(total == 5);
            binding.Dispose(); allowed = false; command.Refresh(); Check(button.Enabled); doc.Dispatch(new(EventKind.KeyDown) { Key = 13 }); Check(total == 5);
        });
        test("Beta observable and scope reject cross-thread mutation without detaching", () =>
        {
            var source = new ObservableValue<int>(1); using var scope = new BindingScope(); int applied = 0; scope.Bind(source, value => applied = value);
            Exception? error = null; var worker = new Thread(() => { try { source.Value = 2; } catch (Exception ex) { error = ex; } }); worker.Start(); worker.Join();
            Check(error is InvalidOperationException && source.Value == 1 && applied == 1); error = null;
            worker = new Thread(() => { try { scope.Dispose(); } catch (Exception ex) { error = ex; } }); worker.Start(); worker.Join();
            Check(error is InvalidOperationException); source.Value = 3; Check(applied == 3);
        });
    }
    private sealed class NotifyProbe : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? changed;
        public int Count;
        public int Subscribers { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged { add { changed += value; Subscribers++; } remove { changed -= value; Subscribers--; } }
        public void Notify(string? property) => changed?.Invoke(this, new(property));
    }
    private sealed class CommandProbe : System.Windows.Input.ICommand
    {
        private EventHandler? changed;
        public Action? BeforeSubscribe;
        public int Subscribers, Queries;
        public event EventHandler? CanExecuteChanged
        {
            add { BeforeSubscribe?.Invoke(); changed += value; Subscribers++; }
            remove { if (changed?.GetInvocationList().Contains(value) == true) { changed -= value; Subscribers--; } }
        }
        public bool CanExecute(object? parameter) { Queries++; return true; }
        public void Execute(object? parameter) { }
    }
}
