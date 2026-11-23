using GLauncher.Core.Java;

namespace GLauncher.Core.Content;

/// <summary>Сведения о файле, установленном из Modrinth (нужны для отметки «установлено», обновления и экспорта .mrpack).</summary>
public sealed class InstalledContent
{
    public string ProjectId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public string Title { get; set; } = "";
    public string VersionNumber { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string? Sha1 { get; set; }
    public string? Sha512 { get; set; }
    public long Size { get; set; }
}

public sealed record InstalledItem(string FileName, string FullPath, bool Enabled, ContentType Type, InstalledContent? Source)
{
    public string Title => Source?.Title ?? DisplayFileName;
    public string DisplayFileName => Enabled ? FileName : FileName[..^ContentManager.DisabledSuffix.Length];
    public string? Version => Source?.VersionNumber;
}

/// <summary>Моды, ресурспаки и шейдеры одного профиля.</summary>
public sealed class ContentManager(Profile profile)
{
    public const string DisabledSuffix = ".disabled";

    private string GameDir => profile.ResolveGameDir();
    private string ManifestPath => Path.Combine(GameDir, ".glauncher", "content.json");

    public static string FolderName(ContentType type) => type switch
    {
        ContentType.Mod => "mods",
        ContentType.ResourcePack => "resourcepacks",
        ContentType.Shader => "shaderpacks",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public string Folder(ContentType type) => Path.Combine(GameDir, FolderName(type));

    /// <summary>Ключ манифеста: «mods/sodium.jar» (без «.disabled»).</summary>
    private static string Key(ContentType type, string fileName) =>
        $"{FolderName(type)}/{(fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase) ? fileName[..^DisabledSuffix.Length] : fileName)}";

    public Dictionary<string, InstalledContent> LoadManifest() =>
        JsonFile.Load<Dictionary<string, InstalledContent>>(ManifestPath) ?? new(StringComparer.OrdinalIgnoreCase);

    private void SaveManifest(Dictionary<string, InstalledContent> manifest) => JsonFile.Save(ManifestPath, manifest);

    public IReadOnlyList<InstalledItem> List(ContentType type)
    {
        var folder = Folder(type);
        if (!Directory.Exists(folder)) return [];

        var manifest = LoadManifest();
        var extensions = type == ContentType.Mod ? new[] { ".jar" } : new[] { ".zip" };

        return Directory.EnumerateFiles(folder)
            .Select(path => Path.GetFileName(path))
            .Select(name =>
            {
                var enabled = !name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                var baseName = enabled ? name : name[..^DisabledSuffix.Length];
                return (name, enabled, baseName);
            })
            .Where(x => extensions.Any(e => x.baseName.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            .Select(x => new InstalledItem(x.name, Path.Combine(folder, x.name), x.enabled, type,
                manifest.GetValueOrDefault(Key(type, x.name))))
            .OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public HashSet<string> InstalledProjectIds(ContentType type)
    {
        var prefix = FolderName(type) + "/";
        return LoadManifest()
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                         (File.Exists(Path.Combine(GameDir, kv.Key)) || File.Exists(Path.Combine(GameDir, kv.Key + DisabledSuffix))))
            .Select(kv => kv.Value.ProjectId)
            .ToHashSet();
    }

    public void SetEnabled(InstalledItem item, bool enabled)
    {
        if (item.Enabled == enabled) return;
        var target = enabled ? item.FullPath[..^DisabledSuffix.Length] : item.FullPath + DisabledSuffix;
        File.Move(item.FullPath, target, overwrite: true);
    }

    public void Delete(InstalledItem item)
    {
        File.Delete(item.FullPath);
        var manifest = LoadManifest();
        if (manifest.Remove(Key(item.Type, item.FileName))) SaveManifest(manifest);
    }

    /// <summary>
    /// Ставит последнюю подходящую версию проекта (и обязательные зависимости для модов).
    /// Если проект уже стоит — заменяет файл. Возвращает названия установленного.
    /// </summary>
    public async Task<IReadOnlyList<string>> InstallAsync(string projectId, ContentType type,
        IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        if (type == ContentType.Mod && profile.Loader == ModLoader.Vanilla)
            throw new LauncherException("Для модов выберите в профиле модлоадер (Fabric, Quilt, Forge или NeoForge).");
        if (string.IsNullOrWhiteSpace(profile.MinecraftVersion))
            throw new LauncherException("В профиле не выбрана версия Minecraft.");

        var installed = new List<string>();
        var visited = new HashSet<string>();
        var manifest = LoadManifest();
        await InstallRecursiveAsync(projectId, null, type, manifest, visited, installed, progress, ct, isDependency: false);
        SaveManifest(manifest);
        return installed;
    }

    private async Task InstallRecursiveAsync(string projectId, string? versionId, ContentType type,
        Dictionary<string, InstalledContent> manifest, HashSet<string> visited, List<string> installed,
        IProgress<ProgressInfo>? progress, CancellationToken ct, bool isDependency)
    {
        if (!visited.Add(projectId)) return;

        // Зависимость уже установлена — не трогаем.
        var prefix = FolderName(type) + "/";
        var existing = manifest.FirstOrDefault(kv => kv.Value.ProjectId == projectId &&
                                                     kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (isDependency && existing.Value is not null &&
            (File.Exists(Path.Combine(GameDir, existing.Key)) || File.Exists(Path.Combine(GameDir, existing.Key + DisabledSuffix))))
            return;

        var loaders = type == ContentType.Mod ? ModrinthClient.LoadersFor(profile.Loader) : null;
        var version = versionId is not null
            ? await ModrinthClient.GetVersionAsync(versionId, ct)
            : await ModrinthClient.GetLatestCompatibleAsync(projectId, profile.MinecraftVersion, loaders, ct);
        if (version is null)
        {
            var title = await ModrinthClient.GetProjectTitleAsync(projectId, ct) ?? projectId;
            throw new LauncherException($"«{title}» нет для Minecraft {profile.MinecraftVersion}" +
                                        (loaders is { Length: > 0 } ? $" ({profile.Loader})." : "."));
        }

        var file = version.PrimaryFile ?? throw new LauncherException("У версии на Modrinth нет файлов.");
        var projectTitle = await ModrinthClient.GetProjectTitleAsync(projectId, ct) ?? version.Name;
        progress?.Report(new($"Загрузка: {projectTitle}", 0, 0));

        var folder = Folder(type);
        var target = Path.Combine(folder, file.FileName);
        if (!Path.GetFullPath(target).StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            throw new LauncherException("Недопустимое имя файла.");
        await Downloader.DownloadFileAsync(new DownloadItem(file.Url, target, file.Sha1, file.Size), ct);

        // Удалить прежнюю версию этого же проекта.
        if (existing.Value is not null && !existing.Key.Equals(Key(type, file.FileName), StringComparison.OrdinalIgnoreCase))
        {
            foreach (var old in new[] { existing.Key, existing.Key + DisabledSuffix })
            {
                var path = Path.Combine(GameDir, old);
                if (File.Exists(path)) File.Delete(path);
            }
            manifest.Remove(existing.Key);
        }

        manifest[Key(type, file.FileName)] = new InstalledContent
        {
            ProjectId = projectId,
            VersionId = version.Id,
            Title = projectTitle,
            VersionNumber = version.VersionNumber,
            DownloadUrl = file.Url,
            Sha1 = file.Sha1,
            Sha512 = file.Sha512,
            Size = file.Size,
        };
        installed.Add(projectTitle);

        if (type != ContentType.Mod) return;
        foreach (var dep in version.Dependencies.Where(d => d.Type == "required" && d.ProjectId is not null))
            await InstallRecursiveAsync(dep.ProjectId!, dep.VersionId, type, manifest, visited, installed, progress, ct, isDependency: true);
    }

    /// <summary>Записать в манифест файл, пришедший из сборки .mrpack.</summary>
    internal static void Record(Dictionary<string, InstalledContent> manifest, string relativePath, InstalledContent content) =>
        manifest[relativePath.Replace('\\', '/')] = content;

    internal void Save(Dictionary<string, InstalledContent> manifest) => SaveManifest(manifest);
}
