using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace GLauncher.Core.Java;

/// <summary>Скачивает официальные сборки Java от Mojang (jre-legacy, java-runtime-gamma, …).</summary>
public static class JavaRuntimeManager
{
    private const string AllUrl =
        "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    private static string Platform => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "windows-x64",
        Architecture.Arm64 => "windows-arm64",
        _ => "windows-x86",
    };

    public static string JavaHome(string component) => Path.Combine(Paths.Runtime, component);
    public static string JavaExe(string home) => Path.Combine(home, "bin", "java.exe");

    /// <summary>Гарантирует наличие нужной Java и возвращает путь к java.exe.</summary>
    public static async Task<string> EnsureAsync(string component, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var home = JavaHome(component);
        var java = JavaExe(home);
        var marker = Path.Combine(home, ".installed");
        progress?.Report(new($"Проверка Java ({component})", 0, 0));

        JsonNode all;
        try
        {
            all = await Http.GetJsonAsync(AllUrl, ct);
        }
        catch (HttpRequestException) when (File.Exists(marker) && File.Exists(java))
        {
            return java; // офлайн, но Java уже есть
        }

        var entries = all[Platform]?[component] as JsonArray;
        if (entries is null || entries.Count == 0)
            entries = all["windows-x64"]?[component] as JsonArray; // ARM64 Windows умеет эмулировать x64
        if (entries is null || entries.Count == 0)
            throw new LauncherException($"Mojang не предоставляет Java «{component}» для этой системы. Укажите путь к Java в профиле.");

        var versionName = entries[0]?["version"]?["name"].Str() ?? "";
        if (File.Exists(java) && File.Exists(marker) && File.ReadAllText(marker) == versionName)
            return java;

        var manifest = await Http.GetJsonAsync(entries[0]!["manifest"]!["url"].Str()!, ct);
        var items = new List<DownloadItem>();
        foreach (var (rel, file) in manifest["files"] as JsonObject ?? [])
        {
            var target = Path.Combine(home, rel.Replace('/', Path.DirectorySeparatorChar));
            switch (file?["type"].Str())
            {
                case "directory":
                    Directory.CreateDirectory(target);
                    break;
                case "file" when (file["downloads"]?["raw"]) is { } raw:
                    items.Add(new DownloadItem(raw["url"].Str()!, target, raw["sha1"].Str(), raw["size"].Long()));
                    break;
            }
        }

        await Downloader.DownloadAllAsync(items, $"Загрузка Java ({component})", progress, ct, verifyHash: true);
        await File.WriteAllTextAsync(marker, versionName, ct);
        return java;
    }
}
