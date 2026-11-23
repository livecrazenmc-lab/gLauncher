using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GLauncher.Core;

/// <summary>Ошибка, текст которой можно показать пользователю как есть.</summary>
public class LauncherException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Состояние длительной операции. Total == 0 — прогресс неизвестен.</summary>
public readonly record struct ProgressInfo(string Stage, int Done, int Total);

public static class Http
{
    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 16,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"gLauncher/{version?.ToString(3) ?? "0"}");
        return client;
    }

    /// <summary>GET JSON с повторами при сетевых сбоях (обрыв соединения, таймаут, 5xx).</summary>
    public static async Task<JsonNode> GetJsonAsync(string url, CancellationToken ct = default, string? bearer = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await GetJsonOnceAsync(url, ct, bearer);
            }
            catch (Exception ex) when (attempt < 3 && !ct.IsCancellationRequested && IsTransient(ex))
            {
                await Task.Delay(700 * attempt, ct);
            }
        }
    }

    /// <summary>GET текста с теми же повторами, что и <see cref="GetJsonAsync"/>.</summary>
    public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await Client.GetStringAsync(url, ct);
            }
            catch (Exception ex) when (attempt < 3 && !ct.IsCancellationRequested && IsTransient(ex))
            {
                await Task.Delay(700 * attempt, ct);
            }
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } code } => (int)code >= 500,
        TaskCanceledException => true, // таймаут HttpClient
        _ => false,
    };

    private static async Task<JsonNode> GetJsonOnceAsync(string url, CancellationToken ct, string? bearer)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (bearer is not null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var resp = await Client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"GET {url} → {(int)resp.StatusCode}", null, resp.StatusCode);
        return JsonNode.Parse(body) ?? throw new LauncherException($"Пустой ответ от {url}");
    }
}

public static class JsonExt
{
    /// <summary>Строковое значение узла или null.</summary>
    public static string? Str(this JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node?.ToString();

    public static long Long(this JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<long>(out var l) ? l : 0;

    public static bool Bool(this JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}

public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // кириллица без \uXXXX
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T? Load<T>(string path)
    {
        if (!File.Exists(path)) return default;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options); }
        catch (JsonException) { return default; }
    }

    public static void Save<T>(string path, T value) =>
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(value, Options));

    public static void WriteAtomic(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}
