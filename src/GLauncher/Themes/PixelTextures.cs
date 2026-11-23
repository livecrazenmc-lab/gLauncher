using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GLauncher.Themes;

public enum PixelTextureKind { Stone, Deepslate, Dirt, GrassBlock, Planks }

/// <summary>
/// Пиксельные текстуры 16×16, нарисованные кодом (свои, не из игры).
/// В XAML: Background="{themes:PixelTexture Kind=Stone, TileSize=64}".
/// </summary>
[MarkupExtensionReturnType(typeof(ImageBrush))]
public sealed class PixelTextureExtension : MarkupExtension
{
    public PixelTextureKind Kind { get; set; }

    /// <summary>Размер одного тайла на экране (16 пикселей текстуры растягиваются до него без сглаживания).</summary>
    public double TileSize { get; set; } = 64;

    public override object ProvideValue(IServiceProvider serviceProvider) => PixelTextures.CreateBrush(Kind, TileSize);
}

public static class PixelTextures
{
    private const int Size = 16;
    private static readonly Dictionary<PixelTextureKind, BitmapSource> Cache = [];

    public static ImageBrush CreateBrush(PixelTextureKind kind, double tileSize)
    {
        var brush = new ImageBrush(Get(kind))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tileSize, tileSize),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        return brush;
    }

    public static BitmapSource Get(PixelTextureKind kind)
    {
        if (Cache.TryGetValue(kind, out var cached)) return cached;

        var pixels = kind switch
        {
            PixelTextureKind.Stone => Stone(new Random(11), 0x34, 0x34, 0x3A, cracks: 7),
            PixelTextureKind.Deepslate => Deepslate(new Random(23)),
            PixelTextureKind.Dirt => Dirt(new Random(37)),
            PixelTextureKind.GrassBlock => GrassBlock(new Random(41)),
            PixelTextureKind.Planks => Planks(new Random(53)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var bitmap = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Bgra32, null, pixels, Size * 4);
        bitmap.Freeze();
        Cache[kind] = bitmap;
        return bitmap;
    }

    // --- Генераторы ---

    private static uint[] Stone(Random rnd, int r, int g, int b, int cracks)
    {
        var px = new uint[Size * Size];
        for (var i = 0; i < px.Length; i++)
        {
            var d = rnd.Next(-9, 10);
            px[i] = Rgb(r + d, g + d, b + d);
        }
        // Тёмные «трещинки» и светлые вкрапления — короткие отрезки по 2–4 пикселя.
        for (var c = 0; c < cracks; c++)
        {
            int x = rnd.Next(Size), y = rnd.Next(Size), len = rnd.Next(2, 5);
            var dark = rnd.Next(3) > 0;
            var horizontal = rnd.Next(2) == 0;
            for (var k = 0; k < len; k++)
            {
                var (xx, yy) = horizontal ? ((x + k) % Size, y) : (x, (y + k) % Size);
                var shade = dark ? -16 : 12;
                px[yy * Size + xx] = Rgb(r + shade, g + shade, b + shade + (dark ? 0 : 2));
            }
        }
        return px;
    }

    private static uint[] Deepslate(Random rnd)
    {
        var px = new uint[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            var row = rnd.Next(-4, 5); // горизонтальные слои
            for (var x = 0; x < Size; x++)
            {
                var d = row + rnd.Next(-6, 7);
                px[y * Size + x] = Rgb(0x26 + d, 0x26 + d, 0x2C + d);
            }
        }
        for (var c = 0; c < 9; c++)
        {
            int x = rnd.Next(Size), y = rnd.Next(Size), len = rnd.Next(3, 7);
            for (var k = 0; k < len; k++) px[y * Size + (x + k) % Size] = Rgb(0x1A, 0x1A, 0x1F);
        }
        return px;
    }

    private static uint[] Dirt(Random rnd)
    {
        var px = new uint[Size * Size];
        for (var i = 0; i < px.Length; i++) px[i] = DirtPixel(rnd);
        return px;
    }

    private static uint DirtPixel(Random rnd)
    {
        var roll = rnd.Next(10);
        var d = rnd.Next(-7, 8);
        return roll switch
        {
            0 => Rgb(0x4E + d, 0x35 + d, 0x24 + d),   // тёмные комочки
            1 => Rgb(0x8C + d, 0x68 + d, 0x4A + d),   // светлые песчинки
            _ => Rgb(0x6E + d, 0x4C + d, 0x33 + d),
        };
    }

    private static uint[] GrassBlock(Random rnd)
    {
        var px = Dirt(rnd);
        for (var x = 0; x < Size; x++)
        {
            // Трава: 3 ряда сверху и «свисающие» травинки разной длины.
            var depth = 3 + rnd.Next(0, 3) + (rnd.Next(5) == 0 ? 2 : 0);
            for (var y = 0; y < depth; y++)
            {
                var d = rnd.Next(-10, 11);
                var bottom = y == depth - 1;
                px[y * Size + x] = bottom
                    ? Rgb(0x3E + d, 0x6B + d, 0x22 + d)
                    : Rgb(0x5C + d, 0x96 + d, 0x34 + d);
            }
        }
        return px;
    }

    private static uint[] Planks(Random rnd)
    {
        var px = new uint[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            var board = y / 4;
            var seam = y % 4 == 3;
            var joint = board % 2 == 0 ? 5 : 12;
            for (var x = 0; x < Size; x++)
            {
                var d = rnd.Next(-6, 7);
                px[y * Size + x] = seam || x == joint
                    ? Rgb(0x4A + d / 2, 0x33 + d / 2, 0x1F)
                    : Rgb(0x8F + d, 0x6A + d, 0x40 + d);
            }
        }
        return px;
    }

    private static uint Rgb(int r, int g, int b) =>
        0xFF000000u | (uint)Math.Clamp(r, 0, 255) << 16 | (uint)Math.Clamp(g, 0, 255) << 8 | (uint)Math.Clamp(b, 0, 255);
}
