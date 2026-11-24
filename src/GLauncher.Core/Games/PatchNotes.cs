using System.Text.Json.Nodes;

namespace GLauncher.Core.Games;

public enum PatchNotesKind { Java, Bedrock, Dungeons, Legends }

public sealed record PatchNoteEntry(string Title, string Version, DateTimeOffset Date, string Type, string ContentPath, string? ImageUrl, string ShortText)
{
    public bool IsSnapshot => Type is "snapshot" or "beta" or "preview";
    public string DateText => Date.LocalDateTime.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
}

/// <summary>Списки изменений с того же сервера, что и у официального лаунчера (launchercontent.mojang.com).</summary>
public static class PatchNotes
{
    private const string Base = "https://launchercontent.mojang.com/v2/";

    private static string FileName(PatchNotesKind kind) => kind switch
    {
        PatchNotesKind.Java => "javaPatchNotes.json",
        PatchNotesKind.Bedrock => "bedrockPatchNotes.json",
        PatchNotesKind.Dungeons => "dungeonsPatchNotes.json",
        PatchNotesKind.Legends => "legendsPatchNotes.json",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static async Task<IReadOnlyList<PatchNoteEntry>> GetAsync(PatchNotesKind kind, CancellationToken ct)
    {
        var cache = Path.Combine(Paths.Cache, "patchnotes", FileName(kind));
        JsonNode json;
        try
        {
            var text = await Http.GetStringAsync(Base + FileName(kind), ct);
            json = JsonNode.Parse(text)!;
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            await File.WriteAllTextAsync(cache, text, ct);
        }
        catch (Exception) when (File.Exists(cache))
        {
            json = JsonNode.Parse(await File.ReadAllTextAsync(cache, ct))!;   // офлайн — последний сохранённый список
        }

        return (json["entries"] as JsonArray ?? [])
            .Where(e => e is not null)
            .Select(e => new PatchNoteEntry(
                e!["title"].Str() ?? "",
                e["version"].Str() ?? "",
                DateTimeOffset.TryParse(e["date"].Str(), out var d) ? d : default,
                (e["type"].Str() ?? e["patchNoteType"].Str() ?? "release").ToLowerInvariant(),
                e["contentPath"].Str() ?? "",
                e["image"]?["url"].Str() is { } img ? new Uri(new Uri(Base), img).ToString() : null,
                e["shortText"].Str() ?? ""))
            .OrderByDescending(e => e.Date)
            .ToList();
    }

    /// <summary>HTML-текст записи.</summary>
    public static async Task<string> GetBodyAsync(PatchNoteEntry entry, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(entry.ContentPath)) return $"<p>{entry.ShortText}</p>";
        var json = await Http.GetJsonAsync(Base + entry.ContentPath, ct);
        return json["body"].Str() ?? $"<p>{entry.ShortText}</p>";
    }
}
