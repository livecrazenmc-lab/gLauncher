using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GLauncher.Core.Auth;

public sealed class Account
{
    /// <summary>Внутренний идентификатор записи в лаунчере.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>UUID и ник Java-профиля. null — у аккаунта нет Java Edition.</summary>
    public string? Uuid { get; set; }
    public string? Username { get; set; }

    public string? Gamertag { get; set; }
    public string? Xuid { get; set; }

    public string? MsRefreshToken { get; set; }
    public string? MinecraftAccessToken { get; set; }
    public DateTimeOffset MinecraftTokenExpiresAt { get; set; }

    public bool OwnsJava { get; set; }
    public bool OwnsBedrock { get; set; }
    public List<string> Entitlements { get; set; } = [];
    public DateTimeOffset LastChecked { get; set; }

    /// <summary>
    /// День рождения «ММ-дд» (без года) — вводится в лаунчере: Microsoft не отдаёт дату рождения
    /// по входу Xbox Live. Хранится только локально, в зашифрованном списке аккаунтов.
    /// </summary>
    public string? Birthday { get; set; }

    /// <summary>Сегодня день рождения владельца аккаунта (29 февраля в невисокосный год — 28-го).</summary>
    public bool IsBirthday(DateTime today)
    {
        if (Birthday is not { Length: 5 } b || !int.TryParse(b[..2], out var month) || !int.TryParse(b[3..], out var day)) return false;
        if (month == 2 && day == 29 && !DateTime.IsLeapYear(today.Year)) day = 28;
        return today.Month == month && today.Day == day;
    }

    [JsonIgnore]
    public string DisplayName => Username ?? Gamertag ?? "Аккаунт Microsoft";

    [JsonIgnore]
    public bool HasValidMinecraftToken =>
        !string.IsNullOrEmpty(MinecraftAccessToken) && MinecraftTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5);
}

/// <summary>Список аккаунтов. Хранится зашифрованным через Windows DPAPI (доступен только текущему пользователю Windows).</summary>
public sealed class AccountStore
{
    private static readonly byte[] Entropy = "gLauncher.accounts.v1"u8.ToArray();

    public List<Account> Accounts { get; set; } = [];
    public string? SelectedId { get; set; }

    public static AccountStore Load()
    {
        if (!File.Exists(Paths.AccountsFile)) return new AccountStore();
        try
        {
            var data = ProtectedData.Unprotect(File.ReadAllBytes(Paths.AccountsFile), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<AccountStore>(data, JsonFile.Options) ?? new AccountStore();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return new AccountStore();
        }
    }

    public void Save()
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(this, JsonFile.Options);
        JsonFile.WriteAtomic(Paths.AccountsFile, ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser));
    }

    /// <summary>Добавляет аккаунт или заменяет уже существующий с тем же UUID/XUID.</summary>
    public Account AddOrUpdate(Account account)
    {
        var index = Accounts.FindIndex(a =>
            a.Id == account.Id ||
            (account.Uuid is not null && a.Uuid == account.Uuid) ||
            (account.Xuid is not null && a.Xuid == account.Xuid));
        if (index >= 0)
        {
            account.Id = Accounts[index].Id;
            account.Birthday ??= Accounts[index].Birthday; // вводится в лаунчере, при повторном входе не теряется
            Accounts[index] = account;
        }
        else
        {
            Accounts.Add(account);
        }
        return account;
    }
}
