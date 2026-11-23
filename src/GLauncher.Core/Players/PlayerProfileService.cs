using System.Text;
using System.Text.Json.Nodes;

namespace GLauncher.Core.Players;

/// <summary>Ник и скин игрока Java Edition. SkinPath — PNG скина в кэше или null (стандартный скин).</summary>
public sealed record PlayerProfile(string Uuid, string Name, string? SkinPath);

/// <summary>
/// Ники и скины по UUID через публичный sessionserver Mojang (без авторизации).
/// Последний удачный ответ хранится в cache\players, чтобы показывать его офлайн.
/// </summary>
public static class PlayerProfileService
{
    private const string SessionServer = "https://sessionserver.mojang.com/session/minecraft/profile/";

    private static string CacheDir => Path.Combine(Paths.Cache, "players");

    public static string Normalize(string uuid) => uuid.Replace("-", "").Trim().ToLowerInvariant();

    /// <summary>Профиль из кэша (без сети) или null.</summary>
    public static PlayerProfile? LoadCached(string uuid)
    {
        var id = Normalize(uuid);
        var json = JsonFile.Load<JsonObject>(Path.Combine(CacheDir, id + ".json"));
        var name = json?["name"].Str();
        if (name is null) return null;
        var skin = Path.Combine(CacheDir, id + ".png");
        return new PlayerProfile(id, name, File.Exists(skin) ? skin : null);
    }

    /// <summary>Свежие ник и скин с серверов Mojang; обновляет кэш.</summary>
    public static async Task<PlayerProfile> FetchAsync(string uuid, CancellationToken ct)
    {
        var id = Normalize(uuid);
        var json = await Http.GetJsonAsync(SessionServer + id, ct);
        var name = json["name"].Str() ?? throw new LauncherException("Профиль игрока не найден.");

        string? skinUrl = null;
        var textures = (json["properties"] as JsonArray ?? [])
            .FirstOrDefault(p => p?["name"].Str() == "textures")?["value"].Str();
        if (textures is not null)
        {
            var decoded = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(textures)));
            skinUrl = decoded?["textures"]?["SKIN"]?["url"].Str();
        }

        Directory.CreateDirectory(CacheDir);
        var skinPath = Path.Combine(CacheDir, id + ".png");
        if (skinUrl is not null)
        {
            // Mojang отдаёт ссылку по http — сервер текстур работает и по https.
            if (skinUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) skinUrl = "https://" + skinUrl[7..];
            await Downloader.DownloadFileAsync(new DownloadItem(skinUrl, skinPath), ct);
        }
        else if (File.Exists(skinPath))
        {
            File.Delete(skinPath); // игрок вернулся к стандартному скину
        }

        JsonFile.Save(Path.Combine(CacheDir, id + ".json"), new JsonObject { ["name"] = name, ["skin"] = skinUrl });
        return new PlayerProfile(id, name, skinUrl is null ? null : skinPath);
    }
}
