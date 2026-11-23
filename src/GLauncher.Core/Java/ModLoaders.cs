using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace GLauncher.Core.Java;

/// <summary>Fabric и Quilt: у обоих есть meta-сервер, который отдаёт готовый JSON версии.</summary>
public sealed class MetaLoaderInstaller
{
    public static readonly MetaLoaderInstaller Fabric = new("Fabric", "https://meta.fabricmc.net/v2");
    public static readonly MetaLoaderInstaller Quilt = new("Quilt", "https://meta.quiltmc.org/v3");

    private readonly string _name;
    private readonly string _meta;

    private MetaLoaderInstaller(string name, string meta)
    {
        _name = name;
        _meta = meta;
    }

    /// <summary>Ставит лоадер для версии Minecraft. Возвращает ID версии и фактическую версию лоадера.</summary>
    public async Task<(string VersionId, string LoaderVersion)> InstallAsync(
        string minecraftVersion, string? loaderVersion, CancellationToken ct)
    {
        var mc = Uri.EscapeDataString(minecraftVersion);

        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            JsonArray list;
            try
            {
                list = await Http.GetJsonAsync($"{_meta}/versions/loader/{mc}", ct) as JsonArray ?? [];
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode is 400 or 404)
            {
                list = [];
            }

            // У Fabric есть флаг stable, у Quilt — нет (там нестабильные версии содержат «-beta»).
            // Quilt к тому же отдаёт список не по порядку, поэтому берём максимальную версию.
            var versions = list
                .Select(x => (Version: x?["loader"]?["version"].Str(), Stable: x?["loader"]?["stable"]))
                .Where(x => x.Version is not null)
                .Select(x => (x.Version!, Stable: x.Stable is null ? !x.Version!.Contains('-') : x.Stable.Bool()))
                .ToList();
            loaderVersion = (versions.Where(v => v.Stable).Select(v => v.Item1).MaxBy(ParseVersion)
                             ?? versions.Select(v => v.Item1).MaxBy(ParseVersion))
                            ?? throw new LauncherException($"{_name} не поддерживает Minecraft {minecraftVersion}.");
        }

        JsonNode profile;
        try
        {
            profile = await Http.GetJsonAsync($"{_meta}/versions/loader/{mc}/{Uri.EscapeDataString(loaderVersion)}/profile/json", ct);
        }
        catch (HttpRequestException ex) when ((int?)ex.StatusCode is 400 or 404)
        {
            throw new LauncherException($"{_name} {loaderVersion} для Minecraft {minecraftVersion} не найден.");
        }

        var id = profile["id"].Str()!;
        JsonFile.WriteAtomic(Paths.VersionJson(id), Encoding.UTF8.GetBytes(profile.ToJsonString()));
        return (id, loaderVersion);
    }

    /// <summary>«0.29.1-beta.3» → 0.29.1 (хвост после «-»/«+» отбрасывается).</summary>
    private static Version ParseVersion(string version)
    {
        var core = version.Split('-', '+')[0];
        return Version.TryParse(core, out var v) ? v : new Version(0, 0);
    }
}

public static class ForgeInstaller
{
    private const string Promotions = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private const string MavenUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge";

    public static async Task<(string VersionId, string ForgeVersion)> InstallAsync(
        string minecraftVersion, string? forgeVersion, string javaExe, Action<string>? log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(forgeVersion))
        {
            var promos = (await Http.GetJsonAsync(Promotions, ct))["promos"];
            forgeVersion = promos?[$"{minecraftVersion}-recommended"].Str()
                           ?? promos?[$"{minecraftVersion}-latest"].Str()
                           ?? throw new LauncherException($"Forge не поддерживает Minecraft {minecraftVersion}.");
        }

        var full = $"{minecraftVersion}-{forgeVersion}";
        var id = await InstallerJarRunner.RunAsync("Forge", $"{MavenUrl}/{full}/forge-{full}-installer.jar",
            $"forge-{full}-installer.jar", "forge", forgeVersion, javaExe, log, ct);
        return (id, forgeVersion);
    }
}

public static class NeoForgeInstaller
{
    private const string MavenUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge";

    public static async Task<(string VersionId, string NeoForgeVersion)> InstallAsync(
        string minecraftVersion, string? neoVersion, string javaExe, Action<string>? log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(neoVersion))
            neoVersion = await FindLatestAsync(minecraftVersion, ct);

        var id = await InstallerJarRunner.RunAsync("NeoForge", $"{MavenUrl}/{neoVersion}/neoforge-{neoVersion}-installer.jar",
            $"neoforge-{neoVersion}-installer.jar", "neoforge", neoVersion, javaExe, log, ct);
        return (id, neoVersion);
    }

    /// <summary>
    /// Версии NeoForge повторяют версию Minecraft без «1.»: MC 1.21.1 → 21.1.x, MC 1.21 → 21.0.x.
    /// Для новой нумерации Minecraft (26.1, 26.1.1) — «26.1.0.x», «26.1.1.x».
    /// </summary>
    public static async Task<string> FindLatestAsync(string minecraftVersion, CancellationToken ct)
    {
        var parts = minecraftVersion.Split('.');
        string prefix;
        if (parts[0] == "1")
        {
            if (parts.Length < 2) throw Unsupported(minecraftVersion);
            prefix = $"{parts[1]}.{(parts.Length > 2 ? parts[2] : "0")}.";
        }
        else
        {
            prefix = $"{parts[0]}.{(parts.Length > 1 ? parts[1] : "0")}.{(parts.Length > 2 ? parts[2] : "0")}.";
        }

        var xml = await Http.GetStringAsync($"{MavenUrl}/maven-metadata.xml", ct);
        var versions = XDocument.Parse(xml).Descendants("version").Select(v => v.Value)
            .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        // Метаданные Maven идут от старых к новым. Стабильные — без «-beta».
        return versions.LastOrDefault(v => !v.Contains('-'))
               ?? versions.LastOrDefault()
               ?? throw Unsupported(minecraftVersion);
    }

    private static LauncherException Unsupported(string mc) =>
        new($"NeoForge не поддерживает Minecraft {mc}. Для 1.20.1 и старше используйте Forge.");
}

/// <summary>Скачивает официальный установщик (Forge/NeoForge) и запускает его в режиме --installClient.</summary>
internal static class InstallerJarRunner
{
    public static async Task<string> RunAsync(string loaderName, string url, string fileName, string idMarker,
        string loaderVersion, string javaExe, Action<string>? log, CancellationToken ct)
    {
        var installer = Path.Combine(Paths.Cache, fileName);
        try
        {
            await Downloader.DownloadIfNeededAsync(new DownloadItem(url, installer), ct);
        }
        catch (LauncherException ex)
        {
            throw new LauncherException($"Не удалось скачать установщик {loaderName} {loaderVersion}. Проверьте версию.", ex);
        }

        // Установщики требуют launcher_profiles.json в папке игры.
        var launcherProfiles = Path.Combine(Paths.Root, "launcher_profiles.json");
        if (!File.Exists(launcherProfiles))
            await File.WriteAllTextAsync(launcherProfiles, "{\"profiles\":{}}", ct);

        var psi = new ProcessStartInfo(javaExe)
        {
            WorkingDirectory = Paths.Cache,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installer);
        psi.ArgumentList.Add("--installClient");
        psi.ArgumentList.Add(Paths.Root);

        var tag = $"[{loaderName}] ";
        log?.Invoke($"{tag}Установка {loaderName} {loaderVersion}…");
        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) log?.Invoke(tag + e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) log?.Invoke(tag + e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw;
        }

        if (proc.ExitCode != 0)
            throw new LauncherException($"Установщик {loaderName} завершился с ошибкой (код {proc.ExitCode}). " +
                                        "Подробности — в журнале.");

        return Directory.GetDirectories(Paths.Versions)
                   .Select(Path.GetFileName)
                   .OfType<string>()
                   .Where(n => n.Contains(idMarker, StringComparison.OrdinalIgnoreCase) &&
                               n.Contains(loaderVersion, StringComparison.OrdinalIgnoreCase) &&
                               File.Exists(Paths.VersionJson(n)))
                   .OrderByDescending(n => File.GetLastWriteTimeUtc(Paths.VersionJson(n)))
                   .FirstOrDefault()
               ?? throw new LauncherException($"{loaderName} установлен, но его версия не найдена в папке versions.");
    }
}
