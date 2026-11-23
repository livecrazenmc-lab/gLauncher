using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GLauncher.Core.Java;

namespace GLauncher.Core.Content;

/// <summary>Импорт и экспорт сборок в формате Modrinth (.mrpack).</summary>
public static class MrpackService
{
    private const string IndexFile = "modrinth.index.json";

    private static readonly Regex ModrinthCdn = new(
        @"^https://cdn\.modrinth\.com/data/(?<project>[A-Za-z0-9]+)/versions/(?<version>[A-Za-z0-9]+)/", RegexOptions.Compiled);

    private static readonly (string Key, ModLoader Loader)[] LoaderKeys =
    [
        ("fabric-loader", ModLoader.Fabric),
        ("quilt-loader", ModLoader.Quilt),
        ("forge", ModLoader.Forge),
        ("neoforge", ModLoader.NeoForge),
    ];

    /// <summary>Создаёт новый профиль из файла .mrpack и скачивает все файлы сборки.</summary>
    public static async Task<Profile> ImportAsync(string mrpackPath, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        progress?.Report(new("Чтение сборки", 0, 0));
        using var zip = ZipFile.OpenRead(mrpackPath);
        var indexEntry = zip.GetEntry(IndexFile) ?? throw new LauncherException("Это не сборка Modrinth: нет modrinth.index.json.");
        JsonNode index;
        using (var stream = indexEntry.Open())
            index = await JsonNode.ParseAsync(stream, cancellationToken: ct) ?? throw new LauncherException("Пустой modrinth.index.json.");

        if (index["game"].Str() != "minecraft")
            throw new LauncherException("Сборка предназначена не для Minecraft.");

        var deps = index["dependencies"];
        var profile = new Profile
        {
            Name = index["name"].Str() ?? Path.GetFileNameWithoutExtension(mrpackPath),
            MinecraftVersion = deps?["minecraft"].Str() ?? throw new LauncherException("В сборке не указана версия Minecraft."),
        };
        foreach (var (key, loader) in LoaderKeys)
        {
            if (deps?[key].Str() is not { } version) continue;
            profile.Loader = loader;
            profile.LoaderVersion = version;
            break;
        }

        var gameDir = profile.ResolveGameDir();
        Directory.CreateDirectory(gameDir);
        var root = Path.GetFullPath(gameDir) + Path.DirectorySeparatorChar;

        // --- Файлы с внешних серверов ---
        var downloads = new List<DownloadItem>();
        var manifest = new Dictionary<string, InstalledContent>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in index["files"] as JsonArray ?? [])
        {
            if (file is null || file["env"]?["client"].Str() == "unsupported") continue;
            var rel = file["path"].Str();
            var url = (file["downloads"] as JsonArray)?.FirstOrDefault().Str();
            if (rel is null || url is null) continue;

            var target = SafePath(root, rel);
            var sha1 = file["hashes"]?["sha1"].Str();
            var size = file["fileSize"].Long();
            downloads.Add(new DownloadItem(url, target, sha1, size));

            var match = ModrinthCdn.Match(url);
            if (match.Success)
            {
                ContentManager.Record(manifest, rel, new InstalledContent
                {
                    ProjectId = match.Groups["project"].Value,
                    VersionId = match.Groups["version"].Value,
                    Title = Path.GetFileNameWithoutExtension(rel),
                    DownloadUrl = url,
                    Sha1 = sha1,
                    Sha512 = file["hashes"]?["sha512"].Str(),
                    Size = size,
                });
            }
        }
        await Downloader.DownloadAllAsync(downloads, "Загрузка файлов сборки", progress, ct);

        // --- Файлы внутри архива: overrides, затем client-overrides поверх ---
        progress?.Report(new("Распаковка настроек сборки", 0, 0));
        foreach (var prefix in new[] { "overrides/", "client-overrides/" })
        {
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.FullName.EndsWith('/')) continue;
                var target = SafePath(root, entry.FullName[prefix.Length..]);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        new ContentManager(profile).Save(manifest);
        return profile;
    }

    /// <summary>Скачивает сборку с Modrinth (последнюю версию) и импортирует её.</summary>
    public static async Task<Profile> InstallFromModrinthAsync(string projectId, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        progress?.Report(new("Поиск версии сборки", 0, 0));
        var version = await ModrinthClient.GetLatestCompatibleAsync(projectId, null, null, ct)
                      ?? throw new LauncherException("У сборки нет опубликованных версий.");
        var file = version.Files.FirstOrDefault(f => f.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                   ?? throw new LauncherException("В версии сборки нет файла .mrpack.");

        var path = Path.Combine(Paths.Cache, "modpacks", file.FileName);
        progress?.Report(new($"Загрузка {file.FileName}", 0, 0));
        await Downloader.DownloadIfNeededAsync(new DownloadItem(file.Url, path, file.Sha1, file.Size), ct);
        return await ImportAsync(path, progress, ct);
    }

    /// <summary>
    /// Экспортирует профиль в .mrpack. Файлы, установленные с Modrinth, попадают в список загрузок,
    /// остальное (свои моды, config, options.txt) — в overrides.
    /// </summary>
    public static async Task ExportAsync(Profile profile, string outputPath, string packVersion,
        IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.MinecraftVersion))
            throw new LauncherException("В профиле не выбрана версия Minecraft.");
        if (profile.Loader != ModLoader.Vanilla && string.IsNullOrWhiteSpace(profile.LoaderVersion))
            throw new LauncherException("Версия модлоадера ещё не известна: запустите профиль один раз или укажите её вручную.");

        progress?.Report(new("Экспорт сборки", 0, 0));
        var content = new ContentManager(profile);
        var manifest = content.LoadManifest();
        var gameDir = profile.ResolveGameDir();

        var files = new JsonArray();
        var overrides = new List<(string Source, string EntryName)>();

        foreach (var type in new[] { ContentType.Mod, ContentType.ResourcePack, ContentType.Shader })
        {
            foreach (var item in content.List(type).Where(i => i.Enabled))
            {
                var rel = $"{ContentManager.FolderName(type)}/{item.FileName}";
                if (item.Source is { DownloadUrl.Length: > 0, Sha1: not null, Sha512: not null } src &&
                    Downloader.Sha1(item.FullPath).Equals(src.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(new JsonObject
                    {
                        ["path"] = rel,
                        ["hashes"] = new JsonObject { ["sha1"] = src.Sha1, ["sha512"] = src.Sha512 },
                        ["env"] = new JsonObject { ["client"] = "required", ["server"] = type == ContentType.Mod ? "required" : "unsupported" },
                        ["downloads"] = new JsonArray(src.DownloadUrl),
                        ["fileSize"] = new FileInfo(item.FullPath).Length,
                    });
                }
                else
                {
                    overrides.Add((item.FullPath, "overrides/" + rel));
                }
            }
        }

        var configDir = Path.Combine(gameDir, "config");
        if (Directory.Exists(configDir))
        {
            foreach (var path in Directory.EnumerateFiles(configDir, "*", SearchOption.AllDirectories))
                overrides.Add((path, "overrides/" + Path.GetRelativePath(gameDir, path).Replace('\\', '/')));
        }
        var options = Path.Combine(gameDir, "options.txt");
        if (File.Exists(options)) overrides.Add((options, "overrides/options.txt"));

        var dependencies = new JsonObject { ["minecraft"] = profile.MinecraftVersion };
        var loaderKey = LoaderKeys.FirstOrDefault(k => k.Loader == profile.Loader).Key;
        if (loaderKey is not null) dependencies[loaderKey] = profile.LoaderVersion;

        var index = new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["versionId"] = packVersion,
            ["name"] = profile.Name,
            ["files"] = files,
            ["dependencies"] = dependencies,
        };

        var tmp = outputPath + ".tmp";
        await using (var fs = File.Create(tmp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var indexEntry = zip.CreateEntry(IndexFile);
            await using (var s = indexEntry.Open())
                await s.WriteAsync(Encoding.UTF8.GetBytes(index.ToJsonString(JsonFile.Options)), ct);

            foreach (var (source, entryName) in overrides)
            {
                ct.ThrowIfCancellationRequested();
                zip.CreateEntryFromFile(source, entryName, CompressionLevel.Optimal);
            }
        }
        File.Move(tmp, outputPath, overwrite: true);
    }

    /// <summary>Путь внутри папки игры; защищает от «../» и абсолютных путей в сборке.</summary>
    private static string SafePath(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new LauncherException($"Сборка содержит недопустимый путь: {relative}");
        return full;
    }
}
