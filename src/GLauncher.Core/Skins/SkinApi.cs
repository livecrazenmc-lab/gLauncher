using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GLauncher.Core.Skins;

public sealed record AccountSkin(string Id, string Url, SkinModel Model, bool Active);

public sealed record AccountCape(string Id, string Url, string Alias, bool Active);

public sealed record AccountTextures(IReadOnlyList<AccountSkin> Skins, IReadOnlyList<AccountCape> Capes)
{
    public AccountSkin? ActiveSkin => Skins.FirstOrDefault(s => s.Active);
    public AccountCape? ActiveCape => Capes.FirstOrDefault(c => c.Active);
}

/// <summary>Скины и плащи аккаунта Java Edition через Minecraft Services (нужен токен Minecraft).</summary>
public static class SkinApi
{
    private const string Profile = "https://api.minecraftservices.com/minecraft/profile";

    public static async Task<AccountTextures> GetAsync(string token, CancellationToken ct)
    {
        var json = await SendAsync(HttpMethod.Get, Profile, token, null, ct)
                   ?? throw new LauncherException("Minecraft Services не вернул профиль.");
        var skins = (json["skins"] as JsonArray ?? [])
            .Where(s => s is not null)
            .Select(s => new AccountSkin(
                s!["id"].Str() ?? "",
                Https(s["url"].Str() ?? ""),
                s["variant"].Str()?.ToUpperInvariant() == "SLIM" ? SkinModel.Slim : SkinModel.Classic,
                s["state"].Str() == "ACTIVE"))
            .ToList();
        var capes = (json["capes"] as JsonArray ?? [])
            .Where(c => c is not null)
            .Select(c => new AccountCape(
                c!["id"].Str() ?? "",
                Https(c["url"].Str() ?? ""),
                c["alias"].Str() ?? "Плащ",
                c["state"].Str() == "ACTIVE"))
            .ToList();
        return new AccountTextures(skins, capes);
    }

    /// <summary>Загружает PNG и делает его скином аккаунта.</summary>
    public static async Task UploadAsync(string token, byte[] png, SkinModel model, CancellationToken ct)
    {
        SkinLibrary.Validate(png);
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var form = new MultipartFormDataContent
        {
            { new StringContent(model == SkinModel.Slim ? "slim" : "classic"), "variant" },
            { file, "file", "skin.png" },
        };
        await SendAsync(HttpMethod.Post, Profile + "/skins", token, form, ct);
    }

    /// <summary>Возвращает стандартный скин.</summary>
    public static Task ResetAsync(string token, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, Profile + "/skins/active", token, null, ct);

    /// <summary>Надевает плащ (capeId) или снимает его (null).</summary>
    public static Task SetCapeAsync(string token, string? capeId, CancellationToken ct) =>
        capeId is null
            ? SendAsync(HttpMethod.Delete, Profile + "/capes/active", token, null, ct)
            : SendAsync(HttpMethod.Put, Profile + "/capes/active", token,
                new StringContent(new JsonObject { ["capeId"] = capeId }.ToJsonString(), Encoding.UTF8, "application/json"), ct);

    /// <summary>Скачивает текстуру (скин или плащ) с кэшем на диске: текстуры Mojang по одному адресу не меняются.</summary>
    public static async Task<byte[]> DownloadTextureAsync(string url, CancellationToken ct)
    {
        url = Https(url);
        var name = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..20] + ".png";
        var path = Path.Combine(Paths.Cache, "textures", name);
        if (!File.Exists(path))
            await Downloader.DownloadFileAsync(new DownloadItem(url, path), ct);
        return await File.ReadAllBytesAsync(path, ct);
    }

    /// <summary>Сервер текстур Mojang отдаёт ссылки http://, но работает и по https.</summary>
    private static string Https(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + url[7..] : url;

    private static async Task<JsonNode?> SendAsync(HttpMethod method, string url, string token, HttpContent? content, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url) { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await Http.Client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        JsonNode? json = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { json = JsonNode.Parse(text); } catch (JsonException) { }
        }

        if (resp.IsSuccessStatusCode) return json;
        throw new LauncherException(resp.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Сессия аккаунта истекла. Нажмите «Проверить лицензии» на вкладке «Аккаунты».",
            HttpStatusCode.TooManyRequests => "Слишком много запросов к Minecraft. Подождите минуту и попробуйте снова.",
            HttpStatusCode.NotFound => "Профиль Java Edition не найден.",
            _ => $"Minecraft Services ответил ошибкой {(int)resp.StatusCode}: {json?["errorMessage"].Str() ?? json?["error"].Str() ?? "без подробностей"}",
        });
    }
}
