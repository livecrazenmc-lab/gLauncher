using System.Reflection;

namespace GLauncher.Core.Auth;

/// <summary>Какой Client ID приложения Azure использовать для входа через Microsoft.</summary>
public static class AuthConfig
{
    /// <summary>ID, вшитый в сборку (свойство GLauncherClientId в Directory.Build.props). Пусто — не задан.</summary>
    public static string BuiltInClientId { get; } =
        typeof(AuthConfig).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "MicrosoftClientId")?.Value?.Trim() ?? "";

    public static bool HasBuiltInClientId => BuiltInClientId.Length > 0;

    /// <summary>
    /// ID для входа: свой из настроек (для разработчиков, необязательно), иначе встроенный.
    /// </summary>
    public static string Resolve(LauncherSettings settings) =>
        string.IsNullOrWhiteSpace(settings.MicrosoftClientId) ? BuiltInClientId : settings.MicrosoftClientId.Trim();
}
