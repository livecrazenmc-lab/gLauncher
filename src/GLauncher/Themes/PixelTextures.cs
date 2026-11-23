using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GLauncher.Themes;

public enum PixelTextureKind
{
    // Шапка (блок сбоку: верхний слой + земля)
    GrassBlock, SunnyGrass, FlowerGrass, PastelGrass, AutumnGrass, SnowyGrass, GarlandGrass, CursedGrass,
    // Фон
    Stone, Deepslate, MossyStone, FrozenStone, SpeckledStone, Dirt, Planks, DarkPlanks, DarkBricks,
}

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

        var rnd = new Random(1000 + (int)kind * 17);
        var pixels = kind switch
        {
            PixelTextureKind.GrassBlock => TopBlock(rnd, (0x5C, 0x96, 0x34), (0x3E, 0x6B, 0x22), []),
            PixelTextureKind.SunnyGrass => TopBlock(rnd, (0x62, 0xB0, 0x30), (0x3F, 0x7D, 0x1E),
                [((0xFF, 0xD8, 0x3A), 5), ((0xFF, 0xF4, 0xB0), 2)]),
            PixelTextureKind.FlowerGrass => TopBlock(rnd, (0x6C, 0xB8, 0x48), (0x45, 0x86, 0x2E),
                [((0xF7, 0xA8, 0xC8), 5), ((0xFF, 0xFF, 0xFF), 3)]),
            PixelTextureKind.PastelGrass => TopBlock(rnd, (0x86, 0xC8, 0x6A), (0x5A, 0x9A, 0x48),
                [((0xF7, 0xA8, 0xC8), 3), ((0xFF, 0xE0, 0x8A), 3), ((0xC6, 0xA8, 0xF0), 3), ((0x9E, 0xD8, 0xF5), 2)]),
            PixelTextureKind.AutumnGrass => AutumnTop(rnd),
            PixelTextureKind.SnowyGrass => TopBlock(rnd, (0xF2, 0xF6, 0xFC), (0xBF, 0xCD, 0xE0), [], noise: 5),
            PixelTextureKind.GarlandGrass => Garland(TopBlock(rnd, (0xF2, 0xF6, 0xFC), (0xBF, 0xCD, 0xE0), [], noise: 5)),
            PixelTextureKind.CursedGrass => TopBlock(rnd, (0x4E, 0x2E, 0x62), (0x2E, 0x1A, 0x3A),
                [((0xFF, 0x8C, 0x1A), 4)], dirt: DarkDirtPixel),

            PixelTextureKind.Stone => Stone(rnd, 0x34, 0x34, 0x3A, cracks: 7),
            PixelTextureKind.Deepslate => Deepslate(rnd),
            PixelTextureKind.MossyStone => Speckle(Stone(rnd, 0x33, 0x36, 0x34, cracks: 6), rnd, [(0x3F, 0x6B, 0x2A), (0x4E, 0x80, 0x34)], 22),
            PixelTextureKind.FrozenStone => Speckle(Stone(rnd, 0x2E, 0x36, 0x44, cracks: 6), rnd, [(0x8A, 0xA6, 0xC8), (0xC8, 0xD8, 0xEC)], 14),
            PixelTextureKind.SpeckledStone => Speckle(Stone(rnd, 0x36, 0x33, 0x3C, cracks: 5), rnd,
                [(0x8A, 0x5A, 0x78), (0x8A, 0x80, 0x52), (0x52, 0x70, 0x8A)], 12),
            PixelTextureKind.Dirt => Fill(rnd, DirtPixel),
            PixelTextureKind.Planks => Planks(rnd, (0x7A, 0x5A, 0x36), (0x45, 0x30, 0x1C)),
            PixelTextureKind.DarkPlanks => Planks(rnd, (0x4A, 0x32, 0x1E), (0x26, 0x18, 0x0E)),
            PixelTextureKind.DarkBricks => Bricks(rnd),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var bitmap = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Bgra32, null, pixels, Size * 4);
        bitmap.Freeze();
        Cache[kind] = bitmap;
        return bitmap;
    }

    // --- Генераторы ---

    /// <summary>Блок сбоку: верхний слой («трава», «снег», «листья») со свисающими краями поверх земли.</summary>
    private static uint[] TopBlock(Random rnd, (int R, int G, int B) top, (int R, int G, int B) edge,
        ((int R, int G, int B) Color, int Count)[] decorations, int noise = 10, Func<Random, uint>? dirt = null)
    {
        var px = Fill(rnd, dirt ?? DirtPixel);
        var depths = new int[Size];
        for (var x = 0; x < Size; x++)
        {
            var depth = 3 + rnd.Next(0, 3) + (rnd.Next(5) == 0 ? 2 : 0);
            depths[x] = depth;
            for (var y = 0; y < depth; y++)
            {
                var d = rnd.Next(-noise, noise + 1);
                var c = y == depth - 1 ? edge : top;
                px[y * Size + x] = Rgb(c.R + d, c.G + d, c.B + d);
            }
        }
        // Украшения (цветы, искры) — одиночные пиксели в верхнем слое.
        foreach (var (color, count) in decorations)
        {
            for (var i = 0; i < count; i++)
            {
                var x = rnd.Next(Size);
                var y = rnd.Next(0, Math.Max(1, depths[x] - 1));
                px[y * Size + x] = Rgb(color.R, color.G, color.B);
            }
        }
        return px;
    }

    private static uint[] AutumnTop(Random rnd)
    {
        (int, int, int)[] leaves = [(0xD2, 0x69, 0x1E), (0xB5, 0x37, 0x1F), (0xE3, 0xA7, 0x2F), (0x9A, 0x4A, 0x1A)];
        var px = Fill(rnd, DirtPixel);
        for (var x = 0; x < Size; x++)
        {
            var depth = 3 + rnd.Next(0, 3) + (rnd.Next(4) == 0 ? 2 : 0);
            for (var y = 0; y < depth; y++)
            {
                var (r, g, b) = leaves[rnd.Next(leaves.Length)];
                var d = rnd.Next(-8, 9);
                if (y == depth - 1) d -= 30;
                px[y * Size + x] = Rgb(r + d, g + d, b + d);
            }
        }
        return px;
    }

    /// <summary>Гирлянда: разноцветные огоньки на проводе вдоль кромки снега.</summary>
    private static uint[] Garland(uint[] px)
    {
        (int, int, int)[] lights = [(0xFF, 0x4A, 0x4A), (0xFF, 0xD8, 0x3A), (0x4A, 0xD8, 0x6A), (0x4A, 0xA8, 0xFF)];
        for (var x = 0; x < Size; x++)
        {
            var wireY = 3 + (x % 8 < 4 ? x % 4 : 3 - x % 4) / 2; // провисающий провод
            px[wireY * Size + x] = Rgb(0x22, 0x3A, 0x22);
            if (x % 4 == 1)
            {
                var (r, g, b) = lights[x / 4 % lights.Length];
                px[(wireY + 1) * Size + x] = Rgb(r, g, b);
            }
        }
        return px;
    }

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

    private static uint[] Speckle(uint[] px, Random rnd, (int R, int G, int B)[] colors, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var (r, g, b) = colors[rnd.Next(colors.Length)];
            var d = rnd.Next(-8, 9);
            px[rnd.Next(px.Length)] = Rgb(r + d, g + d, b + d);
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

    private static uint[] Fill(Random rnd, Func<Random, uint> pixel)
    {
        var px = new uint[Size * Size];
        for (var i = 0; i < px.Length; i++) px[i] = pixel(rnd);
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

    private static uint DarkDirtPixel(Random rnd)
    {
        var roll = rnd.Next(10);
        var d = rnd.Next(-6, 7);
        return roll switch
        {
            0 => Rgb(0x26 + d, 0x18 + d, 0x14 + d),
            1 => Rgb(0x5A + d, 0x3A + d, 0x2C + d),
            _ => Rgb(0x3E + d, 0x28 + d, 0x20 + d),
        };
    }

    private static uint[] Planks(Random rnd, (int R, int G, int B) wood, (int R, int G, int B) seamColor)
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
                    ? Rgb(seamColor.R + d / 2, seamColor.G + d / 2, seamColor.B)
                    : Rgb(wood.R + d, wood.G + d, wood.B + d);
            }
        }
        return px;
    }

    private static uint[] Bricks(Random rnd)
    {
        var px = new uint[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            var row = y / 4;
            var mortarRow = y % 4 == 3;
            var offset = row % 2 == 0 ? 0 : 4;
            for (var x = 0; x < Size; x++)
            {
                var mortar = mortarRow || (x + offset) % 8 == 7;
                var d = rnd.Next(-6, 7);
                px[y * Size + x] = mortar ? Rgb(0x16 + d / 2, 0x0E, 0x12) : Rgb(0x3C + d, 0x1C + d / 2, 0x26 + d / 2);
            }
        }
        // Редкие тлеющие угольки
        for (var i = 0; i < 4; i++) px[rnd.Next(px.Length)] = Rgb(0xC8, 0x5A, 0x14);
        return px;
    }

    private static uint Rgb(int r, int g, int b) =>
        0xFF000000u | (uint)Math.Clamp(r, 0, 255) << 16 | (uint)Math.Clamp(g, 0, 255) << 8 | (uint)Math.Clamp(b, 0, 255);
}
