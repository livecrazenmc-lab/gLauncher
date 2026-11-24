using System.Windows;
using System.Windows.Media;

namespace GLauncher.Themes;

public enum ThemeId
{
    Auto, Blocks, Spring, Summer, Autumn, Winter, NewYear, Easter, Halloween,
    // Эксклюзивные — только для людей из благодарностей
    Legends, Dungeons, Nether, End,
}

/// <summary>Анимированные частицы на фоне окна — «характер» темы.</summary>
/// <summary>Частицы фона. Dandelions — лето: пух одуванчика и светлячки.</summary>
public enum ParticleKind { None, Snow, Leaves, Petals, Fireflies, Embers, Sparkles, Motes, Dandelions }

/// <summary>
/// Тема стиля «Изометрия»: тёмный градиент фона, пара акцентных цветов (кнопки, свечения, сетка),
/// цвет заголовков и частицы. Стили контролов (Theme.xaml) общие и берут цвета через DynamicResource,
/// поэтому тема переключается на лету.
/// </summary>
public sealed record ThemeDefinition(
    ThemeId Id,
    string Title,
    Color BaseTop,        // фон окна сверху
    Color BaseBottom,     // фон окна снизу
    Color Accent,         // основной акцент: кнопки, выделение, сетка, свечение
    Color Accent2,        // второй акцент: конец градиентов, второе свечение
    Color Headline,       // заголовки разделов
    ParticleKind Particles,
    Color[] ParticleColors);

public static class ThemeManager
{
    private static ResourceDictionary? _current;

    /// <summary>Текущая применённая тема (для слоя частиц).</summary>
    public static ThemeDefinition Current { get; private set; } = null!;

    public static event Action? Changed;

    public static IReadOnlyList<ThemeDefinition> Themes { get; } =
    [
        new(ThemeId.Blocks, "Изометрия", C("#0C1016"), C("#121A24"), C("#4ADE80"), C("#22D3EE"), C("#A7F3D0"),
            ParticleKind.Motes, [C("#4ADE80"), C("#22D3EE")]),
        new(ThemeId.Spring, "Весна", C("#0E1412"), C("#17211C"), C("#F472B6"), C("#86EFAC"), C("#FBCFE8"),
            ParticleKind.Petals, [C("#FFE4E6"), C("#FBCFE8"), C("#F9A8D4"), C("#F472B6"), C("#FDA4AF")]),
        new(ThemeId.Summer, "Лето", C("#0B1119"), C("#11202C"), C("#FBBF24"), C("#38BDF8"), C("#FDE68A"),
            ParticleKind.Dandelions, [C("#FFFFFF"), C("#FEF3C7"), C("#FDE047")]),
        new(ThemeId.Autumn, "Осень", C("#140F0C"), C("#1F1712"), C("#FB923C"), C("#F43F5E"), C("#FED7AA"),
            ParticleKind.Leaves, [C("#FACC15"), C("#FBBF24"), C("#F59E0B"), C("#F97316"), C("#EA580C"), C("#DC2626"), C("#B91C1C")]),
        new(ThemeId.Winter, "Зима", C("#0A1020"), C("#121C30"), C("#93C5FD"), C("#E0F2FE"), C("#DBEAFE"),
            ParticleKind.Snow, [C("#FFFFFF"), C("#E0F2FE")]),
        new(ThemeId.NewYear, "Новый год", C("#0E0E1A"), C("#1B1428"), C("#F43F5E"), C("#FBBF24"), C("#FDE68A"),
            ParticleKind.Snow, [C("#FFFFFF"), C("#FDE68A"), C("#FDA4AF"), C("#86EFAC")]),
        new(ThemeId.Easter, "Пасха", C("#13111C"), C("#1C1729"), C("#C4B5FD"), C("#F9A8D4"), C("#EDE9FE"),
            ParticleKind.Petals, [C("#C4B5FD"), C("#F9A8D4"), C("#FDE68A"), C("#A7F3D0")]),
        new(ThemeId.Halloween, "Хэллоуин", C("#110C13"), C("#1C1220"), C("#FB923C"), C("#A855F7"), C("#FED7AA"),
            ParticleKind.Embers, [C("#FB923C"), C("#F97316"), C("#C084FC")]),

        // ---- Эксклюзивные темы (благодарности) ----
        new(ThemeId.Legends, "★ Legends", C("#091515"), C("#0F2224"), C("#2DD4BF"), C("#FCD34D"), C("#CCFBF1"),
            ParticleKind.Sparkles, [C("#5EEAD4"), C("#FDE68A"), C("#FFFFFF")]),
        new(ThemeId.Dungeons, "★ Dungeons", C("#0D110E"), C("#151C16"), C("#34D399"), C("#F59E0B"), C("#D1FAE5"),
            ParticleKind.Embers, [C("#34D399"), C("#FBBF24")]),
        new(ThemeId.Nether, "★ Нижний мир", C("#16090B"), C("#25100F"), C("#F43F5E"), C("#F97316"), C("#FECACA"),
            ParticleKind.Embers, [C("#F97316"), C("#FB7185"), C("#FDE047")]),
        new(ThemeId.End, "★ Энд", C("#0D0915"), C("#180F25"), C("#C084FC"), C("#FDE68A"), C("#F3E8FF"),
            ParticleKind.Motes, [C("#C084FC"), C("#E9D5FF"), C("#FDE68A")]),
    ];

    /// <summary>Эксклюзивные темы видны и применяются только людям из благодарностей.</summary>
    public static bool IsExclusive(ThemeId id) => id is ThemeId.Legends or ThemeId.Dungeons or ThemeId.Nether or ThemeId.End;

    /// <summary>Сезонные и праздничные оформления — отдельно не выбираются, их подбирает тема «Сезонная» (Auto) по дате.</summary>
    public static bool IsSeasonal(ThemeId id) =>
        id is ThemeId.Spring or ThemeId.Summer or ThemeId.Autumn or ThemeId.Winter or ThemeId.NewYear or ThemeId.Easter or ThemeId.Halloween;

    /// <summary>Тема из настроек: старые значения (конкретный сезон или праздник) превращаются в «Сезонную».</summary>
    public static ThemeId FromSetting(string? value)
    {
        var id = Enum.TryParse<ThemeId>(value, out var parsed) ? parsed : ThemeId.Auto;
        return IsSeasonal(id) ? ThemeId.Auto : id;
    }

    public static ThemeDefinition Get(ThemeId id) => Themes.FirstOrDefault(t => t.Id == id) ?? Themes[0];

    /// <summary>Тема, которая реально применится: для «Сезонной» (Auto) — по текущей дате.</summary>
    public static ThemeDefinition Resolve(ThemeId id, DateTime? today = null) =>
        Get(id == ThemeId.Auto ? ForDate(today ?? DateTime.Today) : id);

    /// <summary>
    /// Автовыбор: Новый год 20.12–10.01, Хэллоуин 24.10–02.11, Пасха ±7 дней от православной Пасхи,
    /// в остальное время — тема сезона.
    /// </summary>
    public static ThemeId ForDate(DateTime date)
    {
        var d = date.Date;
        if ((d.Month == 12 && d.Day >= 20) || (d.Month == 1 && d.Day <= 10)) return ThemeId.NewYear;
        if ((d.Month == 10 && d.Day >= 24) || (d.Month == 11 && d.Day <= 2)) return ThemeId.Halloween;
        var easter = OrthodoxEaster(d.Year);
        if (d >= easter.AddDays(-7) && d <= easter.AddDays(7)) return ThemeId.Easter;

        return d.Month switch
        {
            12 or 1 or 2 => ThemeId.Winter,
            3 or 4 or 5 => ThemeId.Spring,
            6 or 7 or 8 => ThemeId.Summer,
            _ => ThemeId.Autumn,
        };
    }

    /// <summary>Православная Пасха (алгоритм Меёса для юлианского календаря + разница в 13 дней, 1900–2099).</summary>
    public static DateTime OrthodoxEaster(int year)
    {
        int a = year % 4, b = year % 7, c = year % 19;
        var d = (19 * c + 15) % 30;
        var e = (2 * a + 4 * b - d + 34) % 7;
        var month = (d + e + 114) / 31;
        var day = (d + e + 114) % 31 + 1;
        return new DateTime(year, month, day).AddDays(13);
    }

    /// <summary>Подставляет палитру темы в ресурсы приложения. Окна перекрашиваются сразу.</summary>
    public static void Apply(ThemeDefinition theme)
    {
        var dict = Build(theme);
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null && merged.Contains(_current))
            merged[merged.IndexOf(_current)] = dict;
        else
            merged.Insert(0, dict);
        _current = dict;
        Current = theme;
        Changed?.Invoke();
    }

    private static ResourceDictionary Build(ThemeDefinition t)
    {
        var d = new ResourceDictionary();
        var surface = Mix(t.BaseBottom, Colors.White, 0.06);

        // --- Цвета (для эффектов свечения) ---
        d["Color.Accent"] = t.Accent;
        d["Color.Accent2"] = t.Accent2;
        d["Color.Glow"] = t.Accent;

        // --- Текст ---
        d["Brush.Text"] = B("#F1F5F9");
        d["Brush.TextDim"] = B("#94A3B8");
        d["Brush.TextGold"] = new SolidColorBrush(t.Headline).Frozen();
        d["Brush.OnAccent"] = B("#0B1016");
        d["Brush.Warning"] = B("#FB7185");

        // --- Поверхности: полупрозрачное «стекло» поверх сияния фона ---
        d["Brush.Panel"] = new SolidColorBrush(WithAlpha(surface, 0xB8)).Frozen();
        d["Brush.PanelDeep"] = new SolidColorBrush(WithAlpha(Mix(t.BaseTop, Colors.White, 0.04), 0xCC)).Frozen();
        d["Brush.Popup"] = new SolidColorBrush(Mix(t.BaseBottom, Colors.White, 0.09)).Frozen();
        d["Brush.Outline"] = B("#1CFFFFFF");
        d["Brush.OutlineStrong"] = new SolidColorBrush(WithAlpha(t.Accent, 0x8C)).Frozen();
        d["Brush.Face"] = B("#17FFFFFF");
        d["Brush.FaceHover"] = B("#26FFFFFF");
        d["Brush.FaceDisabled"] = B("#0AFFFFFF");
        d["Brush.Field"] = B("#38000000");
        d["Brush.FieldBorder"] = B("#1FFFFFFF");
        d["Brush.FieldBorderHover"] = B("#40FFFFFF");
        d["Brush.Hover"] = B("#10FFFFFF");
        d["Brush.BevelLight"] = Brushes.Transparent;
        d["Brush.BevelDark"] = Brushes.Transparent;

        // --- Акценты ---
        d["Brush.Accent"] = new SolidColorBrush(t.Accent).Frozen();
        d["Brush.AccentLight"] = new SolidColorBrush(Mix(t.Accent, Colors.White, 0.35)).Frozen();
        d["Brush.Accent2"] = new SolidColorBrush(t.Accent2).Frozen();
        d["Brush.AccentGradient"] = Gradient(t.Accent, t.Accent2, horizontal: true);
        d["Brush.Selection"] = new SolidColorBrush(WithAlpha(t.Accent, 0x2E)).Frozen();
        d["Brush.Danger"] = new SolidColorBrush(C("#E11D48")).Frozen();
        d["Brush.TooltipBorder"] = new SolidColorBrush(WithAlpha(t.Accent, 0x80)).Frozen();
        d["Brush.Banner"] = Gradient(WithAlpha(t.Accent, 0x3A), WithAlpha(t.Accent2, 0x26), horizontal: true);
        d["Brush.Xp"] = Gradient(t.Accent, t.Accent2, horizontal: true);

        // --- Фон окна: градиент + «северное сияние» + изометрическая сетка ---
        d["Brush.WindowBase"] = Gradient(t.BaseTop, t.BaseBottom, horizontal: false);
        d["Brush.Aurora1"] = Aurora(t.Accent, new Point(0.12, 0.0));
        d["Brush.Aurora2"] = Aurora(t.Accent2, new Point(0.95, 1.0));
        d["Brush.Pattern"] = IsoPattern(WithAlpha(t.Accent, 0x14));

        // Старые ключи: фоны обложек и шапки — теперь градиенты стиля
        d["Texture.Background"] = Gradient(t.BaseTop, t.BaseBottom, horizontal: false);
        d["Texture.Header"] = Gradient(t.Accent, t.Accent2, horizontal: true);
        return d;
    }

    /// <summary>Изометрическая сетка из ромбов — как грани куба из иконки.</summary>
    private static DrawingBrush IsoPattern(Color line)
    {
        var pen = new Pen(new SolidColorBrush(line), 1);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 16), false, true);
            c.PolyLineTo([new Point(28, 0), new Point(56, 16), new Point(28, 32)], true, true);
        }
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 56, 32))));
        group.Children.Add(new GeometryDrawing(null, pen, g));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(WithAlpha(line, (byte)Math.Min(255, line.A * 3))), null,
            new EllipseGeometry(new Point(28, 0), 1.3, 1.3)));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 56, 32),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    private static RadialGradientBrush Aurora(Color color, Point center)
    {
        var brush = new RadialGradientBrush
        {
            Center = center, GradientOrigin = center, RadiusX = 0.75, RadiusY = 0.9,
            GradientStops =
            {
                new GradientStop(WithAlpha(color, 0x30), 0),
                new GradientStop(WithAlpha(color, 0x0C), 0.55),
                new GradientStop(WithAlpha(color, 0x00), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Gradient(Color a, Color b, bool horizontal)
    {
        var brush = horizontal
            ? new LinearGradientBrush(a, b, new Point(0, 0.5), new Point(1, 0.5))
            : new LinearGradientBrush(a, b, 90);
        brush.Freeze();
        return brush;
    }

    private static Color Mix(Color a, Color b, double k) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * k), (byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush B(string hex) => new SolidColorBrush(C(hex)).Frozen();

    private static T Frozen<T>(this T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
