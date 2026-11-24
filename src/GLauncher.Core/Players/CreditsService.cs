using System.Text.Json;
using System.Text.Json.Nodes;

namespace GLauncher.Core.Players;

/// <summary>Человек из благодарностей. Uuid — без дефисов, в нижнем регистре.</summary>
public sealed record CreditPerson(string Uuid, string Role, string? Greeting);

/// <summary>
/// Список благодарностей. Источник — credits.json в репозитории на GitHub: лаунчер скачивает его при запуске,
/// поэтому список можно менять без нового релиза. Без сети — последняя скачанная копия, а если её нет —
/// копия, встроенная в сборку.
/// </summary>
public static class CreditsService
{
    private const string RemoteUrl = "https://raw.githubusercontent.com/livecrazenmc-lab/gLauncher/main/credits.json";
    private static string CacheFile => Path.Combine(Paths.Cache, "credits.json");

    public static IReadOnlyList<CreditPerson> People { get; private set; } = LoadLocal();

    /// <summary>Список обновился (скачан свежий credits.json).</summary>
    public static event Action? Changed;

    public static CreditPerson? Find(string? uuid)
    {
        if (string.IsNullOrEmpty(uuid)) return null;
        var id = PlayerProfileService.Normalize(uuid);
        return People.FirstOrDefault(p => p.Uuid == id);
    }

    public static async Task RefreshAsync(CancellationToken ct)
    {
        string text;
        try
        {
            text = await Http.GetStringAsync(RemoteUrl, ct);
        }
        catch (Exception)
        {
            return; // офлайн или GitHub недоступен — остаётся текущий список
        }

        var people = Parse(text);
        if (people is null) return; // битый файл в репозитории — не ломаем лаунчер
        Directory.CreateDirectory(Paths.Cache);
        await File.WriteAllTextAsync(CacheFile, text, ct);

        if (!people.SequenceEqual(People))
        {
            People = people;
            Changed?.Invoke();
        }
    }

    private static IReadOnlyList<CreditPerson> LoadLocal()
    {
        try
        {
            if (File.Exists(CacheFile) && Parse(File.ReadAllText(CacheFile)) is { } cached) return cached;
        }
        catch (IOException) { }

        using var stream = typeof(CreditsService).Assembly.GetManifestResourceStream("credits.json");
        if (stream is null) return [];
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd()) ?? [];
    }

    private static IReadOnlyList<CreditPerson>? Parse(string json)
    {
        try
        {
            var people = (JsonNode.Parse(json)?["people"] as JsonArray ?? [])
                .Where(p => p?["uuid"].Str() is { Length: >= 32 })
                .Select(p => new CreditPerson(
                    PlayerProfileService.Normalize(p!["uuid"].Str()!),
                    p["role"].Str() ?? "Спасибо!",
                    p["greeting"].Str()))
                .ToList();
            return people;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
