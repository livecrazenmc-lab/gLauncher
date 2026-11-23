using System.Net;
using System.Text.Json.Nodes;
using GLauncher.Core.Java;

namespace GLauncher.Core.Content;

public enum ContentType { Mod, ResourcePack, Shader, Modpack }

public sealed record ModrinthHit(
    string ProjectId, string Slug, string Title, string Description, string Author,
    long Downloads, string? IconUrl, ContentType Type);

public sealed record ModrinthFile(string Url, string FileName, string? Sha1, string? Sha512, long Size, bool Primary);

public sealed record ModrinthDependency(string? ProjectId, string? VersionId, string Type);

public sealed record ModrinthVersion(
    string Id, string ProjectId, string Name, string VersionNumber,
    IReadOnlyList<ModrinthFile> Files, IReadOnlyList<ModrinthDependency> Dependencies)
{
    public ModrinthFile? PrimaryFile => Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();
}

/// <summary>Клиент API Modrinth v2 (без авторизации).</summary>
public static class ModrinthClient
{
    private const string Api = "https://api.modrinth.com/v2";

    public static string ProjectTypeName(ContentType type) => type switch
    {
        ContentType.Mod => "mod",
        ContentType.ResourcePack => "resourcepack",
        ContentType.Shader => "shader",
        ContentType.Modpack => "modpack",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Названия лоадеров Modrinth для модлоадера профиля. Quilt умеет загружать моды Fabric.</summary>
    public static string[] LoadersFor(ModLoader loader) => loader switch
    {
        ModLoader.Fabric => ["fabric"],
        ModLoader.Quilt => ["quilt", "fabric"],
        ModLoader.Forge => ["forge"],
        ModLoader.NeoForge => ["neoforge"],
        _ => [],
    };

    public static async Task<IReadOnlyList<ModrinthHit>> SearchAsync(string query, ContentType type,
        string? gameVersion, string[]? loaders, int offset, int limit, CancellationToken ct)
    {
        var facets = new JsonArray(new JsonArray($"project_type:{ProjectTypeName(type)}"));
        if (!string.IsNullOrEmpty(gameVersion))
            facets.Add(new JsonArray($"versions:{gameVersion}"));
        if (loaders is { Length: > 0 })
            facets.Add(new JsonArray(loaders.Select(l => (JsonNode?)$"categories:{l}").ToArray()));

        var url = $"{Api}/search?query={Uri.EscapeDataString(query)}&limit={limit}&offset={offset}" +
                  $"&index={(string.IsNullOrWhiteSpace(query) ? "downloads" : "relevance")}" +
                  $"&facets={Uri.EscapeDataString(facets.ToJsonString())}";
        var json = await Http.GetJsonAsync(url, ct);

        return (json["hits"] as JsonArray ?? [])
            .Where(h => h is not null)
            .Select(h => new ModrinthHit(
                h!["project_id"].Str()!,
                h["slug"].Str() ?? "",
                h["title"].Str() ?? "",
                h["description"].Str() ?? "",
                h["author"].Str() ?? "",
                h["downloads"].Long(),
                h["icon_url"].Str(),
                type))
            .ToList();
    }

    /// <summary>Самая свежая версия проекта, подходящая под версию игры и лоадер. null — подходящей нет.</summary>
    public static async Task<ModrinthVersion?> GetLatestCompatibleAsync(string projectId, string? gameVersion,
        string[]? loaders, CancellationToken ct)
    {
        var url = $"{Api}/project/{Uri.EscapeDataString(projectId)}/version?include_changelog=false";
        if (loaders is { Length: > 0 })
            url += "&loaders=" + Uri.EscapeDataString(new JsonArray(loaders.Select(l => (JsonNode?)l).ToArray()).ToJsonString());
        if (!string.IsNullOrEmpty(gameVersion))
            url += "&game_versions=" + Uri.EscapeDataString(new JsonArray(gameVersion).ToJsonString());

        var list = await Http.GetJsonAsync(url, ct) as JsonArray ?? [];
        // Ответ отсортирован от новых к старым; релизы предпочтительнее бет и альф.
        var best = list.FirstOrDefault(v => v?["version_type"].Str() == "release") ?? list.FirstOrDefault();
        return best is null ? null : ParseVersion(best);
    }

    public static async Task<ModrinthVersion> GetVersionAsync(string versionId, CancellationToken ct) =>
        ParseVersion(await Http.GetJsonAsync($"{Api}/version/{Uri.EscapeDataString(versionId)}", ct));

    public static async Task<string?> GetProjectTitleAsync(string projectId, CancellationToken ct)
    {
        try
        {
            return (await Http.GetJsonAsync($"{Api}/project/{Uri.EscapeDataString(projectId)}", ct))["title"].Str();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static ModrinthVersion ParseVersion(JsonNode v) => new(
        v["id"].Str()!,
        v["project_id"].Str()!,
        v["name"].Str() ?? "",
        v["version_number"].Str() ?? "",
        (v["files"] as JsonArray ?? [])
            .Where(f => f is not null)
            .Select(f => new ModrinthFile(
                f!["url"].Str()!,
                f["filename"].Str()!,
                f["hashes"]?["sha1"].Str(),
                f["hashes"]?["sha512"].Str(),
                f["size"].Long(),
                f["primary"].Bool()))
            .ToList(),
        (v["dependencies"] as JsonArray ?? [])
            .Where(d => d is not null)
            .Select(d => new ModrinthDependency(d!["project_id"].Str(), d["version_id"].Str(), d["dependency_type"].Str() ?? ""))
            .ToList());
}
