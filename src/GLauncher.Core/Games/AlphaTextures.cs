using System.IO.Compression;

namespace GLauncher.Core.Games;

/// <summary>
/// Текстуры Minecraft Alpha для иконок Java и Bedrock (дёрн сбоку). Это материалы Mojang, поэтому в репозиторий
/// и сборку они не попадают: лаунчер один раз скачивает клиент Alpha a1.2.6 с серверов Mojang (ссылка и SHA-1 —
/// из официального списка версий), достаёт из него terrain.png в кэш, а сам jar удаляет.
/// </summary>
public static class AlphaTextures
{
    private const string ClientUrl = "https://launcher.mojang.com/v1/objects/a68c817afd6c05c253ba5462287c2c19bbb57935/client.jar";
    private const string ClientSha1 = "a68c817afd6c05c253ba5462287c2c19bbb57935";

    public static string TerrainPath => Path.Combine(Paths.Cache, "art", "alpha-terrain.png");

    /// <summary>terrain.png Alpha из кэша, если уже извлечён.</summary>
    public static string? Cached => File.Exists(TerrainPath) ? TerrainPath : null;

    /// <summary>Скачивает и извлекает terrain.png, если его ещё нет. true — появился новый файл. Ошибки сети не бросает.</summary>
    public static async Task<bool> EnsureAsync(CancellationToken ct)
    {
        if (File.Exists(TerrainPath)) return false;
        var jar = Path.Combine(Paths.Cache, "art", "alpha-client.jar");
        try
        {
            await Downloader.DownloadFileAsync(new DownloadItem(ClientUrl, jar, ClientSha1), ct);
            using (var zip = ZipFile.OpenRead(jar))
            {
                var entry = zip.GetEntry("terrain.png") ?? throw new IOException("В клиенте Alpha нет terrain.png");
                var tmp = TerrainPath + ".part";
                entry.ExtractToFile(tmp, overwrite: true);
                File.Move(tmp, TerrainPath, overwrite: true);
            }
            return true;
        }
        catch (Exception)
        {
            return false; // нет сети — остаётся своя текстура дёрна
        }
        finally
        {
            try { if (File.Exists(jar)) File.Delete(jar); } catch { }
        }
    }
}
