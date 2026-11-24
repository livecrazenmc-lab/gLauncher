using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GLauncher.Core.Auth;

public sealed class AuthException(string message, Exception? inner = null) : LauncherException(message, inner);

public sealed record DeviceCodeInfo(string DeviceCode, string UserCode, string VerificationUri, int ExpiresIn, int Interval);

/// <summary>
/// Вход через Microsoft (device code flow) → Xbox Live → XSTS → Minecraft Services,
/// затем проверка лицензий (Java и Bedrock).
/// </summary>
public sealed class MicrosoftAuthService(string clientId)
{
    private const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0";
    private const string Scope = "XboxLive.signin offline_access";
    private const string McApi = "https://api.minecraftservices.com";

    private const string NoClientIdMessage =
        "Вход через Microsoft в этой сборке лаунчера не настроен. Установите последнюю версию gLauncher.";

    public async Task<DeviceCodeInfo> StartDeviceCodeAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new AuthException(NoClientIdMessage);

        var (_, json) = await PostFormAsync($"{Authority}/devicecode", new()
        {
            ["client_id"] = clientId,
            ["scope"] = Scope,
        }, ct);

        if (json?["device_code"] is null)
            throw new AuthException($"Microsoft отклонил запрос: {json?["error_description"].Str() ?? "нет ответа"}");

        return new DeviceCodeInfo(
            json["device_code"].Str()!,
            json["user_code"].Str()!,
            json["verification_uri"].Str() ?? "https://www.microsoft.com/link",
            (int)json["expires_in"].Long(),
            (int)Math.Max(json["interval"].Long(), 1));
    }

    /// <summary>Ждёт, пока пользователь введёт код в браузере, и завершает вход.</summary>
    public async Task<Account> WaitForDeviceCodeAsync(DeviceCodeInfo info, CancellationToken ct)
    {
        var interval = info.Interval;
        var deadline = DateTime.UtcNow.AddSeconds(info.ExpiresIn);

        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            if (DateTime.UtcNow > deadline)
                throw new AuthException("Срок действия кода истёк. Попробуйте ещё раз.");

            var (_, json) = await PostFormAsync($"{Authority}/token", new()
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = clientId,
                ["device_code"] = info.DeviceCode,
            }, ct);

            switch (json?["error"].Str())
            {
                case null when (json?["access_token"]) is not null:
                    return await LoginAsync(json!, new Account(), ct);
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                case "authorization_declined":
                    throw new AuthException("Вход отклонён пользователем.");
                case "expired_token":
                    throw new AuthException("Срок действия кода истёк. Попробуйте ещё раз.");
                default:
                    throw new AuthException($"Ошибка входа Microsoft: {json?["error_description"].Str() ?? json?["error"].Str() ?? "нет ответа"}");
            }
        }
    }

    /// <summary>Обновляет токен Minecraft, если он истёк или скоро истечёт. Заодно перепроверяет лицензии.</summary>
    public async Task EnsureValidAsync(Account account, CancellationToken ct)
    {
        if (account.HasValidMinecraftToken) return;
        await RefreshAsync(account, ct);
    }

    /// <summary>Принудительно обновляет все токены и перепроверяет лицензии.</summary>
    public async Task RefreshAsync(Account account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new AuthException(NoClientIdMessage);
        if (string.IsNullOrEmpty(account.MsRefreshToken))
            throw new AuthException("Сессия аккаунта истекла. Удалите аккаунт и войдите заново.");

        var (_, json) = await PostFormAsync($"{Authority}/token", new()
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = account.MsRefreshToken,
            ["scope"] = Scope,
        }, ct);

        if (json?["access_token"] is null)
            throw new AuthException("Сессия аккаунта истекла. Удалите аккаунт и войдите заново.");

        await LoginAsync(json, account, ct);
    }

    private async Task<Account> LoginAsync(JsonNode msToken, Account account, CancellationToken ct)
    {
        var msAccess = msToken["access_token"].Str()!;
        account.MsRefreshToken = msToken["refresh_token"].Str() ?? account.MsRefreshToken;

        // 1. Xbox Live
        var (xblStatus, xbl) = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", new JsonObject
        {
            ["Properties"] = new JsonObject
            {
                ["AuthMethod"] = "RPS",
                ["SiteName"] = "user.auth.xboxlive.com",
                ["RpsTicket"] = "d=" + msAccess,
            },
            ["RelyingParty"] = "http://auth.xboxlive.com",
            ["TokenType"] = "JWT",
        }, ct);
        var xblToken = xbl?["Token"].Str();
        var uhs = xbl?["DisplayClaims"]?["xui"]?[0]?["uhs"].Str();
        if (xblStatus != HttpStatusCode.OK || xblToken is null || uhs is null)
            throw new AuthException($"Не удалось войти в Xbox Live (код {(int)xblStatus}).");

        // 2. XSTS для Minecraft
        var xstsToken = await XstsAsync(xblToken, "rp://api.minecraftservices.com/", ct);

        // 2б. XSTS для Xbox Live — только ради геймертега и XUID, ошибки не критичны
        try
        {
            var (status, x) = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", XstsBody(xblToken, "http://xboxlive.com"), ct);
            if (status == HttpStatusCode.OK)
            {
                var xui = x?["DisplayClaims"]?["xui"]?[0];
                account.Gamertag = xui?["gtg"].Str() ?? account.Gamertag;
                account.Xuid = xui?["xid"].Str() ?? account.Xuid;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException) { /* не критично */ }

        // 3. Minecraft Services
        var (mcStatus, mc) = await PostJsonAsync($"{McApi}/authentication/login_with_xbox", new JsonObject
        {
            ["identityToken"] = $"XBL3.0 x={uhs};{xstsToken}",
        }, ct);
        if (mcStatus == HttpStatusCode.Forbidden)
            throw new AuthException("Minecraft отклонил вход: приложение лаунчера ещё не одобрено Mojang для Minecraft API. " +
                                    "(Для разработчика: заявка — https://aka.ms/mce-reviewappid)");
        var mcToken = mc?["access_token"].Str();
        if (mcStatus != HttpStatusCode.OK || mcToken is null)
            throw new AuthException($"Не удалось войти в Minecraft Services (код {(int)mcStatus}).");

        account.MinecraftAccessToken = mcToken;
        account.MinecraftTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(mc!["expires_in"].Long(), 60));

        // 4. Лицензии
        var entitlements = await Http.GetJsonAsync($"{McApi}/entitlements/mcstore", ct, mcToken);
        account.Entitlements = (entitlements["items"] as JsonArray ?? [])
            .Select(i => i?["name"].Str())
            .OfType<string>()
            .ToList();

        // 5. Java-профиль (есть только у владельцев Java Edition / Game Pass)
        JsonNode? profile = null;
        try
        {
            profile = await Http.GetJsonAsync($"{McApi}/minecraft/profile", ct, mcToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }

        account.Uuid = profile?["id"].Str();
        account.Username = profile?["name"].Str();
        account.OwnsJava = account.Uuid is not null && account.Username is not null;
        account.OwnsBedrock = account.Entitlements.Any(e =>
            e is "product_minecraft_bedrock" or "game_minecraft_bedrock");
        account.LastChecked = DateTimeOffset.UtcNow;

        if (!account.OwnsJava && !account.OwnsBedrock)
            throw new AuthException("На этом аккаунте Microsoft не найдено лицензии Minecraft (ни Java, ни Bedrock).");

        return account;
    }

    private static async Task<string> XstsAsync(string xblToken, string relyingParty, CancellationToken ct)
    {
        var (status, json) = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", XstsBody(xblToken, relyingParty), ct);
        if (status == HttpStatusCode.OK && json?["Token"].Str() is { } token)
            return token;

        throw new AuthException(json?["XErr"].Long() switch
        {
            2148916233 => "У этого аккаунта нет профиля Xbox. Войдите на xbox.com, чтобы создать его, и повторите попытку.",
            2148916235 => "Xbox Live недоступен в вашей стране.",
            2148916236 or 2148916237 => "Аккаунту требуется подтверждение возраста на xbox.com.",
            2148916238 => "Это детский аккаунт: взрослый должен добавить его в семейную группу Microsoft.",
            _ => $"Xbox Live отклонил вход (код {(int)status}).",
        });
    }

    private static JsonObject XstsBody(string xblToken, string relyingParty) => new()
    {
        ["Properties"] = new JsonObject
        {
            ["SandboxId"] = "RETAIL",
            ["UserTokens"] = new JsonArray(xblToken),
        },
        ["RelyingParty"] = relyingParty,
        ["TokenType"] = "JWT",
    };

    private static async Task<(HttpStatusCode, JsonNode?)> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        return await SendAsync(req, ct);
    }

    private static async Task<(HttpStatusCode, JsonNode?)> PostJsonAsync(string url, JsonNode body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await SendAsync(req, ct);
    }

    private static async Task<(HttpStatusCode, JsonNode?)> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await Http.Client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        JsonNode? json = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { json = JsonNode.Parse(text); }
            catch (JsonException) { }
        }
        return (resp.StatusCode, json);
    }
}

