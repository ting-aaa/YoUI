using System.Numerics;
using YoUI;

static class BetaControlTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> require)
    {
        void Check(bool condition, string message = "Beta control assertion failed") => require(condition, message);
        static UiDocument Doc(Element root, float w = 400, float h = 300) => new(root, new TestMeasurer(), w, h);
        static void Key(UiDocument doc, int key) => doc.Dispatch(new(EventKind.KeyDown) { Key = key });
        static void Click(UiDocument doc, float x, float y) { doc.Dispatch(new(EventKind.PointerDown) { Position = new(x, y) }); doc.Dispatch(new(EventKind.PointerUp) { Position = new(x, y) }); }
        static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }

        test("Beta checkbox keyboard activation updates checked semantics once", () =>
        {
            var box = new CheckBox("Remember"); var doc = Doc(box); int changed = 0; box.Changed += _ => changed++;
            doc.Focus(box); Key(doc, 32); box.Value = true;
            Check(box.Value && changed == 1 && doc.Semantics().First().Value == "checked" && box.AccessibleName == "Remember");
            Key(doc, 13); Check(!box.Value && changed == 2);
        });
        test("Beta radio groups commit exclusivity before failures and reentrant selections", () =>
        {
            var group = new RadioGroup(); var a = new RadioButton("A", group); var b = new RadioButton("B", group); var c = new RadioButton("C", group);
            a.Value = true; bool invariant = false;
            a.Changed += _ => { invariant = !a.Value && b.Value && group.Selected == b; throw new InvalidOperationException("observer"); };
            Throws<InvalidOperationException>(() => b.Value = true); Check(invariant && b.Value && !a.Value && group.Selected == b);
            b.Changed += value => { if (!value) c.Value = true; };
            Throws<InvalidOperationException>(() => a.Value = true); Check(c.Value && !a.Value && !b.Value && group.Selected == c);
        });
        test("Beta radio arrow navigation skips inactive members and transfers focus", () =>
        {
            var root = new StackPanel(); var group = new RadioGroup(); var a = root.Add(new RadioButton("A", group)); root.Add(new RadioButton("B", group) { Enabled = false }); var c = root.Add(new RadioButton("C", group)); var doc = Doc(root);
            doc.Focus(a); Key(doc, 39); Check(c.Value && doc.Focused == c && !a.Value); Key(doc, 39); Check(a.Value && doc.Focused == a);
            c.Group = null; c.Value = true; Check(a.Value && c.Value && group.Selected == a);
        });
        test("Beta radio transfer rejects old-group reentry without leaving stale memberships", () =>
        {
            var a = new RadioGroup(); var b = new RadioGroup(); var c = new RadioGroup(); var radio = new RadioButton("Moving", a) { Value = true };
            Action<RadioButton?> redirect = selected => { if (selected == null) radio.Group = c; }; a.SelectionChanged += redirect;
            Throws<InvalidOperationException>(() => radio.Group = b);
            Check(radio.Group == null && a.Selected == null && b.Selected == null && c.Selected == null && radio.Value);
            a.SelectionChanged -= redirect; radio.Group = b;
            var competitor = new RadioButton("Other", c) { Value = true };
            Check(radio.Group == b && b.Selected == radio && radio.Value && competitor.Value);
            radio.Group = c; Check(c.Selected == radio && !competitor.Value && radio.Value && b.Selected == null);
        });
        test("Beta radio transfer target failure keeps membership coherent and permits retry", () =>
        {
            var a = new RadioGroup(); var b = new RadioGroup(); var c = new RadioGroup(); var radio = new RadioButton("Moving", a) { Value = true };
            Action<RadioButton?> redirect = selected => { if (selected == radio) radio.Group = c; }; b.SelectionChanged += redirect;
            Throws<InvalidOperationException>(() => radio.Group = b);
            Check(radio.Group == b && b.Selected == radio && a.Selected == null && c.Selected == null && radio.Value);
            b.SelectionChanged -= redirect; radio.Group = c; Check(radio.Group == c && c.Selected == radio && b.Selected == null);
            var competitor = new RadioButton("Other", b) { Value = true }; Check(radio.Value && competitor.Value && c.Selected == radio);
        });
        test("Beta radio keyboard navigation honors modal scope and inactive membership", () =>
        {
            var group = new RadioGroup(); var root = new Element(); var background = root.Add(new RadioButton("Background", group)); var modal = root.Add(new StackPanel());
            var first = modal.Add(new RadioButton("First", group)); var disabled = modal.Add(new RadioButton("Disabled", group) { Enabled = false });
            var hiddenParent = modal.Add(new Element { Visible = false }); hiddenParent.Add(new RadioButton("Hidden descendant", group));
            var last = modal.Add(new RadioButton("Last", group)); var detached = new RadioButton("Detached", group); var foreign = new RadioButton("Foreign document", group); _ = Doc(foreign);
            var doc = Doc(root); first.Value = true; doc.SetModal(modal);
            Key(doc, 39); Check(last.Value && group.Selected == last && doc.Focused == last && !background.Value);
            Key(doc, 39); Check(first.Value && doc.Focused == first); Key(doc, 37); Check(last.Value && doc.Focused == last);
            doc.Focus(disabled); doc.Focus(detached); doc.Focus(foreign); Check(doc.Focused == last);
            // Explicit application selection retains the global group contract.
            background.Value = true; Check(group.Selected == background && !last.Value && doc.Focused == last);
            Key(doc, 39); Check(first.Value && doc.Focused == first && !background.Value);
        });
        test("Beta radio arrows and Tab share the top nested popup navigation scope", () =>
        {
            var group = new RadioGroup(); var root = new Element(); var background = root.Add(new RadioButton("Background", group)); var modal = root.Add(new Element()); var owner = modal.Add(new Button("Owner")); var modalRadio = modal.Add(new RadioButton("Modal", group)); var doc = Doc(root); doc.SetModal(modal);
            var firstContent = new StackPanel(); var firstA = firstContent.Add(new RadioButton("First A", group)); var firstB = firstContent.Add(new RadioButton("First B", group));
            var firstPopup = doc.ShowPopup(owner, firstContent, new(180, 100));
            var secondContent = new StackPanel(); var secondA = secondContent.Add(new RadioButton("Second A", group)); secondContent.Add(new RadioButton("Second disabled", group) { Enabled = false }); var secondB = secondContent.Add(new RadioButton("Second B", group));
            var secondPopup = doc.ShowPopup(firstA, secondContent, new(180, 140)); secondA.Value = true;
            Key(doc, 39); Check(secondB.Value && doc.Focused == secondB && !firstA.Value && !firstB.Value && !modalRadio.Value && !background.Value);
            Key(doc, 39); Check(secondA.Value && doc.Focused == secondA);
            doc.Focus(firstA); Key(doc, 39); Check(secondA.Value && doc.Focused == firstA && !firstB.Value);
            Key(doc, 9); Check(doc.Focused == secondA); Key(doc, 9); Check(doc.Focused == secondB); Key(doc, 27);
            Check(!secondPopup.IsOpen && doc.Focused == firstA); firstA.Value = true; Key(doc, 39); Check(firstB.Value && doc.Focused == firstB && !modalRadio.Value);
            firstPopup.Dispose(); Check(doc.PopupCount == 0);
        });
        test("Beta content slots validate ownership and detach focused descendants", () =>
        {
            var root = new Element(); var slot = root.Add(new ContentPresenter()); var old = new Element(); var input = old.Add(new TextBox()); slot.Content = old; var doc = Doc(root); doc.Focus(input);
            var other = root.Add(new Label("owned")); Throws<InvalidOperationException>(() => slot.Content = other); Check(slot.Content == old && doc.Focused == input);
            var replacement = new Label("replacement"); slot.Content = replacement;
            Check(doc.Focused == null && input.Document == null && old.Parent == null && slot.Content == replacement && replacement.Document == doc);
        });
        test("Beta content replacement failure leaves a coherent reusable slot", () =>
        {
            var host = new Element(); var button = host.Add(new Button("Custom")); var old = new Probe(); button.Content = old; var doc = Doc(host); doc.Focus(old);
            old.Event += e => { if (e.Kind == EventKind.Blur) throw new InvalidOperationException("blur"); };
            Throws<InvalidOperationException>(() => button.Content = new Label("unused")); Check(button.Content == null && button.Children.Count == 0 && old.Document == null);
            var broken = new Probe { FailAttach = true }; Throws<InvalidOperationException>(() => button.Content = broken);
            Check(button.Content == broken && broken.Parent == button && broken.Document == doc); broken.FailAttach = false;
            button.Content = new Label("retry"); Check(broken.Document == null && button.Content?.Document == doc);
        });
        test("Beta custom button content retains button keyboard activation", () =>
        {
            var button = new Button("Accessible action"); var visual = new StackPanel { Horizontal = true, Gap = 8 }; visual.Add(new Label("★")); visual.Add(new Label("Custom")); button.Content = visual;
            var doc = Doc(button); doc.BuildFrame(); int clicked = 0; button.Clicked += () => clicked++;
            Check(button.Caption.Parent == null && visual.Document == doc && visual.Bounds.Width > 0);
            doc.Focus(button); Key(doc, 32); Click(doc, 20, 20); Check(clicked == 2 && button.AccessibleName == "Accessible action");
        });
        test("Beta focused selected button ring never fills the translucent interior", () =>
        {
            var button = new Button("Selected") { Selected = true }; var doc = Doc(button, 150, 42); doc.Focus(button);
            var frame = doc.BuildFrame(); var commands = frame.Commands.ToArray(); var center = new Vector2(75, 21);
            var solidAccent = commands.Where(c => c.Kind == 0 && c.Color == doc.Theme.Accent).ToArray();
            Check(solidAccent.Length == 4 && solidAccent.All(c => !c.Rect.Contains(center)));
            Check(commands.Any(c => c.Kind == 0 && c.Rect == button.Bounds && c.Color.A < 1));
            Check(button.Caption.Color == doc.Theme.Accent && button.Caption.Bounds.Width > 0);
        });
        test("Beta keyed composition retains views focus and lifecycle through reorder", () =>
        {
            int released = 0;
            var items = new ItemsControl<(int Id, string Text)> { Key = item => item.Id, ItemTemplate = item => new Button(item.Text), BindItem = (view, item) => ((Button)view).Text = item.Text, ReleaseItem = _ => released++, Gap = 2 };
            items.SetItems([(1, "one"), (2, "two"), (3, "three")]); var doc = Doc(items); var retained = items.ItemViews[2]; doc.Focus(retained);
            items.SetItems([(3, "III"), (2, "II"), (1, "I")]); doc.BuildFrame();
            Check(items.Children[1] == retained && doc.Focused == retained && ((Button)retained).Text == "II" && released == 0);
            Throws<ArgumentException>(() => items.SetItems([(2, "duplicate"), (2, "key")])); Check(items.Children.Count == 3 && doc.Focused == retained);
            items.SetItems([(3, "three")]); Check(released == 2 && retained.Document == null && doc.Focused == null);
        });
        test("Beta keyed composition completes structural cleanup when release throws", () =>
        {
            int released = 0; var items = new ItemsControl<int> { Key = i => i, ItemTemplate = i => new Label(i.ToString()), ReleaseItem = _ => { released++; throw new InvalidOperationException("release"); } };
            items.SetItems([1, 2]); _ = Doc(items);
            Throws<InvalidOperationException>(() => items.SetItems([3])); Check(released == 2 && items.Children.Count == 1 && items.ItemViews.Count == 1 && items.ItemViews.ContainsKey(3));
        });
        test("Beta keyed composition releases failed candidates and applies percent layout once", () =>
        {
            int released = 0;
            var items = new ItemsControl<int> { Key = i => i, ItemTemplate = _ => new Element { Layout = new() { Height = UiLength.Percent(.5f), Width = UiLength.Percent(.5f), Margin = new(5) } }, BindItem = (_, item) => { if (item == 2) throw new InvalidOperationException("bind"); }, ReleaseItem = _ => released++ };
            Throws<InvalidOperationException>(() => items.SetItems([1, 2])); Check(released == 2 && items.Children.Count == 0 && items.ItemViews.Count == 0);
            items.SetItems([1]); var doc = Doc(items, 200, 100); doc.BuildFrame();
            Check(items.Children[0].Bounds == new Rect(5, 5, 95, 45));
        });
        test("Beta numeric editor commits invariant finite ranges and cancels invalid input", () =>
        {
            var numeric = new NumericUpDown { Minimum = -2, Maximum = 3, Step = .5f }; var doc = Doc(numeric); int changed = 0; numeric.ValueChanged += _ => changed++;
            doc.Focus(numeric.Input); numeric.Input.Text = "2.75"; Key(doc, 13); Check(numeric.Value == 2.75f && changed == 1);
            Key(doc, 38); Check(numeric.Value == 3 && changed == 2); numeric.Input.Text = "NaN"; Key(doc, 13); Check(numeric.Input.Text == "3" && numeric.Value == 3);
            numeric.Input.Text = "-10"; doc.Focus(null); Check(numeric.Value == -2 && changed == 3);
            Throws<ArgumentOutOfRangeException>(() => numeric.Maximum = -3); Throws<ArgumentOutOfRangeException>(() => numeric.Step = 0); Throws<ArgumentOutOfRangeException>(() => numeric.Value = float.NaN);
            numeric.Value = 3; numeric.Step = float.MaxValue; doc.Focus(numeric.Input); Key(doc, 38); Check(numeric.Value == 3);
        });
        test("Beta narrow numeric step buttons center complete glyph bounds", () =>
        {
            var numeric = new NumericUpDown(); var doc = Doc(numeric, 180, 36); doc.BuildFrame();
            foreach (var button in numeric.Children.OfType<Button>())
            {
                var glyph = button.Caption;
                Check(button.Bounds.Width == 32 && glyph.Bounds.Width >= 9 && glyph.Bounds.Height > 18);
                Check(Math.Abs(glyph.Bounds.X + glyph.Bounds.Width / 2 - button.Bounds.X - button.Bounds.Width / 2) < .01f);
            }
        });
        test("Beta progress validates finite values and exports nonfocusable semantics", () =>
        {
            var bar = new ProgressBar { Value = 2 }; var doc = Doc(bar); Check(bar.Value == 1 && doc.Semantics().Single().Role == "progressbar");
            Throws<ArgumentOutOfRangeException>(() => bar.Value = float.PositiveInfinity); bar.Value = -.5f; Check(bar.Value == 0);
        });
        test("Beta tabs retain content and move focus from a hidden page", () =>
        {
            var tabs = new TabControl(); var first = new Probe(); var input = first.Add(new TextBox { Text = "retained" }); var second = new Probe(); tabs.AddPage("One", first); tabs.AddPage("Two", second); var doc = Doc(tabs); doc.BuildFrame(); doc.Focus(input);
            tabs.SelectedIndex = 1; Check(first.Document == doc && !first.Visible && second.Visible && doc.Focused != input && first.Loads == 1 && first.Unloads == 0);
            Key(doc, 37); Check(tabs.SelectedIndex == 0 && first.Visible && input.Text == "retained" && first.Loads == 1);
            Key(doc, 35); Check(tabs.SelectedIndex == 1); Key(doc, 36); Check(tabs.SelectedIndex == 0);
            Throws<ArgumentOutOfRangeException>(() => tabs.SelectedIndex = 2);
        });
        test("Beta ComboBox popup escapes scroll clipping and restores composite focus", () =>
        {
            var content = new Element(); var combo = content.Add(new ComboBox { Transform = RectTransform.Fixed(10, 20, 140, 36) }); combo.SetItems(["Alpha", "Beta", "Gamma"]);
            var root = new Element(); root.Add(new ScrollView(content) { Transform = RectTransform.Fixed(0, 0, 200, 60) }); var doc = Doc(root); doc.BuildFrame();
            Click(doc, 30, 35); Check(combo.IsOpen && doc.PopupCount == 1);
            var option = doc.Focused!; var popupContent = option.Parent!.Parent!;
            Check(popupContent.Bounds.Bottom > 60 && doc.Semantics().Any(s => s.Name == "Gamma"));
            Click(doc, popupContent.Bounds.X + 15, popupContent.Bounds.Y + 50); Check(combo.SelectedIndex == 1 && !combo.IsOpen && doc.PopupCount == 0 && doc.Focused == combo);
            combo.SetOpen(true); Key(doc, 27); Check(!combo.IsOpen && doc.Focused == combo);
            Throws<ArgumentOutOfRangeException>(() => combo.SelectedIndex = 3); combo.SetItems([]); Check(combo.SelectedIndex == -1 && !combo.IsOpen);
        });
        test("Beta popup placement follows accumulated offsets and remains viewport bounded", () =>
        {
            var root = new Element { VisualOffset = new(8, 5) }; var panel = root.Add(new Element { Transform = RectTransform.Fixed(20, 30, 100, 80), VisualOffset = new(12, 7) }); var owner = panel.Add(new Button("Owner") { Transform = RectTransform.Fixed(0, 0, 70, 30), VisualOffset = new(3, 4) }); var doc = Doc(root, 200, 180); doc.BuildFrame();
            var popup = doc.ShowPopup(owner, new Button("Overlay"), new(110, 60)); Check(popup.Content.Bounds.X == owner.VisualBounds.X && popup.Content.Bounds.Y == owner.VisualBounds.Bottom);
            panel.VisualOffset += new Vector2(10, 0); doc.BuildFrame(); Check(popup.Content.Bounds.X == owner.VisualBounds.X);
            popup.Dispose(); var huge = doc.ShowPopup(owner, new Button("Huge"), new(1000, 1000));
            Check(huge.Content.Bounds == new Rect(0, 0, 200, 180)); huge.Dispose();
        });
        test("Beta popup outside close consumes click and Escape restores prior focus", () =>
        {
            var root = new Element(); var owner = root.Add(new Button("Owner") { Transform = RectTransform.Fixed(0, 0, 80, 30) }); var background = root.Add(new Button("Background") { Transform = RectTransform.Fixed(250, 200, 100, 40) }); var doc = Doc(root); int clicks = 0; background.Clicked += () => clicks++; doc.Focus(owner);
            var popup = doc.ShowPopup(owner, new Button("Popup"), new(100, 70)); Click(doc, 260, 220); Check(!popup.IsOpen && doc.Focused == owner && clicks == 0);
            popup = doc.ShowPopup(owner, new Button("Popup"), new(100, 70)); Key(doc, 27); Check(!popup.IsOpen && doc.Focused == owner); popup.Dispose();
        });
        test("Beta popup owner hide disable and detach terminate overlay lifetime immediately", () =>
        {
            foreach (int mode in new[] { 0, 1, 2 })
            {
                var root = new Element(); var parent = root.Add(new Element()); var owner = parent.Add(new Button("Owner")); var doc = Doc(root); var overlay = new Button("Overlay"); int closed = 0;
                var popup = doc.ShowPopup(owner, overlay, new(100, 60), () => closed++); doc.Dispatch(new(EventKind.PointerDown) { Position = new(10, overlay.Bounds.Y + 10) });
                if (mode == 0) parent.Visible = false; else if (mode == 1) parent.Enabled = false; else root.Remove(parent);
                Check(!popup.IsOpen && doc.PopupCount == 0 && overlay.Document == null && closed == 1 && doc.Focused == null);
                popup.Dispose(); Check(closed == 1);
            }
        });
        test("Beta nested popup owner close cleans descendants and restores base focus", () =>
        {
            var owner = new Button("Owner"); var doc = Doc(owner); doc.Focus(owner); var first = doc.ShowPopup(owner, new Button("First"), new(120, 80)); var second = doc.ShowPopup(first.Content, new Button("Second"), new(100, 60));
            first.Dispose(); Check(!first.IsOpen && !second.IsOpen && first.Content.Document == null && second.Content.Document == null && doc.PopupCount == 0 && doc.Focused == owner);
        });
        test("Beta popup modal scope blocks background owners and modal replacement closes overlays", () =>
        {
            var root = new Element(); var background = root.Add(new Button("Background")); var modal = root.Add(new Element()); var owner = modal.Add(new Button("Modal owner")); var doc = Doc(root); doc.SetModal(modal);
            Throws<InvalidOperationException>(() => doc.ShowPopup(background, new Button("Bad"), new(100, 60)));
            var popup = doc.ShowPopup(owner, new Button("Good"), new(100, 60)); Check(doc.Focused == popup.Content); doc.SetModal(null); Check(!popup.IsOpen && popup.Content.Document == null);
        });
        test("Beta modal transition attempts every failing popup cleanup", () =>
        {
            var root = new Element(); var owner = root.Add(new Button("Owner")); var modal = root.Add(new Element()); var doc = Doc(root); int closed = 0;
            var first = doc.ShowPopup(owner, new Button("First"), new(100, 60), () => { closed++; throw new InvalidOperationException("first"); });
            var second = doc.ShowPopup(owner, new Button("Second"), new(100, 60), () => { closed++; throw new InvalidOperationException("second"); });
            Throws<InvalidOperationException>(() => doc.SetModal(modal)); Check(closed == 2 && !first.IsOpen && !second.IsOpen && doc.PopupCount == 0 && first.Content.Document == null && second.Content.Document == null);
        });
        test("Beta popup failed attach and layout unwind association and restore focus", () =>
        {
            foreach (bool attach in new[] { true, false })
            {
                var owner = new Button("Owner"); var doc = Doc(owner); doc.Focus(owner); var broken = new Probe { FailAttach = attach, FailMeasure = !attach };
                Throws<InvalidOperationException>(() => doc.ShowPopup(owner, broken, new(100, 60)));
                Check(broken.Document == null && doc.PopupCount == 0 && doc.Focused == owner);
                broken.FailAttach = broken.FailMeasure = false; var retry = doc.ShowPopup(owner, broken, new(100, 60)); Check(retry.IsOpen); retry.Dispose();
            }
        });
        test("Beta popup paint and semantic failures retain retryable dirty stages", () =>
        {
            var owner = new Button("Owner"); var doc = Doc(owner); var content = new Probe(); var popup = doc.ShowPopup(owner, content, new(120, 70)); doc.BuildFrame(); doc.Semantics();
            content.FailPaint = true; content.Invalidate(); Throws<InvalidOperationException>(() => doc.BuildFrame()); Check(doc.NeedsRender && (content.Dirty & DirtyFlags.Paint) != 0);
            content.FailPaint = false; doc.BuildFrame(); Check(!doc.NeedsRender);
            content.FailSemantics = true; content.Invalidate(DirtyFlags.Semantics); Throws<InvalidOperationException>(() => doc.Semantics()); Check((content.Dirty & DirtyFlags.Semantics) != 0);
            content.FailSemantics = false; Check(doc.Semantics().Last().Role == "button"); popup.Dispose();
        });
        test("Beta popup failing blur cancel and closed callbacks still detach every descendant", () =>
        {
            var root = new Element(); var owner = root.Add(new Button("Owner")); var doc = Doc(root); doc.Focus(owner); var content = new Element(); var child = content.Add(new Button("Failure"));
            var popup = doc.ShowPopup(owner, content, new(100, 70), () => throw new InvalidOperationException("closed"));
            child.Event += e => { if (e.Kind is EventKind.Blur or EventKind.PointerCancel) throw new InvalidOperationException("termination"); };
            doc.Dispatch(new(EventKind.PointerDown) { Position = new(child.Bounds.X + 5, child.Bounds.Y + 5) });
            Throws<InvalidOperationException>(() => root.Remove(owner));
            Check(!popup.IsOpen && doc.PopupCount == 0 && content.Document == null && child.Document == null && owner.Document == null && doc.Focused == null);
        });
        test("Beta read-only table snapshots data sorts stably filters and maps offset input", () =>
        {
            var root = new Element { VisualOffset = new(10, 5) }; var parent = root.Add(new Element { VisualOffset = new(4, 7) }); var grid = parent.Add(new DataGrid { Transform = RectTransform.Fixed(20, 30, 250, 160), VisualOffset = new(3, 2) });
            string[] source = ["b", "2"]; DataColumn[] columns = [new("Name", 100), new("Count", 100)]; grid.SetData(columns, [("b", source), ("a1", new[] { "a", "1" }), ("a2", new[] { "a", "3" })]); source[0] = "wrong"; columns[0] = new("mutated", 50); var doc = Doc(root); doc.BuildFrame();
            grid.Sort(0); Check(grid.GetRowKey(0) == "a1" && grid.GetRowKey(1) == "a2" && grid.Columns[0].Header == "Name");
            var b = grid.VisualBounds; Click(doc, b.X + 10, b.Y + 40); Check(grid.SelectedKey == "a1");
            grid.Filter = "b"; Check(grid.RowCount == 1 && grid.GetRowKey(0) == "b" && grid.SelectedKey == ""); grid.Filter = ""; grid.Sort(0, true); Check(grid.GetRowKey(0) == "b");
            Throws<ArgumentException>(() => grid.SetData([new("Bad", float.NaN)], [])); Check(grid.RowCount == 3);
            Throws<ArgumentException>(() => grid.SetData([new("One")], [("duplicate", new[] { "a" }), ("duplicate", new[] { "b" })])); Check(grid.RowCount == 3);
            doc.Focus(grid); Key(doc, 35); Check(grid.SelectedKey == "a2"); Key(doc, 36); Check(grid.SelectedKey == "b");
        });
        test("Beta read-only table emits a bounded viewport for large sources", () =>
        {
            var grid = new DataGrid(); grid.SetData([new("Value")], Enumerable.Range(0, 100000).Select(i => (i.ToString(), new[] { i.ToString() }))); var doc = Doc(grid, 200, 100); doc.BuildFrame();
            Check(grid.VisibleRowCount <= 4 && grid.Children.Count == 0); doc.Focus(grid); Key(doc, 35); Check(grid.SelectedKey == "99999" && grid.ScrollOffset > 0 && grid.VisibleRowCount <= 4);
            Key(doc, 36); Check(grid.SelectedKey == "0" && grid.ScrollOffset == 0); doc.Dispatch(new(EventKind.TextInput) { Text = "edit" }); Check(grid.GetRowKey(0) == "0");
        });
        test("Beta virtual collection shrink publishes effective selection and bound model", () =>
        {
            var list = new VirtualList { ItemCount = 5 }; var doc = Doc(list, 200, 100); doc.BuildFrame();
            var model = new ObservableValue<int>(99); using var scope = new BindingScope(); scope.BindSelection(list, model);
            Check(list.SelectedIndex == 4 && model.Value == 4); list.ScrollOffset = 10000;
            int changed = 0; list.SelectionChanged += value => { changed++; Check(value == list.SelectedIndex && list.ScrollOffset <= Math.Max(0, list.ItemCount * list.RowHeight - 100)); };
            list.ItemCount = 2; Check(list.SelectedIndex == 1 && model.Value == 1 && changed == 1);
            list.ItemCount = 0; Check(list.SelectedIndex == -1 && model.Value == -1 && list.ScrollOffset == 0 && changed == 2);
            list.ItemCount = 0; list.ItemCount = 3; Check(list.SelectedIndex == -1 && model.Value == -1 && changed == 2);
            Throws<ArgumentOutOfRangeException>(() => list.ItemCount = -1); Check(list.ItemCount == 3);
        });
        test("Beta virtual collection shrink attempts observers after failure and preserves reentry", () =>
        {
            var list = new VirtualList { ItemCount = 5 }; _ = Doc(list); bool fail = false, redirect = false;
            list.SelectionChanged += _ => { if (fail) throw new InvalidOperationException("observer"); };
            list.SelectionChanged += value => { if (redirect && value == -1) { list.ItemCount = 3; list.SelectedIndex = 2; } };
            var model = new ObservableValue<int>(4); using var scope = new BindingScope(); scope.BindSelection(list, model);
            fail = true; Throws<InvalidOperationException>(() => list.ItemCount = 2); Check(list.ItemCount == 2 && list.SelectedIndex == 1 && model.Value == 1);
            fail = false; redirect = true; list.ItemCount = 0; Check(list.ItemCount == 3 && list.SelectedIndex == 2 && model.Value == 2);
        });
    }

    private sealed class Probe : Element
    {
        public bool FailAttach, FailMeasure, FailPaint, FailSemantics;
        public int Loads, Unloads;
        public Probe() { Focusable = HitTestVisible = true; Role = "button"; }
        public override Vector2 Measure(Vector2 available) { if (FailMeasure) throw new InvalidOperationException("measure"); return base.Measure(available); }
        protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme) { if (FailPaint) throw new InvalidOperationException("paint"); base.Paint(list, bounds, opacity, theme); }
        public override string SemanticValue => FailSemantics ? throw new InvalidOperationException("semantics") : "probe";
        protected override void OnDocumentChanged(UiDocument? previous, UiDocument? current)
        { if (previous != null) Unloads++; if (current != null) { Loads++; if (FailAttach) throw new InvalidOperationException("attach"); } }
    }
}
