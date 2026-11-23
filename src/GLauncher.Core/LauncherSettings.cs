namespace GLauncher.Core;

public enum AfterLaunchAction { KeepOpen, Minimize, Close }

public sealed class LauncherSettings
{
    /// <summary>
    /// Client ID приложения Azure (Microsoft Entra), через которое идёт вход.
    /// Приложение должно быть одобрено Mojang для доступа к Minecraft API.
    /// </summary>
    public string MicrosoftClientId { get; set; } = "";

    /// <summary>Случайный идентификатор установки лаунчера (передаётся игре как clientId).</summary>
    public string ClientToken { get; set; } = Guid.NewGuid().ToString("N");

    public bool ShowSnapshots { get; set; }

    /// <summary>Что делать с окном лаунчера после запуска Java Edition.</summary>
    public AfterLaunchAction AfterLaunch { get; set; } = AfterLaunchAction.KeepOpen;

    /// <summary>Тема оформления: Auto (по дате), Blocks, Spring, Summer, Autumn, Winter, NewYear, Easter, Halloween.</summary>
    public string Theme { get; set; } = "Auto";

    /// <summary>Проверять обновления лаунчера при запуске.</summary>
    public bool CheckForUpdates { get; set; } = true;

    public static LauncherSettings Load()
    {
        var settings = JsonFile.Load<LauncherSettings>(Paths.SettingsFile) ?? new LauncherSettings();
        if (!File.Exists(Paths.SettingsFile)) settings.Save();
        return settings;
    }

    public void Save() => JsonFile.Save(Paths.SettingsFile, this);
}
