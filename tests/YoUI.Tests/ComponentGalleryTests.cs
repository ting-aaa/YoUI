using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using YoUI;
using YoUI.Components.Sample;
using YoUI.Showcase;

internal static class ComponentGalleryTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> require)
    {
        test("gallery composes the external assembly and current model snapshot", () =>
        {
            var state = new RatingState { Value = 4 };
            var gallery = new ComponentGallery(state);
            var doc = Document(gallery);
            Clean(doc);
            require(typeof(RatingControl).Assembly != typeof(Element).Assembly && typeof(RatingControl).Assembly != typeof(ComponentGallery).Assembly,
                "the gallery consumes the real external component assembly");
            require(ReferenceEquals(gallery.State, state) && gallery.Rating.Value == 4, "the supplied application model seeds the attached control");
            require(ModelLabel(gallery).Text.StartsWith("Model: 4 / 5"), "the visible model caption reflects the supplied state");
            require(gallery.Page == 0 && gallery.Navigation.Select(n => n.Selected).SequenceEqual(new[] { true, false, false, false }), "only Controls is initially selected");
            require(gallery.ShowPreview.Value && gallery.Preview.Visible && gallery.Opacity.Value == .8f && gallery.Preview.CompositedOpacity == .8f,
                "preview defaults agree across the composed controls");
            require(!gallery.ModalOpen && !gallery.DisabledButton.Enabled, "the modal starts closed and the disabled example stays disabled");
        });

        test("gallery pointer and keyboard input reach buttons toggle slider and theme", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery);
            Click(doc, Button(gallery, "Normal"));
            require(gallery.Status == "Normal button activated.", "a dispatched pointer click activates the normal button");
            int disabledClicks = 0; gallery.DisabledButton.Clicked += () => disabledClicks++;
            Click(doc, gallery.DisabledButton);
            require(disabledClicks == 0 && doc.Focused != gallery.DisabledButton, "a disabled control neither activates nor receives pointer focus");
            Click(doc, gallery.ThemeButton);
            require(doc.Theme == Theme.Light && gallery.ThemeButton.Text == "Dark theme", "pointer activation switches the theme and describes the next action");
            Key(doc, 13);
            require(doc.Theme == Theme.Dark && gallery.ThemeButton.Text == "Light theme", "Enter switches back through the same button contract");
            Click(doc, gallery.ShowPreview);
            require(!gallery.ShowPreview.Value && !gallery.Preview.Visible, "the toggle hides its composed preview");
            Key(doc, 32);
            require(gallery.ShowPreview.Value && gallery.Preview.Visible, "Space restores the preview");
            doc.UpdateLayout(); var bounds = gallery.Opacity.VisualBounds;
            Click(doc, new Vector2(bounds.X + 8 + (bounds.Width - 16) * .25f, bounds.Y + bounds.Height / 2));
            require(Near(gallery.Opacity.Value, .25f) && Near(gallery.Preview.CompositedOpacity, .25f), "slider pointer input updates the composited surface");
            Key(doc, 39);
            require(Near(gallery.Opacity.Value, .3f) && Near(gallery.Preview.CompositedOpacity, .3f), "slider keyboard input follows the same composition path");
            Click(doc, Button(gallery, "Primary"));
            require(gallery.ModalOpen && doc.Focused == gallery.CancelButton, "the primary control opens the real reset dialog");
            Key(doc, 27);
        });

        test("gallery Tab order excludes disabled controls and detached pages", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery);
            Element[] order = [.. gallery.Navigation, gallery.ThemeButton, gallery.ResetButton, Button(gallery, "Normal"), Button(gallery, "Primary"),
                gallery.ShowPreview, gallery.Opacity, gallery.Rating, Button(gallery, "Set model to 5")];
            doc.Focus(null);
            foreach (var expected in order)
            {
                Key(doc, 9);
                require(doc.Focused == expected, $"Tab reaches {expected.AccessibleName} in document order");
            }
            Key(doc, 9); require(doc.Focused == gallery.Navigation[0], "forward navigation wraps without visiting modal or detached page controls");
            Key(doc, 9, shift: true); require(doc.Focused == order[^1], "Shift+Tab wraps to the last active control");
            Click(doc, gallery.Navigation[1]);
            require(gallery.Page == 1 && gallery.Rating.Document == null && gallery.Search.Document == doc, "navigation really detaches the old page");
            doc.Focus(gallery.ResetButton); Key(doc, 9);
            require(doc.Focused == gallery.Search, "Tab enters the current page instead of an inactive control");
            Key(doc, 9); require(doc.Focused == gallery.Items, "virtual row buttons do not flood the focus order");
        });

        test("gallery rating synchronizes real input and detached-page model changes", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery);
            doc.UpdateLayout(); var bounds = gallery.Rating.VisualBounds;
            Click(doc, new Vector2(bounds.X + bounds.Width * .4f, bounds.Y + bounds.Height / 2));
            require(gallery.State.Value == 2 && gallery.Rating.Value == 2 && ModelLabel(gallery).Text.StartsWith("Model: 2 / 5"), "pointer input writes through to model and caption");
            Key(doc, 39); require(gallery.State.Value == 3, "Right increments the application model");
            Key(doc, 36); Key(doc, 37); require(gallery.State.Value == 0, "Home and Left honor the lower bound");
            Key(doc, 35); Key(doc, 39); require(gallery.State.Value == 5, "End and Right honor the upper bound");
            gallery.State.Value = 1;
            require(gallery.Rating.Value == 1, "programmatic model updates reach the attached component");
            Click(doc, Button(gallery, "Set model to 5"));
            require(gallery.State.Value == 5 && gallery.Rating.Value == 5, "the model demo button takes the same two-way path");
            var label = ModelLabel(gallery);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Click(doc, gallery.Navigation[1]);
                int detachedValue = gallery.Rating.Value;
                gallery.State.Value = 2;
                require(gallery.Rating.Value == detachedValue && label.Text.StartsWith("Model: 2 / 5"), "the detached page releases its component binding while the gallery observes its model");
                gallery.Rating.Value = 0;
                require(gallery.State.Value == 2, "detached component writes cannot corrupt the application model");
                Click(doc, gallery.Navigation[0]);
                require(gallery.Rating.Value == 2, "reattachment reads the newest model instead of a stale local value");
                int notifications = 0;
                void Changed(int _) => notifications++;
                gallery.Rating.Changed += Changed;
                gallery.State.Value = 4; gallery.State.Value = 4;
                require(notifications == 1 && gallery.Rating.Value == 4, "repeated navigation does not duplicate effective notifications");
                gallery.Rating.Changed -= Changed;
            }
        });

        test("gallery detachment releases model listeners and repeated attachment refreshes", () =>
        {
            var state = new RatingState { Value = 4 }; var gallery = new ComponentGallery(state);
            var root = new Element(); root.Add(gallery); var doc = Document(root);
            var label = ModelLabel(gallery);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                root.Remove(gallery); Clean(doc);
                state.Value = 1;
                require(gallery.Rating.Value == 4 && label.Text.StartsWith("Model: 4 / 5") && !doc.NeedsRender,
                    "neither component nor gallery label observes the external model while detached");
                gallery.Rating.Value = 2; require(state.Value == 1, "detached component has no model-write subscription");
                root.Add(gallery);
                require(gallery.Rating.Value == 1 && label.Text.StartsWith("Model: 1 / 5"), "a repeat attachment refreshes both model views");
                state.Value = 4;
            }
            var weak = DetachedGallery(state);
            for (int attempt = 0; attempt < 3 && weak.IsAlive; attempt++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            require(!weak.IsAlive, "a long-lived model must not retain a detached gallery or component");
            GC.KeepAlive(state);
        });

        test("gallery capture cancels before navigation and cannot affect a reattached page", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery);
            doc.UpdateLayout(); var bounds = gallery.Rating.VisualBounds;
            int cancellations = 0;
            gallery.Rating.Event += e => { if (e.Kind == EventKind.PointerCancel && e.Phase == EventPhase.Target) cancellations++; };
            Pointer(doc, EventKind.PointerDown, new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
            Pointer(doc, EventKind.PointerMove, new(1270, 10));
            require(gallery.State.Value == 5, "captured dragging updates the component beyond its bounds");
            doc.CancelPointer();
            Pointer(doc, EventKind.PointerMove, new(bounds.X, bounds.Y + 4));
            require(cancellations == 1 && gallery.State.Value == 5, "explicit cancellation ends the drag");
            Pointer(doc, EventKind.PointerDown, new(bounds.X + bounds.Width * .4f, bounds.Y + bounds.Height / 2));
            doc.Focus(gallery.Navigation[1]); Key(doc, 13);
            require(gallery.Page == 1 && cancellations == 2 && gallery.Rating.Document == null && doc.Focused == gallery.Navigation[1],
                "keyboard navigation cancels captured input before detaching the page");
            Pointer(doc, EventKind.PointerUp, new(bounds.Right, bounds.Y + 4));
            Click(doc, gallery.Navigation[0]);
            Pointer(doc, EventKind.PointerMove, new(bounds.Right, bounds.Y + 4));
            require(gallery.State.Value == 2, "neither a late release nor a subsequent move replays the canceled drag");
        });

        test("gallery interrupted attachment does not subscribe a detached model view", () =>
        {
            var state = new RatingState { Value = 3 }; var gallery = new ComponentGallery(state);
            var root = new Element(); var doc = Document(root);
            gallery.Rating.Changed += _ => root.Remove(gallery);
            root.Add(gallery);
            require(gallery.Document == null && gallery.Rating.Document == null, "a component callback can detach the gallery during its initial model synchronization");
            var caption = Descendants(gallery).OfType<Label>().Single(label => label.Text.StartsWith("Model:") || label.Text == "");
            string before = caption.Text;
            Clean(doc); state.Value = 4;
            require(caption.Text == before && gallery.Rating.Value == 3 && !doc.NeedsRender,
                "an interrupted attachment must not leave a gallery listener observing the external model");
            gallery.Rating.Value = 2;
            require(state.Value == 4, "an interrupted attachment cannot leave a component model-write listener");
        });

        test("gallery repeated modal open cancel and Escape preserve state and restore focus", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery);
            gallery.State.Value = 5;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Click(doc, gallery.ResetButton);
                require(gallery.ModalOpen && doc.Focused == gallery.CancelButton, "opening gives focus to Cancel");
                gallery.OpenReset(); gallery.OpenReset();
                Key(doc, 9); require(doc.Focused == gallery.ConfirmButton, "Tab stays inside the dialog");
                Key(doc, 9); require(doc.Focused == gallery.CancelButton, "modal focus wraps inside the dialog");
                Key(doc, 9, shift: true); require(doc.Focused == gallery.ConfirmButton, "reverse Tab remains trapped in the dialog");
                Key(doc, 27);
                require(!gallery.ModalOpen && doc.Focused == gallery.ResetButton && gallery.State.Value == 5,
                    "Escape restores the original opener, including after redundant opens, without resetting state");
                Click(doc, gallery.ResetButton); Click(doc, gallery.CancelButton);
                require(!gallery.ModalOpen && doc.Focused == gallery.ResetButton && gallery.State.Value == 5, "pointer Cancel shares the same non-destructive close path");
            }
        });

        test("gallery modal blocks the background and Escape still works after a backdrop click", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery);
            Click(doc, gallery.ResetButton); Click(doc, gallery.ThemeButton);
            require(doc.Theme == Theme.Dark && gallery.ModalOpen, "the backdrop prevents activating the theme behind the dialog");
            Key(doc, 27);
            require(!gallery.ModalOpen && doc.Focused == gallery.ResetButton, "Escape can close the modal after a background click and restores its opener");
        });

        test("gallery navigation and detachment clean up an open modal", () =>
        {
            var root = new Element(); var gallery = root.Add(new ComponentGallery()); var doc = Document(root);
            Click(doc, gallery.ResetButton); gallery.SelectPage(1);
            require(!gallery.ModalOpen && gallery.Page == 1 && doc.Focused == gallery.Navigation[1], "programmatic navigation removes modality before selecting a new page");
            Click(doc, gallery.ResetButton); root.Remove(gallery); root.Add(gallery);
            require(!gallery.ModalOpen, "a reattached gallery does not revive an abandoned reset dialog");
            Click(doc, gallery.ThemeButton);
            require(doc.Theme == Theme.Light, "detaching the modal removes its input restriction from the document");
            Click(doc, gallery.ResetButton); Key(doc, 27);
            require(doc.Focused == gallery.ResetButton, "modal focus restoration remains correct after gallery reattachment");
        });

        test("gallery filtering preserves source identity and rebinds equal-count results", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery);
            Click(doc, gallery.Navigation[1]); doc.UpdateLayout();
            var list = gallery.Items.VisualBounds;
            Click(doc, new Vector2(list.X + 30, list.Y + gallery.Items.RowHeight * 4 + 20));
            require(gallery.Items.SelectedIndex == 4 && gallery.SelectedRecord == 4 && gallery.SelectionText.Contains("005 / Aurora"), "pointer selection stores source record identity");
            Replace(doc, gallery.Search, "aUrOrA"); doc.UpdateLayout();
            require(gallery.Items.ItemCount == 60 && gallery.Items.SelectedIndex == 1 && gallery.SelectedRecord == 4, "case-insensitive filtering keeps the selected source record at its new visible index");
            Replace(doc, gallery.Search, "005"); doc.UpdateLayout();
            require(gallery.Items.ItemCount == 1 && gallery.Items.SelectedIndex == 0 && gallery.SelectedRecord == 4, "a narrowed one-record result keeps the same selection");
            Replace(doc, gallery.Search, "Aurora"); doc.UpdateLayout();
            require(gallery.Items.Children.OfType<Button>().First().Text == "001 / Aurora", "the realized rows describe Aurora records");
            Replace(doc, gallery.Search, "Orbit"); doc.UpdateLayout();
            require(gallery.Items.ItemCount == 60 && gallery.SelectedRecord == -1 && gallery.Items.SelectedIndex == -1, "changing to an equal-sized result set clears a removed identity");
            require(gallery.Items.Children.OfType<Button>().First().Text == "002 / Orbit" && gallery.SelectionText == "No record selected. 60 results.",
                "equal item counts still refresh virtual row captions and the selection summary");
            Replace(doc, gallery.Search, "no-such-gallery-record"); doc.UpdateLayout();
            require(gallery.Items.ItemCount == 0 && gallery.Items.RealizedCount == 0 && gallery.SelectedRecord == -1 && gallery.Items.ScrollOffset == 0,
                "empty results release realized rows, selection and scroll position");
            Replace(doc, gallery.Search, ""); doc.UpdateLayout();
            require(gallery.Items.ItemCount == 240 && gallery.SelectedRecord == -1 && gallery.Items.Children.OfType<Button>().First().Text == "001 / Aurora",
                "clearing the query repopulates the unselected source list");
        });

        test("gallery collection keyboard selection and filtering retain the selected source", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery);
            Click(doc, gallery.Navigation[1]); doc.Focus(gallery.Search); Key(doc, 9); Key(doc, 35); doc.UpdateLayout();
            require(doc.Focused == gallery.Items && gallery.SelectedRecord == 239 && gallery.Items.SelectedIndex == 239 && gallery.Items.ScrollOffset > 0,
                "End selects and reveals the last virtual record");
            Replace(doc, gallery.Search, "Tide"); doc.UpdateLayout();
            require(gallery.SelectedRecord == 239 && gallery.Items.SelectedIndex == 59 && gallery.Items.ScrollOffset == 0 && gallery.SelectionText.Contains("240 / Tide"),
                "filtering resets the viewport without confusing visible position with source identity");
            doc.Focus(gallery.Items); Key(doc, 36); require(gallery.SelectedRecord == 3, "Home selects the first filtered source record");
            Key(doc, 40); require(gallery.SelectedRecord == 7, "Down follows the filtered source mapping");
            Key(doc, 38); require(gallery.SelectedRecord == 3, "Up follows the same mapping");
            Click(doc, gallery.Navigation[0]); Click(doc, gallery.Navigation[1]);
            require(gallery.Search.Text == "Tide" && gallery.SelectedRecord == 3 && gallery.Items.SelectedIndex == 0, "page navigation preserves collection state");
        });

        test("gallery property editors commit invariant values on Enter and blur", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery);
            Click(doc, gallery.Navigation[2]);
            var gap = Field(gallery, "Gap"); var padding = Field(gallery, "Padding");
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Replace(doc, gap, "8.5");
                require(gallery.LayoutPreview.Gap == 12, "typing alone does not mutate the layout");
                Key(doc, 13); doc.UpdateLayout();
                require(gallery.LayoutPreview.Gap == 8.5f && gap.Text == "8.5", "Enter commits an invariant decimal independent of UI culture");
                Replace(doc, padding, "32"); Key(doc, 9); doc.UpdateLayout();
                require(gallery.LayoutPreview.Padding == 32 && padding.Text == "32", "leaving the field commits the upper boundary");
                Replace(doc, padding, "0"); Key(doc, 13); doc.UpdateLayout();
                require(gallery.LayoutPreview.Padding == 0, "the lower spacing boundary is valid");
                var first = gallery.LayoutPreview.Children[0].Bounds; var second = gallery.LayoutPreview.Children[1].Bounds;
                require(Near(second.X - first.Right, 8.5f) && Near(first.X, gallery.LayoutPreview.Bounds.X), "committed spacing changes actual child geometry");
            }
            finally { CultureInfo.CurrentCulture = culture; }
        });

        test("gallery property editors reject malformed nonfinite and out-of-range commits", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery);
            Click(doc, gallery.Navigation[2]); var gap = Field(gallery, "Gap");
            int failures = 0; gallery.Properties.ValidationFailed += _ => failures++;
            string[] invalid = ["-1", "33", "NaN", "Infinity", "1e100", "1,5", "not a number", ""];
            for (int i = 0; i < invalid.Length; i++)
            {
                Replace(doc, gap, invalid[i]); Key(doc, 13);
                require(gallery.LayoutPreview.Gap == 12 && gap.Text == "12", $"invalid spacing '{invalid[i]}' leaves the layout unchanged and restores its displayed value");
                require(failures == i + 1 && gallery.Status.StartsWith("Gap:"), "one failed commit reports a field-specific validation message");
            }
            Replace(doc, gap, "99"); Key(doc, 9);
            require(failures == invalid.Length + 1 && gallery.LayoutPreview.Gap == 12 && gap.Text == "12", "blur uses the same validation and rollback contract");
            Replace(doc, gap, "16"); Key(doc, 13);
            require(gallery.LayoutPreview.Gap == 16 && failures == invalid.Length + 1, "a valid edit still succeeds after rejected input");
        });

        test("gallery reset restores controls collection and detached property editors", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery);
            Click(doc, gallery.Navigation[2]); var gap = Field(gallery, "Gap"); var padding = Field(gallery, "Padding");
            Replace(doc, gap, "24"); Key(doc, 13); Replace(doc, padding, "6"); Key(doc, 13);
            Click(doc, gallery.Navigation[1]); Replace(doc, gallery.Search, "Orbit"); doc.Focus(gallery.Items); Key(doc, 35);
            Click(doc, gallery.Navigation[0]); Click(doc, gallery.ShowPreview); gallery.Opacity.Value = .2f; gallery.State.Value = 5;
            Click(doc, gallery.ResetButton); Key(doc, 9); Key(doc, 13);
            require(!gallery.ModalOpen && doc.Focused == gallery.ResetButton && gallery.Status == "Demos reset.", "confirm resets once and restores the opener");
            require(gallery.State.Value == 3 && gallery.Rating.Value == 3 && gallery.ShowPreview.Value && gallery.Preview.Visible && gallery.Opacity.Value == .8f && gallery.Preview.CompositedOpacity == .8f,
                "reset synchronizes the application model, controls and composed preview");
            require(gallery.Search.Text == "" && gallery.Items.ItemCount == 240 && gallery.SelectedRecord == -1 && gallery.Items.SelectedIndex == -1 && gallery.Items.ScrollOffset == 0,
                "reset clears a selected filtered collection even while that page is detached");
            require(gallery.LayoutPreview.Gap == 12 && gallery.LayoutPreview.Padding == 12 && gap.Text == "12" && padding.Text == "12",
                "reset refreshes both detached property fields and their target layout");
            Click(doc, gallery.Navigation[1]); doc.UpdateLayout();
            require(gallery.Items.Children.OfType<Button>().First().Text == "001 / Aurora", "reset rebinding survives collection reattachment");
        });

        test("gallery rendering controls preserve state cancel capture and reset across navigation", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery);
            Click(doc, gallery.Navigation[3]); Clean(doc);
            require(gallery.Rendering.Document == doc && gallery.Rating.Document == null, "the rendering page is attached without GPU construction");
            require(gallery.Rendering.Clipped && gallery.Rendering.GroupOpacity == .65f, "rendering defaults match their controls");
            Click(doc, gallery.RenderingClip); Key(doc, 32); Key(doc, 32);
            require(!gallery.Rendering.Clipped && !gallery.RenderingClip.Primary, "pointer and keyboard toggle the clip consistently");
            doc.Focus(gallery.RenderingOpacity); Key(doc, 39);
            require(Near(gallery.Rendering.GroupOpacity, .70f) && Near(gallery.RenderingOpacity.Value, .70f), "keyboard opacity updates the painter");
            doc.UpdateLayout(); var bounds = gallery.RenderingOpacity.VisualBounds;
            int cancellations = 0;
            gallery.RenderingOpacity.Event += e => { if (e.Kind == EventKind.PointerCancel && e.Phase == EventPhase.Target) cancellations++; };
            Pointer(doc, EventKind.PointerDown, new(bounds.X + 8 + (bounds.Width - 16) * .3f, bounds.Y + bounds.Height / 2));
            float before = gallery.RenderingOpacity.Value;
            gallery.SelectPage(0); Pointer(doc, EventKind.PointerMove, new(bounds.Right, bounds.Y + 8));
            Pointer(doc, EventKind.PointerUp, new(bounds.Right, bounds.Y + 8));
            require(cancellations == 1 && gallery.RenderingOpacity.Value == before, "navigation cancels the rendering slider capture before detachment");
            gallery.SelectPage(3); Clean(doc);
            require(!gallery.Rendering.Clipped && gallery.Rendering.GroupOpacity == before, "page reentry preserves both rendering settings");
            Click(doc, gallery.ResetButton); Key(doc, 27);
            require(!gallery.Rendering.Clipped && gallery.Rendering.GroupOpacity == before && doc.Focused == gallery.ResetButton,
                "Cancel preserves rendering values and restores focus");
            gallery.SelectPage(0); Click(doc, gallery.ResetButton); Click(doc, gallery.ConfirmButton);
            gallery.SelectPage(3); Clean(doc);
            require(gallery.Rendering.Clipped && gallery.RenderingClip.Value && gallery.RenderingClip.Primary && gallery.Rendering.GroupOpacity == .65f,
                "Reset synchronizes the detached rendering controls and painter");
            Click(doc, gallery.ThemeButton); Clean(doc);
            require(doc.Theme == Theme.Light && gallery.Rendering.Document == doc, "theme change keeps the active renderer-free sample usable");
        });

        test("gallery compact layout keeps navigation usable and pages scrollable", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery, 640, 600);
            foreach (var navigation in gallery.Navigation)
            {
                Click(doc, navigation); Clean(doc);
                require(doc.PendingUpdates == DirtyFlags.None, "each compact page settles without perpetual layout or paint invalidation");
                foreach (var button in gallery.Navigation.Append(gallery.ThemeButton).Append(gallery.ResetButton))
                    require(button.Bounds.Width > 0 && button.Bounds.X >= 0 && button.Bounds.Right <= 640 && button.Bounds.Bottom < 210, "all top-level actions stay within the compact viewport");
                var viewport = PageViewport(gallery);
                require(viewport.Bounds == new Rect(24, 210, 592, 340) && viewport.ClipToBounds, "the page uses the bounded scrolling surface");
                require(viewport.Content.Bounds.Height < 1000, "a viewport control inside a StackPanel must not consume its unconstrained measurement height");
                if (gallery.Page == 2) require(gallery.Properties.Bounds.Height == 230, "the nested property grid respects its fixed viewport height");
                foreach (var element in Descendants(viewport))
                    require(Finite(element.Bounds), "compact page layout produces finite, nonnegative bounds");
                Pointer(doc, EventKind.Scroll, new(viewport.Bounds.Right - 7, viewport.Bounds.Y + 30), delta: -3);
                require(viewport.ScrollOffset > 0, "a dispatched wheel event scrolls overflowing compact page content");
            }
        });

        test("gallery compact keyboard focus reveals clipped controls on every page", () =>
        {
            var gallery = new ComponentGallery(); var doc = Document(gallery, 640, 600);
            for (int page = 0; page < gallery.Navigation.Length; page++)
            {
                Click(doc, gallery.Navigation[page]); doc.UpdateLayout();
                var viewport = PageViewport(gallery);
                var focusable = Descendants(viewport).Where(e => e.Focusable && e.Enabled).ToArray();
                bool needsReveal = focusable.Any(e => e.VisualBounds.Bottom > viewport.VisualBounds.Bottom);
                doc.Focus(gallery.ResetButton);
                foreach (var expected in focusable)
                {
                    Key(doc, 9); doc.UpdateLayout();
                    require(doc.Focused == expected, $"compact page {page} reaches {expected.AccessibleName} through Tab");
                    var bounds = expected.VisualBounds; var clip = viewport.VisualBounds;
                    require(bounds.Y >= clip.Y - .1f && bounds.Bottom <= clip.Bottom + .1f,
                        $"compact page {page} reveals focused '{expected.AccessibleName}' ({bounds.Y}..{bounds.Bottom}) inside {clip.Y}..{clip.Bottom}");
                }
                require(!needsReveal || viewport.ScrollOffset > 0, $"keyboard navigation reveals controls below compact page {page} viewport");
            }
        });
    }

    private static UiDocument Document(Element root, float width = 1280, float height = 1000) => new(root, new TestMeasurer(), width, height);
    private static bool Near(float a, float b) => Math.Abs(a - b) < .001f;
    private static bool Finite(Rect rect) => float.IsFinite(rect.X) && float.IsFinite(rect.Y) && float.IsFinite(rect.Width) && float.IsFinite(rect.Height) && rect.Width >= 0 && rect.Height >= 0;
    private static void Key(UiDocument doc, int key, bool shift = false, bool control = false) => doc.Dispatch(new(EventKind.KeyDown) { Key = key, Shift = shift, Control = control });
    private static void Pointer(UiDocument doc, EventKind kind, Vector2 position, float delta = 0) => doc.Dispatch(new(kind) { Position = position, Delta = delta });
    private static void Click(UiDocument doc, Vector2 position) { Pointer(doc, EventKind.PointerDown, position); Pointer(doc, EventKind.PointerUp, position); }
    private static void Click(UiDocument doc, Element element)
    {
        doc.UpdateLayout(); var bounds = element.VisualBounds;
        Click(doc, new Vector2(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
    }
    private static void Replace(UiDocument doc, TextBox field, string text)
    {
        doc.Focus(field); Key(doc, 65, control: true);
        // A single edit avoids a transient empty query that would itself change filtering.
        if (text.Length == 0) Key(doc, 8); else doc.Dispatch(new(EventKind.TextInput) { Text = text });
    }
    private static IEnumerable<Element> Descendants(Element root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static Button Button(ComponentGallery gallery, string text) => Descendants(gallery).OfType<Button>().Single(b => b.Text == text);
    private static TextBox Field(ComponentGallery gallery, string name) => Descendants(gallery.Properties).OfType<TextBox>().Single(f => f.AccessibleName == name);
    private static Label ModelLabel(ComponentGallery gallery) => Descendants(gallery).OfType<Label>().Single(label => label.Text.StartsWith("Model:"));
    private static ScrollView PageViewport(ComponentGallery gallery) => Descendants(gallery).OfType<ScrollView>().First();
    private static void Clean(UiDocument doc)
    {
        if (!doc.UpdateLayout()) throw new Exception("Gallery fixture layout did not settle.");
        for (int pass = 0; pass < 8 && doc.NeedsRender; pass++) doc.BuildFrame();
        doc.Semantics();
        if (doc.PendingUpdates != DirtyFlags.None) throw new Exception("Gallery fixture did not become clean.");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DetachedGallery(RatingState state)
    {
        var root = new Element(); var gallery = root.Add(new ComponentGallery(state)); _ = Document(root);
        root.Remove(gallery); return new WeakReference(gallery);
    }
}
