using System.Text.Json.Nodes;

namespace GLauncher.Core.Games;

public enum GameArtId { Java, Bedrock, Dungeons, DungeonsII, Legends }

/// <summary>
/// Официальные арты игр для обложек (картинка с логотипом). Они принадлежат Mojang/Microsoft, поэтому в репозиторий
/// и в сборку не попадают: лаунчер скачивает «TitledHeroArt» из каталога Microsoft Store (тот же источник, что у страниц
/// магазина) и хранит в кэше на диске. Пока арта нет (нет сети) — обложка показывает свой куб и заголовок.
/// </summary>
public static class GameArt
{
    private static readonly Dictionary<GameArtId, string> StoreIds = new()
    {
        [GameArtId.Java] = "9NXP44L49SHJ",       // Minecraft: Java & Bedrock Edition for PC (у Java Edition своего арта нет)
        [GameArtId.Bedrock] = "9NBLGGH2JHXJ",    // Minecraft for Windows
        [GameArtId.Dungeons] = "9P8MK4NC0LJB",   // Minecraft Dungeons for Windows
        [GameArtId.DungeonsII] = "9P5786PJB9RP", // Minecraft Dungeons II
        [GameArtId.Legends] = "9N98Z825TNFW",    // Minecraft Legends - Windows
    };

    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static string Folder => Path.Combine(Paths.Cache, "art");

    public static string PathOf(GameArtId id) => Path.Combine(Folder, id + ".img");

    /// <summary>Арт из кэша, если уже скачан.</summary>
    public static string? Cached(GameArtId id) => File.Exists(PathOf(id)) ? PathOf(id) : null;

    /// <summary>Скачивает арты, которых нет в кэше или которые старше недели. Ошибки сети не бросает.</summary>
    public static async Task<IReadOnlyList<GameArtId>> RefreshAsync(CancellationToken ct)
    {
        TryDeleteOldIcons();
        var stale = StoreIds.Keys
            .Where(id => !File.Exists(PathOf(id)) || DateTime.UtcNow - File.GetLastWriteTimeUtc(PathOf(id)) > MaxAge)
            .ToList();
        if (stale.Count == 0) return [];

        var updated = new List<GameArtId>();
        try
        {
            Directory.CreateDirectory(Folder);
            var ids = string.Join(',', stale.Select(id => StoreIds[id]).Distinct());
            var catalog = await Http.GetJsonAsync(
                $"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={ids}&market=US&languages=en-US", ct);

            foreach (var product in catalog["Products"] as JsonArray ?? [])
            {
                var productId = product?["ProductId"].Str();
                var images = product?["LocalizedProperties"]?[0]?["Images"] as JsonArray ?? [];
                var art = Pick(images, "TitledHeroArt") ?? Pick(images, "SuperHeroArt");
                if (art is null) continue;

                foreach (var id in stale.Where(id => StoreIds[id] == productId))
                {
                    // Адрес приходит без схемы («//store-images…»); сервер картинок сам уменьшает до нужной ширины.
                    var url = (art.StartsWith("//") ? "https:" + art : art) + "?w=1600";
                    var tmp = PathOf(id) + ".part";
                    try
                    {
                        await Downloader.DownloadFileAsync(new DownloadItem(url, tmp), ct);
                        File.Move(tmp, PathOf(id), overwrite: true);
                        updated.Add(id);
                    }
                    catch (Exception)
                    {
                        try { File.Delete(tmp); } catch { }
                    }
                }
            }
        }
        catch (Exception)
        {
            // Нет сети или каталог недоступен — остаются арты из кэша или свой куб.
        }
        return updated;
    }

    private static string? Pick(JsonArray images, string purpose) => images
        .Where(i => i?["ImagePurpose"].Str() == purpose)
        .OrderByDescending(i => i!["Width"].Long())
        .FirstOrDefault()?["Uri"].Str();

    /// <summary>Логотипы из прошлых версий больше не нужны — иконки изданий рисует сам лаунчер.</summary>
    private static void TryDeleteOldIcons()
    {
        try
        {
            var old = Path.Combine(Paths.Cache, "icons");
            if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
        }
        catch { }
    }
}
