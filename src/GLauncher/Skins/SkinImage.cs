using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GLauncher.Skins;

/// <summary>Скин как массив пикселей BGRA 64×64 (старый формат 64×32 приводится к новому).</summary>
public sealed class SkinImage
{
    public const int Size = 64;
    public uint[] Pixels { get; } = new uint[Size * Size];

    public uint this[int x, int y]
    {
        get => Pixels[y * Size + x];
        set => Pixels[y * Size + x] = value;
    }

    public static SkinImage FromPng(byte[] png)
    {
        var decoder = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bmp = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var skin = new SkinImage();
        var h = Math.Min(bmp.PixelHeight, Size);
        var rows = new uint[Size * h];
        bmp.CopyPixels(new Int32Rect(0, 0, Size, h), rows, Size * 4, 0);
        Array.Copy(rows, skin.Pixels, rows.Length);

        if (h == 32)
        {
            // Старый формат: левые рука и нога — зеркальные копии правых.
            skin.CopyMirroredLimb(0, 16, 16, 48);   // нога
            skin.CopyMirroredLimb(40, 16, 32, 48);  // рука
        }
        return skin;
    }

    public static SkinImage FromFile(string path) => FromPng(File.ReadAllBytes(path));

    /// <summary>
    /// Делает левую конечность из правой (старые скины 64×32): каждая грань отражается по горизонтали,
    /// а боковые грани меняются местами.
    /// </summary>
    private void CopyMirroredLimb(int sx, int sy, int dx, int dy)
    {
        // (откуда x, y) → (куда x, y), размер w×h — координаты внутри развёртки 16×16
        (int fx, int fy, int tx, int ty, int w, int h)[] faces =
        [
            (4, 0, 4, 0, 4, 4),    // верх
            (8, 0, 8, 0, 4, 4),    // низ
            (8, 4, 0, 4, 4, 12),   // левая сторона → правая
            (4, 4, 4, 4, 4, 12),   // перед
            (0, 4, 8, 4, 4, 12),   // правая сторона → левая
            (12, 4, 12, 4, 4, 12), // спина
        ];
        foreach (var (fx, fy, tx, ty, w, h) in faces)
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                this[dx + tx + x, dy + ty + y] = this[sx + fx + (w - 1 - x), sy + fy + y];
    }

    /// <summary>Увеличенная текстура для 3D: каждый пиксель скина → scale×scale (чёткие пиксели без сглаживания).</summary>
    public BitmapSource ToTexture(int scale = 8) => Upscale(Pixels, Size, Size, scale);

    /// <summary>Вид спереди 16×32 (оба слоя) — для миниатюр в библиотеке.</summary>
    public BitmapSource FrontView(bool slim, int scale = 4)
    {
        var view = new uint[16 * 32];
        void Blit(int sx, int sy, int w, int h, int dx, int dy)
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var src = this[sx + x, sy + y];
                var a = src >> 24;
                if (a == 0) continue;
                ref var dst = ref view[(dy + y) * 16 + dx + x];
                dst = a == 255 ? src : Blend(dst, src);
            }
        }

        var arm = slim ? 3 : 4;
        // Голова + шляпа
        Blit(8, 8, 8, 8, 4, 0); Blit(40, 8, 8, 8, 4, 0);
        // Тело + куртка
        Blit(20, 20, 8, 12, 4, 8); Blit(20, 36, 8, 12, 4, 8);
        // Правая рука (слева на экране) + рукав
        Blit(44, 20, arm, 12, 4 - arm, 8); Blit(44, 36, arm, 12, 4 - arm, 8);
        // Левая рука + рукав
        Blit(36, 52, arm, 12, 12, 8); Blit(52, 52, arm, 12, 12, 8);
        // Ноги + штанины
        Blit(4, 20, 4, 12, 4, 20); Blit(4, 36, 4, 12, 4, 20);
        Blit(20, 52, 4, 12, 8, 20); Blit(4, 52, 4, 12, 8, 20);
        return Upscale(view, 16, 32, scale);
    }

    private static uint Blend(uint dst, uint src)
    {
        var a = (src >> 24) / 255.0;
        uint Ch(int shift) => (uint)(((src >> shift) & 0xFF) * a + ((dst >> shift) & 0xFF) * (1 - a));
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    public static BitmapSource Upscale(uint[] px, int w, int h, int scale)
    {
        var big = new uint[w * scale * h * scale];
        for (var y = 0; y < h * scale; y++)
        for (var x = 0; x < w * scale; x++)
            big[y * w * scale + x] = px[(y / scale) * w + x / scale];
        var bmp = BitmapSource.Create(w * scale, h * scale, 96, 96, PixelFormats.Bgra32, null, big, w * scale * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Текстура плаща (64×32 или кратная), увеличенная для 3D.</summary>
    public static BitmapSource CapeTexture(byte[] png, out int width, out int height)
    {
        var decoder = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bmp = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        width = bmp.PixelWidth; height = bmp.PixelHeight;
        var px = new uint[width * height];
        bmp.CopyPixels(px, width * 4, 0);
        return Upscale(px, width, height, Math.Max(1, 512 / width));
    }
}
