using System.IO.Compression;
using System.Text.Json.Nodes;

namespace GLauncher.Core.Java;

public sealed class PreparedGame
{
    public required JsonObject Json { get; init; }
    public required string VersionId { get; init; }
    public required List<string> Classpath { get; init; }
    public required string NativesDir { get; init; }
    public required string AssetIndexId { get; init; }
    public string? GameAssetsDir { get; init; }
    public string? LoggingArgument { get; init; }
    public required string JavaComponent { get; init; }
}

/// <summary>Скачивает всё, что нужно версии: библиотеки, клиент, ассеты, нативные библиотеки.</summary>
public static class GameInstaller
{
    private const string MojangLibraries = "https://libraries.minecraft.net/";
    private const string AssetsUrl = "https://resources.download.minecraft.net/";

    public static async Task<PreparedGame> PrepareAsync(string versionId, string gameDir,
        IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        progress?.Report(new("Чтение описания версии", 0, 0));
        var json = await VersionRepository.ResolveAsync(versionId, ct);

        var downloads = new List<DownloadItem>();
        var classpath = new List<string>();
        var natives = new List<(string Jar, string[] Exclude)>();
        var seen = new HashSet<string>();

        // --- Библиотеки ---
        foreach (var lib in json["libraries"] as JsonArray ?? [])
        {
            if (lib is null || !Rules.Allowed(lib["rules"])) continue;
            var name = lib["name"].Str();
            if (name is null) continue;

            // Старый формат нативных библиотек (до 1.19): отдельный classifier, распаковывается в папку natives.
            if (lib["natives"]?["windows"].Str() is { } nativeClassifier)
            {
                var classifier = nativeClassifier.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                var item = lib["downloads"]?["classifiers"]?[classifier] is { } art
                    ? FromArtifact(art)
                    : FromMaven(lib, name + ":" + classifier);
                downloads.Add(item);
                var exclude = (lib["extract"]?["exclude"] as JsonArray ?? [])
                    .Select(e => e.Str()).OfType<string>().ToArray();
                natives.Add((item.Path, exclude));

                if (lib["downloads"]?["artifact"] is null) continue;
            }

            if (!seen.Add(Maven.Key(name))) continue;

            var download = lib["downloads"]?["artifact"] is { } artifact ? FromArtifact(artifact) : FromMaven(lib, name);
            if (!string.IsNullOrEmpty(download.Url)) downloads.Add(download);
            classpath.Add(download.Path);
        }

        // --- Клиент ---
        var jarId = json["jar"].Str() ?? versionId;
        var jarPath = Paths.VersionJar(jarId);
        if (json["downloads"]?["client"] is { } client)
            downloads.Add(new DownloadItem(client["url"].Str()!, jarPath, client["sha1"].Str(), client["size"].Long()));
        classpath.Add(jarPath);

        // --- Конфиг логирования (заодно закрывает Log4Shell на старых версиях) ---
        string? loggingArg = null;
        if (json["logging"]?["client"] is { } logging && logging["file"] is { } logFile)
        {
            var path = Path.Combine(Paths.Assets, "log_configs", logFile["id"].Str()!);
            downloads.Add(new DownloadItem(logFile["url"].Str()!, path, logFile["sha1"].Str(), logFile["size"].Long()));
            loggingArg = logging["argument"].Str()?.Replace("${path}", path);
        }

        // --- Ассеты ---
        var assetIndex = json["assetIndex"] ?? throw new LauncherException("В описании версии нет assetIndex.");
        var indexId = assetIndex["id"].Str()!;
        var indexPath = Path.Combine(Paths.Assets, "indexes", indexId + ".json");
        await Downloader.DownloadIfNeededAsync(new DownloadItem(assetIndex["url"].Str()!, indexPath,
            assetIndex["sha1"].Str(), assetIndex["size"].Long()), ct);
        var index = JsonNode.Parse(await File.ReadAllTextAsync(indexPath, ct))!;

        var objects = new List<(string Name, string Path)>();
        foreach (var (assetName, obj) in index["objects"] as JsonObject ?? [])
        {
            var hash = obj?["hash"].Str();
            if (hash is null) continue;
            var path = Path.Combine(Paths.Assets, "objects", hash[..2], hash);
            downloads.Add(new DownloadItem($"{AssetsUrl}{hash[..2]}/{hash}", path, hash, obj!["size"].Long()));
            objects.Add((assetName, path));
        }

        await Downloader.DownloadAllAsync(downloads, "Загрузка файлов игры", progress, ct);

        // --- Распаковка нативных библиотек ---
        progress?.Report(new("Подготовка нативных библиотек", 0, 0));
        var nativesDir = Path.Combine(Paths.VersionDir(versionId), "natives");
        ExtractNatives(natives, nativesDir);

        // --- Старые версии (до 1.7.10) ждут ассеты в виде обычных файлов ---
        string? gameAssets = null;
        if (index["virtual"].Bool())
            gameAssets = CopyLegacyAssets(objects, Path.Combine(Paths.Assets, "virtual", indexId));
        else if (index["map_to_resources"].Bool())
            gameAssets = CopyLegacyAssets(objects, Path.Combine(gameDir, "resources"));

        return new PreparedGame
        {
            Json = json,
            VersionId = versionId,
            Classpath = classpath,
            NativesDir = nativesDir,
            AssetIndexId = indexId,
            GameAssetsDir = gameAssets,
            LoggingArgument = loggingArg,
            JavaComponent = json["javaVersion"]?["component"].Str() ?? "jre-legacy",
        };
    }

    private static DownloadItem FromArtifact(JsonNode art) => new(
        art["url"].Str() ?? "",
        Path.Combine(Paths.Libraries, art["path"].Str()!.Replace('/', Path.DirectorySeparatorChar)),
        art["sha1"].Str(),
        art["size"].Long());

    private static DownloadItem FromMaven(JsonNode lib, string name)
    {
        var rel = Maven.ToPath(name);
        var baseUrl = lib["url"].Str() ?? MojangLibraries;
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        return new DownloadItem(baseUrl + rel, Path.Combine(Paths.Libraries, rel.Replace('/', Path.DirectorySeparatorChar)),
            lib["sha1"].Str(), lib["size"].Long());
    }

    private static void ExtractNatives(List<(string Jar, string[] Exclude)> natives, string dir)
    {
        Directory.CreateDirectory(dir);
        var root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;

        foreach (var (jar, exclude) in natives)
        {
            using var zip = ZipFile.OpenRead(jar);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                if (exclude.Any(e => entry.FullName.StartsWith(e, StringComparison.OrdinalIgnoreCase))) continue;

                var dest = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // защита от «../»
                if (File.Exists(dest) && new FileInfo(dest).Length == entry.Length) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                try { entry.ExtractToFile(dest, overwrite: true); }
                catch (IOException) { /* файл занят уже запущенной игрой той же версии */ }
            }
        }
    }

    private static string CopyLegacyAssets(List<(string Name, string Path)> objects, string targetDir)
    {
        foreach (var (name, source) in objects)
        {
            var dest = Path.Combine(targetDir, name.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(dest)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest);
        }
        return targetDir;
    }
}
