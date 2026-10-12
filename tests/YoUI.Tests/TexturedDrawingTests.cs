using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using YoUI;

internal static class TexturedDrawingTests
{
    // A zero-handle owner allows disposal without loading native code or requiring a GPU.
    private static NativeImage Image(uint id = 41, uint width = 100, uint height = 80) =>
        (NativeImage)Activator.CreateInstance(typeof(NativeImage), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [RuntimeHelpers.GetUninitializedObject(typeof(NativeRenderer)), id, width, height], culture: null)!;
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
    internal static void Run(Action<string, Action> test, Action<bool, string> require)
    {
        void Check(bool value) => require(value, "Textured drawing assertion failed");
        test("textured triangle encodes image-local UVs and straight-alpha tint", () =>
        {
            using var image = Image(); var list = new DrawList(); var tint = new Color(.8f, .4f, .2f, .25f);
            list.TexturedTriangle(new(2, 3), new(8, 5), new(4, 9), new(.1f, .2f), new(.9f, .3f), new(.6f, .8f), image, tint);
            var c = list.Commands[0];
            Check(c.Kind == 8 && c.Flags == 41 && c.Rect == new Rect(2, 3, 8, 5));
            Check(c.Reserved0 == 4 && c.Reserved1 == 9 && c.Color == tint);
            Check(c.Color2 == new Color(.1f, .2f, .9f, .3f) && c.Radius == .6f && c.FontSize == .8f);
            Check(c.TextOffset == 0 && c.TextLength == 0 && image.Width == 100 && image.Height == 80);
        });
        test("indexed textured mesh preserves triangle and painter order", () =>
        {
            using var image = Image(); var list = new DrawList();
            Vector2[] p = [new(0, 0), new(10, 0), new(10, 20), new(0, 20)];
            Vector2[] uv = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
            list.Rectangle(new(0, 0, 1, 1), Color.Transparent);
            list.TexturedMesh(p, uv, [2, 3, 0, 0, 1, 2], image); list.PushClip(new(0, 0, 30, 30));
            Check(list.Commands.Length == 4 && list.Commands[0].Kind == 0 && list.Commands[3].Kind == 2);
            Check(list.Commands[1].Rect == new Rect(10, 20, 0, 20) && list.Commands[1].Reserved0 == 0);
            Check(list.Commands[2].Rect == new Rect(0, 0, 10, 0) && list.Commands[2].Reserved1 == 20);
            Check(list.Commands[1].Color == new Color(1, 1, 1));
            list.TexturedMesh([], [], [], image); Check(list.Commands.Length == 4);
        });
        test("malformed meshes reject atomically including late and unused vertices", () =>
        {
            using var image = Image(); var list = new DrawList(); list.Rectangle(new(0, 0, 1, 1), Color.Transparent);
            Vector2[] p = [new(0, 0), new(10, 0), new(10, 20), new(0, 20)];
            Vector2[] uv = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
            Reject<ArgumentException>(() => list.TexturedMesh(p, uv[..3], [0, 1, 2], image));
            Reject<ArgumentException>(() => list.TexturedMesh(p, uv, [0, 1, 2, 0], image));
            Reject<ArgumentOutOfRangeException>(() => list.TexturedMesh(p, uv, [0, 1, 2, 0, 1, 4], image));
            Reject<ArgumentOutOfRangeException>(() => list.TexturedMesh(p, uv, [0, 1, 2, 0, -1, 2], image));
            uv[3] = new(float.NaN, 1); Reject<ArgumentOutOfRangeException>(() => list.TexturedMesh(p, uv, [0, 1, 2], image));
            uv[3] = new(0, 1); p[3] = new(0, float.PositiveInfinity);
            Reject<ArgumentOutOfRangeException>(() => list.TexturedMesh(p, uv, [0, 1, 2], image));
            Check(list.Commands.Length == 1);
        });
        test("textured helpers reject missing released images and invalid triangle values", () =>
        {
            using var image = Image(); var list = new DrawList();
            void Triangle(NativeImage value, Vector2 p = default, Vector2 uv = default, Color? tint = null) =>
                list.TexturedTriangle(p, Vector2.One, Vector2.UnitY, uv, Vector2.UnitX, Vector2.UnitY, value, tint);
            Reject<ArgumentNullException>(() => Triangle(null!));
            Reject<ArgumentOutOfRangeException>(() => Triangle(image, new(float.NaN, 0)));
            Reject<ArgumentOutOfRangeException>(() => Triangle(image, uv: new(1.01f, 0)));
            Reject<ArgumentOutOfRangeException>(() => Triangle(image, uv: new(0, -.01f)));
            Reject<ArgumentOutOfRangeException>(() => Triangle(image, uv: new(float.PositiveInfinity, 0)));
            Reject<ArgumentOutOfRangeException>(() => Triangle(image, tint: new Color(1, 1, 1, float.NaN)));
            image.Dispose();
            Reject<ObjectDisposedException>(() => Triangle(image));
            Reject<ObjectDisposedException>(() => list.TexturedMesh([], [], [], image));
            Reject<ObjectDisposedException>(() => list.NineSlice(new(0, 0, 10, 10), image, 1, 1, 1, 1));
            Reject<ArgumentNullException>(() => list.TexturedMesh([], [], [], null!));
            Reject<ArgumentNullException>(() => list.NineSlice(new(0, 0, 10, 10), null!, 1, 1, 1, 1));
            Check(list.Commands.Length == 0 && image.Width == 100 && image.Height == 80);
        });
        test("textured helpers atomically reject each out-of-range tint component", () =>
        {
            using var image = Image(); var list = new DrawList(); list.Rectangle(new(0, 0, 1, 1), Color.Transparent);
            Color[] invalid = [new(-.01f, 1, 1), new(1.01f, 1, 1), new(1, -.01f, 1), new(1, 1.01f, 1),
                new(1, 1, -.01f), new(1, 1, 1.01f), new(1, 1, 1, -.01f), new(1, 1, 1, 1.01f)];
            foreach (var tint in invalid)
            {
                Reject<ArgumentOutOfRangeException>(() => list.TexturedTriangle(Vector2.Zero, Vector2.UnitX, Vector2.UnitY,
                    Vector2.Zero, Vector2.UnitX, Vector2.UnitY, image, tint));
                Reject<ArgumentOutOfRangeException>(() => list.TexturedMesh([Vector2.Zero, Vector2.UnitX, Vector2.UnitY],
                    [Vector2.Zero, Vector2.UnitX, Vector2.UnitY], [0, 1, 2], image, tint));
                Reject<ArgumentOutOfRangeException>(() => list.NineSlice(new(0, 0, 200, 160), image, 10, 20, 30, 10, tint));
                Check(list.Commands.Length == 1);
            }
        });
        test("append and copy scale textured positions without changing any UV or tint", () =>
        {
            using var image = Image(); var source = new DrawList(); var target = new DrawList();
            source.PushClip(new(1, 2, 20, 30)); source.BeginLayer(.5f);
            source.TexturedTriangle(new(2, 3), new(8, 5), new(4, 9), new(.1f, .2f), new(.9f, .3f), new(.6f, .8f), image);
            source.Triangle(new(2, 3), new(8, 5), new(4, 9), new(1, 0, 0)); source.EndLayer(); source.PopClip();
            target.Append(source, new(10, 20), 2);
            var original = source.Commands[2]; var c = target.Commands[2];
            Check(c.Rect == new Rect(14, 26, 26, 30) && c.Reserved0 == 18 && c.Reserved1 == 38);
            Check(c.Color2 == original.Color2 && c.Color == original.Color && c.Radius == original.Radius && c.FontSize == original.FontSize && c.Flags == original.Flags);
            Check(target.Commands[3].Rect == c.Rect && target.Commands[3].Reserved0 == c.Reserved0);
            Check(target.Commands[0].Rect == new Rect(12, 24, 40, 60) && target.Commands[4].Kind == 5 && target.Commands[5].Kind == 3);
            source.CopyScaledTo(target, 3); c = target.Commands[2];
            Check(target.Commands.Length == 6 && c.Rect == new Rect(6, 9, 24, 15) && c.Reserved0 == 12 && c.Reserved1 == 27);
            Check(c.Color2 == original.Color2 && c.Radius == .6f && c.FontSize == .8f);
        });
        test("mixed text and textured geometry retain independent flags and UVs through transforms", () =>
        {
            using var image = Image(); var source = new DrawList(); var target = new DrawList();
            source.Text(new(1, 2, 40, 20), "office אבג", new(1, 1, 1), 12, bold: true, wrap: false, clip: false);
            source.NineSlice(new(5, 7, 200, 160), image, 10, 20, 30, 10);
            target.Text(new(0, 0, 1, 1), "prefix", new(1, 1, 1));
            target.Append(source, new(10, 20), 2);
            Check(target.Commands.Length == 20 && target.Commands[1].Flags == 7);
            Check(target.Commands[1].Rect == new Rect(12, 24, 80, 40) && target.Commands[1].FontSize == 24);
            Check(System.Text.Encoding.UTF8.GetString(target.TextBytes.Slice((int)target.Commands[1].TextOffset, (int)target.Commands[1].TextLength)) == "office אבג");
            for (int i = 1; i < source.Commands.Length; i++)
            {
                var original = source.Commands[i]; var transformed = target.Commands[i + 1];
                Check(transformed.Flags == 41 && transformed.Color2 == original.Color2 && transformed.Radius == original.Radius && transformed.FontSize == original.FontSize);
                Check(transformed.Reserved0 == original.Reserved0 * 2 + 10 && transformed.Reserved1 == original.Reserved1 * 2 + 20);
            }
            source.CopyScaledTo(target, 3);
            Check(target.Commands.Length == 19 && target.Commands[0].Flags == 7 && target.Commands[0].FontSize == 36);
            Check(target.Commands[1].Color2 == source.Commands[1].Color2 && target.Commands[1].FontSize == source.Commands[1].FontSize);
        });
        test("nine-slice uses source pixel borders and emits row-major triangles", () =>
        {
            using var image = Image(); var list = new DrawList(); var tint = new Color(.2f, .4f, .6f, .5f);
            list.NineSlice(new(5, 7, 200, 160), image, 10, 20, 30, 10, tint); Check(list.Commands.Length == 18);
            var first = list.Commands[0];
            Check(first.Rect == new Rect(5, 7, 15, 7) && first.Reserved0 == 15 && first.Reserved1 == 27);
            Check(first.Color2 == new Color(0, 0, .1f, 0) && first.Radius == .1f && first.FontSize == .25f);
            var center = list.Commands[8];
            Check(center.Rect == new Rect(15, 27, 175, 27) && center.Reserved1 == 157);
            Check(center.Color2 == new Color(.1f, .25f, .7f, .25f) && center.Radius == .7f && center.FontSize == .875f);
            foreach (var c in list.Commands) Check(c.Kind == 8 && c.Flags == 41 && c.Color == tint);
        });
        test("nine-slice proportionally shrinks opposite borders and handles zero extents", () =>
        {
            using var image = Image(); var list = new DrawList();
            list.NineSlice(new(0, 0, 20, 15), image, 10, 20, 30, 10); Check(list.Commands.Length == 8);
            Check(list.Commands[0].Rect == new Rect(0, 0, 5, 0) && list.Commands[0].Reserved1 == 10);
            Check(list.Commands[2].Rect == new Rect(5, 0, 20, 0));
            Check(list.Commands[4].Rect == new Rect(0, 10, 5, 10) && list.Commands[4].Reserved1 == 15);
            list.Clear(); list.NineSlice(new(0, 0, 0, 15), image, 10, 20, 30, 10); Check(list.Commands.Length == 0);
            list.NineSlice(new(0, 0, 20, 0), image, 10, 20, 30, 10); Check(list.Commands.Length == 0);
            list.NineSlice(new(0, 0, 20, 15), image, 0, 0, 0, 0); Check(list.Commands.Length == 2);
        });
        test("nine-slice omits collapsed source cells even for larger destinations", () =>
        {
            using var image = Image(); var list = new DrawList();
            list.NineSlice(new(0, 0, 200, 160), image, 30, 24, 70, 56); Check(list.Commands.Length == 8);
            Check(list.Commands[0].Rect == new Rect(0, 0, 30, 0)); Check(list.Commands[2].Rect == new Rect(130, 0, 200, 0));
            foreach (var c in list.Commands)
            {
                Check(c.Color2.R <= c.Color2.B && c.Color2.G <= c.Color2.A);
                Check(c.Color2.R <= c.Radius && c.Color2.G <= c.FontSize);
            }
            list.Clear(); list.NineSlice(new(0, 0, 200, 160), image, 100, 80, 0, 0); Check(list.Commands.Length == 2);
        });
        test("nine-slice rejects invalid rectangles borders and source overlap atomically", () =>
        {
            using var image = Image(); var list = new DrawList();
            Reject<ArgumentException>(() => list.NineSlice(new(0, 0, 10, 10), image, 60, 0, 41, 0));
            Reject<ArgumentException>(() => list.NineSlice(new(0, 0, 10, 10), image, 0, 41, 0, 40));
            Reject<ArgumentException>(() => list.NineSlice(new(0, 0, 10, 10), image, -1, 0, 0, 0));
            Reject<ArgumentException>(() => list.NineSlice(new(0, 0, 10, 10), image, 0, float.NaN, 0, 0));
            Reject<ArgumentException>(() => list.NineSlice(new(0, 0, 10, 10), image, 0, 0, float.PositiveInfinity, 0));
            Reject<ArgumentOutOfRangeException>(() => list.NineSlice(new(0, 0, -10, 10), image, 0, 0, 0, 0));
            Reject<ArgumentOutOfRangeException>(() => list.NineSlice(new(float.NaN, 0, 10, 10), image, 0, 0, 0, 0));
            Reject<ArgumentOutOfRangeException>(() => list.NineSlice(new(float.MaxValue, 0, float.MaxValue, 10), image, 0, 0, 0, 0));
            Reject<ArgumentOutOfRangeException>(() => list.NineSlice(new(0, 0, 10, 10), image, 0, 0, 0, 0, new Color(float.NaN, 1, 1)));
            using var empty = Image(width: 0); Reject<ArgumentException>(() => list.NineSlice(new(0, 0, 10, 10), empty, 0, 0, 0, 0));
            Check(list.Commands.Length == 0);
            list.NineSlice(new(0, 0, 100, 80), image, 50, 40, 50, 40); Check(list.Commands.Length == 8);
        });
    }
}
