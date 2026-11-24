using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Windows.Management.Deployment;

namespace GLauncher.Core.Games;

/// <summary>Игра семейства Minecraft, которую лаунчер находит и запускает (Dungeons, Dungeons II, Legends).</summary>
public sealed record SpinoffGame(
    string Id,
    string Title,
    string Description,
    Func<string, bool> MatchesName,
    string? StoreProductId,
    string StoreSearch,
    string[] Entitlements,
    PatchNotesKind? PatchNotes);

/// <summary>Найденная установка игры: из Microsoft Store/Xbox или Steam.</summary>
public sealed class GameInstall
{
    public required string Source { get; init; }
    public string? Version { get; init; }
    internal Func<Task> LaunchAction { get; init; } = () => Task.CompletedTask;
    public Task LaunchAsync() => LaunchAction();
}

public static class SpinoffGames
{
    public static readonly SpinoffGame Dungeons = new(
        "dungeons", "Minecraft Dungeons",
        "Приключенческий экшен в мире Minecraft: подземелья, добыча и прохождение вместе с друзьями.",
        name => name.StartsWith("Minecraft Dungeons", StringComparison.OrdinalIgnoreCase) && !IsDungeons2(name),
        "9P8MK4NC0LJB", "Minecraft Dungeons",
        ["product_dungeons", "game_dungeons"], PatchNotesKind.Dungeons);

    public static readonly SpinoffGame DungeonsII = new(
        "dungeons2", "Minecraft Dungeons II",
        "Продолжение Minecraft Dungeons: новые земли, герои и кооператив до четырёх игроков.",
        IsDungeons2,
        null, "Minecraft Dungeons II",
        [], null);

    public static readonly SpinoffGame Legends = new(
        "legends", "Minecraft Legends",
        "Стратегия в мире Minecraft: защищайте Верхний мир от пиглинов вместе с союзниками.",
        name => name.Contains("Minecraft Legends", StringComparison.OrdinalIgnoreCase),
        "9N98Z825TNFW", "Minecraft Legends",
        ["product_legends", "game_legends"], PatchNotesKind.Legends);

    public static IReadOnlyList<SpinoffGame> All { get; } = [Dungeons, DungeonsII, Legends];

    private static bool IsDungeons2(string name) =>
        Regex.IsMatch(name, @"Minecraft Dungeons\s*(II|2)\b", RegexOptions.IgnoreCase);

    /// <summary>Ищет установленные копии всех игр. Долгая операция (перебор пакетов Windows) — вызывать в фоне.</summary>
    public static Dictionary<string, List<GameInstall>> DetectAll()
    {
        var result = All.ToDictionary(g => g.Id, _ => new List<GameInstall>());
        foreach (var (name, install) in StorePackages().Concat(SteamGames()))
            foreach (var game in All.Where(g => g.MatchesName(name)))
                result[game.Id].Add(install);
        return result;
    }

    /// <summary>Открывает страницу игры в Microsoft Store (или поиск, если ID страницы неизвестен).</summary>
    public static void OpenStore(SpinoffGame game) => OpenUri(game.StoreProductId is { } id
        ? $"ms-windows-store://pdp/?ProductId={id}"
        : $"ms-windows-store://search/?query={Uri.EscapeDataString(game.StoreSearch)}");

    // --- Microsoft Store / Xbox ---

    private static IEnumerable<(string Name, GameInstall Install)> StorePackages()
    {
        var found = new List<(string, GameInstall)>();
        try
        {
            foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
            {
                string name;
                try
                {
                    if (package.IsFramework || package.IsResourcePackage) continue;
                    name = package.DisplayName;
                }
                catch { continue; }
                if (string.IsNullOrEmpty(name) || !name.Contains("Minecraft", StringComparison.OrdinalIgnoreCase)) continue;

                var v = package.Id.Version;
                var pkg = package;
                found.Add((name, new GameInstall
                {
                    Source = "Microsoft Store / Xbox",
                    Version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                    LaunchAction = async () =>
                    {
                        var entries = await pkg.GetAppListEntriesAsync();
                        if (entries.Count == 0 || !await entries[0].LaunchAsync())
                            throw new LauncherException("Windows не смогла запустить игру. Попробуйте запустить её через меню «Пуск».");
                    },
                }));
            }
        }
        catch (Exception)
        {
            // Нет доступа к списку пакетов — просто ничего не найдём в Store.
        }
        return found;
    }

    // --- Steam ---

    private static IEnumerable<(string Name, GameInstall Install)> SteamGames()
    {
        var found = new List<(string, GameInstall)>();
        var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
        if (string.IsNullOrEmpty(steam)) return found;

        var libraries = new List<string> { steam.Replace('/', '\\') };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }

        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                string text;
                try { text = File.ReadAllText(manifest); } catch (IOException) { continue; }
                var name = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                var appId = Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                if (name.Length == 0 || appId.Length == 0 || !name.Contains("Minecraft", StringComparison.OrdinalIgnoreCase)) continue;
                found.Add((name, new GameInstall
                {
                    Source = "Steam",
                    LaunchAction = () => { OpenUri($"steam://rungameid/{appId}"); return Task.CompletedTask; },
                }));
            }
        }
        return found;
    }

    internal static void OpenUri(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
}
