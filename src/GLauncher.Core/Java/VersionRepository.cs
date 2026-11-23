using System.Text.Json.Nodes;

namespace GLauncher.Core.Java;

public sealed record MinecraftVersionInfo(string Id, string Type, DateTimeOffset ReleaseTime);

/// <summary>Список версий Mojang и JSON-описания версий (с поддержкой inheritsFrom).</summary>
public static class VersionRepository
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static JsonNode? _manifest;

    private static string ManifestCache => Path.Combine(Paths.Versions, "version_manifest_v2.json");

    public static async Task<JsonNode> GetManifestAsync(CancellationToken ct = default, bool refresh = false)
    {
        if (_manifest is not null && !refresh) return _manifest;
        try
        {
            var text = await Http.Client.GetStringAsync(ManifestUrl, ct);
            _manifest = JsonNode.Parse(text)!;
            Directory.CreateDirectory(Paths.Versions);
            await File.WriteAllTextAsync(ManifestCache, text, ct);
        }
        catch (HttpRequestException) when (File.Exists(ManifestCache))
        {
            _manifest = JsonNode.Parse(await File.ReadAllTextAsync(ManifestCache, ct))!;
        }
        return _manifest;
    }

    public static async Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken ct = default)
    {
        var manifest = await GetManifestAsync(ct);
        return (manifest["versions"] as JsonArray ?? [])
            .Where(v => v is not null)
            .Select(v => new MinecraftVersionInfo(
                v!["id"].Str()!,
                v["type"].Str() ?? "release",
                DateTimeOffset.TryParse(v["releaseTime"].Str(), out var t) ? t : default))
            .ToList();
    }

    public static async Task<JsonObject> GetVersionJsonAsync(string id, CancellationToken ct)
    {
        var path = Paths.VersionJson(id);
        if (!File.Exists(path))
        {
            var manifest = await GetManifestAsync(ct);
            var entry = (manifest["versions"] as JsonArray ?? []).FirstOrDefault(v => v?["id"].Str() == id)
                        ?? throw new LauncherException($"Версия «{id}» не найдена.");
            await Downloader.DownloadFileAsync(new DownloadItem(entry["url"].Str()!, path, entry["sha1"].Str()), ct);
        }
        return JsonNode.Parse(await File.ReadAllTextAsync(path, ct)) as JsonObject
               ?? throw new LauncherException($"Файл версии «{id}» повреждён.");
    }

    /// <summary>JSON версии, объединённый со всеми родителями (inheritsFrom).</summary>
    public static async Task<JsonObject> ResolveAsync(string id, CancellationToken ct, int depth = 0)
    {
        if (depth > 10) throw new LauncherException("Слишком глубокая цепочка inheritsFrom.");
        var json = await GetVersionJsonAsync(id, ct);
        var parentId = json["inheritsFrom"].Str();
        if (parentId is null) return json;

        var parent = await ResolveAsync(parentId, ct, depth + 1);
        return Merge(parent, json);
    }

    private static JsonObject Merge(JsonObject parent, JsonObject child)
    {
        var result = (JsonObject)parent.DeepClone();

        foreach (var (key, value) in child)
        {
            switch (key)
            {
                case "inheritsFrom":
                    break;

                case "libraries":
                    // Библиотеки потомка идут первыми — при дублях выигрывают они.
                    var libs = new JsonArray();
                    foreach (var lib in value as JsonArray ?? []) libs.Add(lib?.DeepClone());
                    foreach (var lib in parent["libraries"] as JsonArray ?? []) libs.Add(lib?.DeepClone());
                    result["libraries"] = libs;
                    break;

                case "arguments" when value is JsonObject childArgs:
                    if (result["arguments"] is not JsonObject resultArgs)
                    {
                        resultArgs = new JsonObject();
                        result["arguments"] = resultArgs;
                    }
                    foreach (var (kind, list) in childArgs)
                    {
                        var merged = new JsonArray();
                        foreach (var a in resultArgs[kind] as JsonArray ?? []) merged.Add(a?.DeepClone());
                        foreach (var a in list as JsonArray ?? []) merged.Add(a?.DeepClone());
                        resultArgs[kind] = merged;
                    }
                    break;

                default:
                    result[key] = value?.DeepClone();
                    break;
            }
        }
        return result;
    }
}
