namespace GLauncher.Core;

/// <summary>Расположение всех данных лаунчера: %APPDATA%\.glauncher</summary>
public static class Paths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".glauncher");

    public static string Versions => Path.Combine(Root, "versions");
    public static string Libraries => Path.Combine(Root, "libraries");
    public static string Assets => Path.Combine(Root, "assets");
    public static string Runtime => Path.Combine(Root, "runtime");
    public static string Instances => Path.Combine(Root, "instances");
    public static string Cache => Path.Combine(Root, "cache");

    public static string AccountsFile => Path.Combine(Root, "accounts.dat");
    public static string ProfilesFile => Path.Combine(Root, "profiles.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string VersionDir(string id) => Path.Combine(Versions, id);
    public static string VersionJson(string id) => Path.Combine(Versions, id, id + ".json");
    public static string VersionJar(string id) => Path.Combine(Versions, id, id + ".jar");
}
