using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GLauncher.Themes;

/// <summary>
/// Иконки изданий в боковой панели — свой пиксель-арт 16×16, нарисованный кодом (не файлы Mojang):
/// Java — изометрический блок дёрна, Bedrock — блок дёрна сбоку, Dungeons — булыжник с лавой и мхом,
/// Dungeons II — светящаяся синяя маска, Legends — синий кристалл. Показываются без сглаживания.
/// </summary>
public static class EditionIcons
{
    public static ImageSource Java { get; } = IsoGrassBlock();
    public static ImageSource Bedrock { get; } = PixelTextures.Get(PixelTextureKind.GrassBlock);
    public static ImageSource Dungeons { get; } = Draw(DungeonsPixel);
    public static ImageSource DungeonsII { get; } = Draw(DungeonsIIPixel);
    public static ImageSource Legends { get; } = Draw(LegendsPixel);

    private static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;

    /// <summary>Детерминированный «шум» для пиксельных текстур.</summary>
    private static int Noise(int x, int y, int seed) => (int)((uint)((x * 73856093) ^ (y * 19349663) ^ (seed * 83492791)) % 100);

    private static BitmapSource Draw(Func<int, int, uint> pixel)
    {
        var px = new uint[16 * 16];
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
            px[y * 16 + x] = pixel(x, y);
        var bmp = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, px, 16 * 4);
        bmp.Freeze();
        return bmp;
    }

    // ---------------- Dungeons: булыжник, мох сверху, трещины с лавой ----------------
    private static uint DungeonsPixel(int x, int y)
    {
        var n = Noise(x, y, 1);
        // мох по верхнему краю, свисает неровно
        var moss = y < 2 || (y == 2 && n < 55) || (y == 3 && n < 20);
        if (moss) return n < 40 ? Rgb(0x3E, 0x6B, 0x2A) : Rgb(0x55, 0x8A, 0x36);
        // лавовые трещины
        bool Lava(int cx, int cy) => cx == x && cy == y;
        (int, int)[] crack = [(8, 5), (9, 6), (8, 7), (7, 8), (8, 9), (4, 11), (5, 11), (5, 12), (10, 13), (11, 13), (11, 14), (12, 12)];
        foreach (var (cx, cy) in crack)
            if (Lava(cx, cy)) return (x + y) % 3 == 0 ? Rgb(0xFF, 0xD2, 0x4A) : Rgb(0xFF, 0x7A, 0x1A);
        // швы между камнями
        var seam = (y % 5 == 4) || ((x + (y / 5) * 3) % 6 == 5);
        if (seam) return Rgb(0x3A, 0x3A, 0x3E);
        return n switch
        {
            < 25 => Rgb(0x6A, 0x6A, 0x70),
            < 70 => Rgb(0x80, 0x80, 0x86),
            _ => Rgb(0x96, 0x96, 0x9C),
        };
    }

    // ---------------- Dungeons II: тёмно-синяя плитка и светящаяся маска ----------------
    private static uint DungeonsIIPixel(int x, int y)
    {
        var bg = (x + y) % 4 == 0 ? Rgb(0x0B, 0x16, 0x34) : Rgb(0x0E, 0x1C, 0x42);
        // голова-маска 10×11
        if (x < 3 || x > 12 || y < 2 || y > 13) return bg;
        var edge = x == 3 || x == 12 || y == 2 || y == 13;
        if (edge) return (x == 3 || x == 12) && (y == 2 || y == 13) ? bg : Rgb(0x2F, 0xB4, 0xE8);
        // глаза (светятся), рот
        if ((x is 5 or 6 || x is 9 or 10) && y is 6 or 7) return y == 6 ? Rgb(0xE8, 0xFB, 0xFF) : Rgb(0x8C, 0xEF, 0xFF);
        if (y == 10 && x >= 6 && x <= 9) return Rgb(0x8C, 0xEF, 0xFF);
        if (y == 11 && (x == 5 || x == 10)) return Rgb(0x8C, 0xEF, 0xFF);
        return Noise(x, y, 2) < 30 ? Rgb(0x1A, 0x4C, 0x9E) : Rgb(0x1E, 0x5B, 0xB8);
    }

    // ---------------- Legends: синий кристалл-ромб на тёмном ----------------
    private static uint LegendsPixel(int x, int y)
    {
        double cx = 7.5, cy = 7.5;
        var d = Math.Abs(x - cx) + Math.Abs(y - cy);
        if (d > 7.5) return (x + y) % 2 == 0 ? Rgb(0x0E, 0x16, 0x24) : Rgb(0x11, 0x1B, 0x2B);
        if (d > 6.5) return Rgb(0x15, 0x4E, 0xA8);                      // контур
        if (d < 1.6) return Rgb(0xF0, 0xFD, 0xFF);                      // сердцевина
        var upperLeft = x <= cx && y <= cy;
        var lowerRight = x > cx && y > cy;
        if (upperLeft) return d < 4 ? Rgb(0x9E, 0xE7, 0xFF) : Rgb(0x5C, 0xC4, 0xFF);
        if (lowerRight) return d < 4 ? Rgb(0x3F, 0xA9, 0xF5) : Rgb(0x1E, 0x6F, 0xD9);
        return d < 4 ? Rgb(0x6F, 0xD0, 0xFF) : Rgb(0x2E, 0x8C, 0xEB);
    }

    // ---------------- Java: изометрический блок дёрна из пиксельных граней ----------------
    private static BitmapSource GrassTop() => Draw((x, y) => Noise(x, y, 3) switch
    {
        < 20 => Rgb(0x5E, 0x9E, 0x3A),
        < 65 => Rgb(0x72, 0xB8, 0x48),
        _ => Rgb(0x86, 0xCC, 0x56),
    });

    private static ImageSource IsoGrassBlock()
    {
        var side = PixelTextures.Get(PixelTextureKind.GrassBlock);
        var top = GrassTop();
        const double w = 13.86, h = 8; // полуширина ромба и его полувысота (холст 32×32)

        ImageDrawing Face(ImageSource img) => new(img, new Rect(0, 0, 1, 1));
        GeometryDrawing Shade(byte alpha) => new(new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 0, 1, 1)));

        DrawingGroup Mapped(Matrix m, ImageSource img, byte shade)
        {
            var g = new DrawingGroup { Transform = new MatrixTransform(m) };
            g.Children.Add(Face(img));
            if (shade > 0) g.Children.Add(Shade(shade));
            return g;
        }

        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
        group.Children.Add(Mapped(new Matrix(w, h, -w, h, 16, 0), top, 0));          // верх
        group.Children.Add(Mapped(new Matrix(w, h, 0, 16, 16 - w, h), side, 0x30));  // левая грань
        group.Children.Add(Mapped(new Matrix(w, -h, 0, 16, 16, 2 * h), side, 0x60));  // правая грань
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
