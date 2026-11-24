using System.Text.Json.Nodes;

namespace GLauncher.Core.Games;

public enum GameIconId { Java, Bedrock, Dungeons, DungeonsII, Legends }

/// <summary>
/// Официальные иконки игр. Они принадлежат Mojang/Microsoft, поэтому в репозиторий и в сборку не попадают:
/// лаунчер скачивает логотипы из каталога Microsoft Store (тот же источник, что у страниц магазина)
/// и хранит их в кэше на диске. Если скачать не удалось — интерфейс показывает свои пиксельные иконки.
/// </summary>
public static class GameIcons
{
    private static readonly Dictionary<GameIconId, string> StoreIds = new()
    {
        [GameIconId.Java] = "9PJ8266BHFWN",       // Minecraft: Java Edition
        [GameIconId.Bedrock] = "9NBLGGH2JHXJ",    // Minecraft for Windows
        [GameIconId.Dungeons] = "9P8MK4NC0LJB",   // Minecraft Dungeons for Windows
        [GameIconId.DungeonsII] = "9P5786PJB9RP", // Minecraft Dungeons II
        [GameIconId.Legends] = "9N98Z825TNFW",    // Minecraft Legends - Windows
    };

    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static string Folder => Path.Combine(Paths.Cache, "icons");

    public static string PathOf(GameIconId id) => Path.Combine(Folder, id + ".img");

    /// <summary>Иконка из кэша, если уже скачана.</summary>
    public static string? Cached(GameIconId id) => File.Exists(PathOf(id)) ? PathOf(id) : null;

    /// <summary>Скачивает логотипы, которых нет в кэше или которые старше недели. Ошибки сети не бросает.</summary>
    public static async Task<IReadOnlyList<GameIconId>> RefreshAsync(CancellationToken ct)
    {
        var stale = StoreIds.Keys
            .Where(id => !File.Exists(PathOf(id)) || DateTime.UtcNow - File.GetLastWriteTimeUtc(PathOf(id)) > MaxAge)
            .ToList();
        if (stale.Count == 0) return [];

        var updated = new List<GameIconId>();
        try
        {
            var ids = string.Join(',', stale.Select(id => StoreIds[id]));
            var catalog = await Http.GetJsonAsync(
                $"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={ids}&market=US&languages=en-US", ct);

            foreach (var product in catalog["Products"] as JsonArray ?? [])
            {
                var productId = product?["ProductId"].Str();
                var match = StoreIds.Where(kv => kv.Value == productId).Select(kv => (GameIconId?)kv.Key).FirstOrDefault();
                if (match is not { } id || !stale.Contains(id)) continue;

                var logo = (product!["LocalizedProperties"]?[0]?["Images"] as JsonArray ?? [])
                    .Where(i => i?["ImagePurpose"].Str() == "Logo")
                    .OrderByDescending(i => i!["Width"].Long())
                    .FirstOrDefault()?["Uri"].Str();
                if (logo is null) continue;

                // Адрес приходит без схемы («//store-images…»); сервер картинок сам уменьшает до нужного размера.
                var url = (logo.StartsWith("//") ? "https:" + logo : logo) + "?w=96&h=96";
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
        catch (Exception)
        {
            // Нет сети или каталог недоступен — остаются иконки из кэша или свои пиксельные.
        }
        return updated;
    }
}
