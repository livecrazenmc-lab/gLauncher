using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace GLauncher.Themes;

/// <summary>Вариант шрифта в настройках. Family = null — встроенный пиксельный Monocraft.</summary>
public sealed record FontOption(string? Family, string Title, FontFamily Preview)
{
    public override string ToString() => Title;
}

/// <summary>
/// Шрифт интерфейса: по умолчанию встроенный пиксельный Monocraft (SIL OFL 1.1), либо любой шрифт системы.
/// Журнал игры остаётся моноширинным (Cascadia Mono / Consolas).
/// </summary>
public static class FontManager
{
    public static readonly FontFamily Pixel =
        new(new Uri("pack://application:,,,/gLauncher;component/"), "./Assets/Fonts/#Monocraft, Segoe UI");

    /// <summary>Пиксельный шрифт первым, дальше все шрифты системы по алфавиту (с учётом русских названий).</summary>
    public static List<FontOption> Options()
    {
        var list = new List<FontOption> { new(null, "Пиксельный (Monocraft)", Pixel) };
        var lang = XmlLanguage.GetLanguage("ru-RU");
        list.AddRange(Fonts.SystemFontFamilies
            .Select(f => (Family: f, Name: NameOf(f, lang)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new FontOption(x.Family.Source, x.Name, x.Family)));
        return list;
    }

    private static string NameOf(FontFamily family, XmlLanguage lang) =>
        family.FamilyNames.TryGetValue(lang, out var ru) ? ru
        : family.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-US"), out var en) ? en
        : family.Source;

    /// <summary>Применить шрифт ко всему интерфейсу сразу (ключи Font.Text и Font.Display — DynamicResource).</summary>
    public static void Apply(string? family)
    {
        var font = string.IsNullOrWhiteSpace(family) ? Pixel : new FontFamily(family + ", Segoe UI");
        var resources = Application.Current.Resources;
        resources["Font.Text"] = font;
        resources["Font.Display"] = font;
    }
}
