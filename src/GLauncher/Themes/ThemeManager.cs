using System.Windows;
using System.Windows.Media;

namespace GLauncher.Themes;

public enum ThemeId
{
    Auto, Blocks, Spring, Summer, Autumn, Winter, NewYear, Easter, Halloween,
    // Эксклюзивные — только для людей из благодарностей
    Legends, Dungeons, Nether, End,
}

/// <summary>
/// Тема = палитра + текстуры шапки и фона. Стили контролов (Theme.xaml) общие для всех тем
/// и берут цвета через DynamicResource, поэтому тема переключается на лету.
/// </summary>
public sealed record ThemeDefinition(
    ThemeId Id,
    string Title,
    PixelTextureKind Header,
    PixelTextureKind Background,
    Color Accent,        // кнопки «Играть», «Найти», выделение
    Color AccentLight,   // фокус, галочки, буква «g» в логотипе
    Color Headline,      // заголовки разделов, выбранная вкладка
    Color XpLight,       // шкала прогресса
    Color XpDark,
    Color TooltipBorder,
    Color Face,          // обычные кнопки
    Color Danger);

public static class ThemeManager
{
    private static ResourceDictionary? _current;

    public static IReadOnlyList<ThemeDefinition> Themes { get; } =
    [
        new(ThemeId.Blocks, "Блоки", PixelTextureKind.GrassBlock, PixelTextureKind.Stone,
            C("#4E8C2A"), C("#8BD14B"), C("#F2C94C"), C("#8BE34F"), C("#56A82B"), C("#5A2FA8"), C("#5A5A62"), C("#8E3229")),
        new(ThemeId.Spring, "Весна", PixelTextureKind.FlowerGrass, PixelTextureKind.MossyStone,
            C("#4F9E45"), C("#A8E07A"), C("#F7A8C8"), C("#A8E07A"), C("#5AA845"), C("#C2569A"), C("#58605A"), C("#8E3240")),
        new(ThemeId.Summer, "Лето", PixelTextureKind.SunnyGrass, PixelTextureKind.Planks,
            C("#2E8FCC"), C("#7FD3FF"), C("#FFD45C"), C("#FFE066"), C("#E0A020"), C("#2E8FCC"), C("#5E5A54"), C("#A33A28")),
        new(ThemeId.Autumn, "Осень", PixelTextureKind.AutumnGrass, PixelTextureKind.DarkPlanks,
            C("#B8561A"), C("#F2A03D"), C("#F2C94C"), C("#F2A03D"), C("#B5531A"), C("#8E3B1A"), C("#5E5650"), C("#7E2A1E")),
        new(ThemeId.Winter, "Зима", PixelTextureKind.SnowyGrass, PixelTextureKind.FrozenStone,
            C("#3A78BE"), C("#9FD4FF"), C("#E2F0FF"), C("#BFE4FF"), C("#4E8DD6"), C("#3A78BE"), C("#56606E"), C("#8E3245")),
        new(ThemeId.NewYear, "Новый год", PixelTextureKind.GarlandGrass, PixelTextureKind.Deepslate,
            C("#B3262E"), C("#FF7A7A"), C("#F2C94C"), C("#F05454"), C("#A81E27"), C("#2E8B57"), C("#555A5E"), C("#6E1A20")),
        new(ThemeId.Easter, "Пасха", PixelTextureKind.PastelGrass, PixelTextureKind.SpeckledStone,
            C("#8A5FC4"), C("#D6B8FF"), C("#FFE08A"), C("#F7A8C8"), C("#C2569A"), C("#8A5FC4"), C("#5C5866"), C("#8E3250")),
        new(ThemeId.Halloween, "Хэллоуин", PixelTextureKind.CursedGrass, PixelTextureKind.DarkBricks,
            C("#C9591A"), C("#FF9A3D"), C("#C58BFF"), C("#FF9A3D"), C("#B44C0F"), C("#7B3FB5"), C("#4A4250"), C("#6E1F3A")),

        // ---- Эксклюзивные темы (благодарности) ----
        new(ThemeId.Legends, "★ Legends", PixelTextureKind.LegendsGrass, PixelTextureKind.LegendsStone,
            C("#1F9E86"), C("#5FE3C4"), C("#FFD66A"), C("#7FE8FF"), C("#2F9ED0"), C("#E0A030"), C("#4E5E66"), C("#9A3A2E")),
        new(ThemeId.Dungeons, "★ Dungeons", PixelTextureKind.DungeonMoss, PixelTextureKind.Deepslate,
            C("#2E9E4E"), C("#4FE07A"), C("#FFB648"), C("#4FE07A"), C("#1F7A3A"), C("#C97A1E"), C("#4E4E54"), C("#8A2A2A")),
        new(ThemeId.Nether, "★ Нижний мир", PixelTextureKind.CrimsonNylium, PixelTextureKind.Netherrack,
            C("#B0222E"), C("#FF6A4A"), C("#FFC94A"), C("#FF8A3A"), C("#B03A14"), C("#F2A02A"), C("#5A3A3C"), C("#6E0F18")),
        new(ThemeId.End, "★ Энд", PixelTextureKind.EndStoneTop, PixelTextureKind.Obsidian,
            C("#7E3FC0"), C("#C08BFF"), C("#E8E6A8"), C("#D6A8FF"), C("#7E3FC0"), C("#C08BFF"), C("#4A4058"), C("#8A2A5A")),
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

    /// <summary>Тема, которая реально применится: для «Авто» — по текущей дате.</summary>
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

    /// <summary>Подставляет палитру и текстуры темы в ресурсы приложения. Окна перекрашиваются сразу.</summary>
    public static void Apply(ThemeDefinition theme)
    {
        var dict = Build(theme);
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null && merged.Contains(_current))
            merged[merged.IndexOf(_current)] = dict;
        else
            merged.Insert(0, dict);
        _current = dict;
    }

    private static ResourceDictionary Build(ThemeDefinition t)
    {
        var d = new ResourceDictionary();

        // Общие для всех тем
        d["Brush.Text"] = B("#F0F0F0");
        d["Brush.TextDim"] = B("#A9A9B2");
        d["Brush.Warning"] = B("#FF7A6B");
        d["Brush.Outline"] = B("#000000");
        d["Brush.FaceDisabled"] = B("#2F2F34");
        d["Brush.Field"] = B("#101013");
        d["Brush.FieldBorder"] = B("#5C5C66");
        d["Brush.FieldBorderHover"] = B("#8A8A96");
        d["Brush.Panel"] = B("#D91C1C20");
        d["Brush.PanelDeep"] = B("#E6101013");
        d["Brush.Hover"] = B("#1FFFFFFF");
        d["Brush.BevelLight"] = B("#55FFFFFF");
        d["Brush.BevelDark"] = B("#66000000");

        // Цвета темы
        d["Brush.Face"] = new SolidColorBrush(t.Face).Frozen();
        d["Brush.Accent"] = new SolidColorBrush(t.Accent).Frozen();
        d["Brush.AccentLight"] = new SolidColorBrush(t.AccentLight).Frozen();
        d["Brush.TextGold"] = new SolidColorBrush(t.Headline).Frozen();
        d["Brush.Danger"] = new SolidColorBrush(t.Danger).Frozen();
        d["Brush.TooltipBorder"] = new SolidColorBrush(t.TooltipBorder).Frozen();
        d["Brush.Selection"] = new SolidColorBrush(Shade(t.Accent, 0.75, 0xB0)).Frozen();
        d["Brush.Banner"] = new SolidColorBrush(Shade(t.Accent, 0.35, 0xF2)).Frozen();
        d["Brush.Xp"] = XpBrush(t.XpLight, t.XpDark);

        // Текстуры
        d["Texture.Background"] = PixelTextures.CreateBrush(t.Background, 64);
        d["Texture.Header"] = PixelTextures.CreateBrush(t.Header, 72);
        return d;
    }

    /// <summary>Шкала прогресса в виде сегментов: светлая верхняя половина, тёмная нижняя, тёмный разделитель.</summary>
    private static DrawingBrush XpBrush(Color light, Color dark)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(light), null, new RectangleGeometry(new Rect(0, 0, 12, 10))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(dark), null, new RectangleGeometry(new Rect(0, 5, 12, 5))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Shade(dark, 0.35, 0xFF)), null, new RectangleGeometry(new Rect(11, 0, 1, 10))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 12, 10),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    private static Color Shade(Color c, double k, byte alpha) =>
        Color.FromArgb(alpha, (byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush B(string hex) => new SolidColorBrush(C(hex)).Frozen();

    private static T Frozen<T>(this T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
