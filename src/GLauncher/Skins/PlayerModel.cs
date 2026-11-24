using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace GLauncher.Skins;

/// <summary>
/// 3D-модель игрока из скина: голова, тело, руки, ноги и второй слой (шляпа, куртка, рукава, штанины), плюс плащ.
/// Единица — один пиксель скина. Игрок стоит на y=0, смотрит в +Z, рост 32.
/// </summary>
public static class PlayerModel
{
    public static Model3DGroup Build(SkinImage skin, bool slim, byte[]? capePng)
    {
        var group = new Model3DGroup();
        var material = TextureMaterial(skin.ToTexture());
        var arm = slim ? 3 : 4;

        // --- Базовый слой ---
        group.Children.Add(Box(-4, 24, -4, 4, 32, 4, 0, 0, 8, 8, 8, material));                  // голова
        group.Children.Add(Box(-4, 12, -2, 4, 24, 2, 16, 16, 8, 12, 4, material));                // тело
        group.Children.Add(Box(-4 - arm, 12, -2, -4, 24, 2, 40, 16, arm, 12, 4, material));       // правая рука
        group.Children.Add(Box(4, 12, -2, 4 + arm, 24, 2, 32, 48, arm, 12, 4, material));         // левая рука
        group.Children.Add(Box(-4, 0, -2, 0, 12, 2, 0, 16, 4, 12, 4, material));                  // правая нога
        group.Children.Add(Box(0, 0, -2, 4, 12, 2, 16, 48, 4, 12, 4, material));                  // левая нога

        // --- Второй слой (чуть больше базового, с прозрачностью) ---
        const double h = 0.5, b = 0.25;
        group.Children.Add(Box(-4 - h, 24 - h, -4 - h, 4 + h, 32 + h, 4 + h, 32, 0, 8, 8, 8, material));
        group.Children.Add(Box(-4 - b, 12 - b, -2 - b, 4 + b, 24 + b, 2 + b, 16, 32, 8, 12, 4, material));
        group.Children.Add(Box(-4 - arm - b, 12 - b, -2 - b, -4 + b, 24 + b, 2 + b, 40, 32, arm, 12, 4, material));
        group.Children.Add(Box(4 - b, 12 - b, -2 - b, 4 + arm + b, 24 + b, 2 + b, 48, 48, arm, 12, 4, material));
        group.Children.Add(Box(-4 - b, -b, -2 - b, b, 12 + b, 2 + b, 0, 32, 4, 12, 4, material));
        group.Children.Add(Box(-b, -b, -2 - b, 4 + b, 12 + b, 2 + b, 0, 48, 4, 12, 4, material));

        if (capePng is not null)
            group.Children.Add(Cape(capePng));
        return group;
    }

    /// <summary>Плащ 10×16×1 за спиной, слегка отведённый назад.</summary>
    private static Model3D Cape(byte[] png)
    {
        var texture = SkinImage.CapeTexture(png, out var w, out var hgt);
        var material = TextureMaterial(texture);
        // Развёртка плаща задана для 64×32; у HD-плащей (например 128×64) пропорции те же.
        var cape = Box(-5, 8, -3, 5, 24, -2, 0, 0, 10, 16, 1, material, 64, 64.0 * hgt / w);
        var transform = new Transform3DGroup();
        // «Лицевая» сторона развёртки плаща должна смотреть назад: разворот на 180° вокруг вертикали.
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 180), 0, 16, -2.5));
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 8), 0, 24, -2));
        cape.Transform = transform;
        return cape;
    }

    private static Material TextureMaterial(ImageSource texture)
    {
        // Absolute-Viewport обязателен: иначе WPF растягивает картинку по границам использованных
        // текстурных координат, а не по всей текстуре 0..1.
        var brush = new ImageBrush(texture) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        return new DiffuseMaterial(brush);
    }

    /// <summary>
    /// Коробка (x0,y0,z0)–(x1,y1,z1) с развёрткой Minecraft: верх (u+d, v), низ (u+d+w, v),
    /// правая сторона (u, v+d), перед (u+d, v+d), левая сторона (u+d+w, v+d), спина (u+2d+w, v+d).
    /// </summary>
    private static GeometryModel3D Box(double x0, double y0, double z0, double x1, double y1, double z1,
        int u, int v, int w, int h, int d, Material material, double texW = 64, double texH = 64)
    {
        var mesh = new MeshGeometry3D();

        // a — верхний левый, b — верхний правый, c — нижний правый, d — нижний левый угол грани, если смотреть на неё снаружи
        void Face(Point3D a, Point3D b, Point3D c, Point3D dd, double tu, double tv, double tw, double th)
        {
            var i = mesh.Positions.Count;
            mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c); mesh.Positions.Add(dd);
            const double e = 0.02; // отступ внутрь пикселя — без «протекания» соседних пикселей по краям
            mesh.TextureCoordinates.Add(new Point((tu + e) / texW, (tv + e) / texH));
            mesh.TextureCoordinates.Add(new Point((tu + tw - e) / texW, (tv + e) / texH));
            mesh.TextureCoordinates.Add(new Point((tu + tw - e) / texW, (tv + th - e) / texH));
            mesh.TextureCoordinates.Add(new Point((tu + e) / texW, (tv + th - e) / texH));
            // против часовой стрелки, если смотреть снаружи
            mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 3); mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i + 1);
        }

        Face(new(x0, y1, z1), new(x1, y1, z1), new(x1, y0, z1), new(x0, y0, z1), u + d, v + d, w, h);                 // перед (+z)
        Face(new(x1, y1, z0), new(x0, y1, z0), new(x0, y0, z0), new(x1, y0, z0), u + 2 * d + w, v + d, w, h);         // спина (−z)
        Face(new(x0, y1, z0), new(x0, y1, z1), new(x0, y0, z1), new(x0, y0, z0), u, v + d, d, h);                     // правая сторона (−x)
        Face(new(x1, y1, z1), new(x1, y1, z0), new(x1, y0, z0), new(x1, y0, z1), u + d + w, v + d, d, h);             // левая сторона (+x)
        Face(new(x0, y1, z0), new(x1, y1, z0), new(x1, y1, z1), new(x0, y1, z1), u + d, v, w, d);                     // верх
        Face(new(x0, y0, z1), new(x1, y0, z1), new(x1, y0, z0), new(x0, y0, z0), u + d + w, v, w, d);                 // низ

        mesh.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = null };
    }
}
