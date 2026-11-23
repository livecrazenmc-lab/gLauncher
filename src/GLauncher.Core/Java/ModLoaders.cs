using System.Diagnostics;
using System.Text.Json.Nodes;

namespace GLauncher.Core.Java;

public static class FabricInstaller
{
    private const string Meta = "https://meta.fabricmc.net/v2";

    /// <summary>Ставит Fabric для версии Minecraft. Возвращает ID версии и фактическую версию лоадера.</summary>
    public static async Task<(string VersionId, string LoaderVersion)> InstallAsync(
        string minecraftVersion, string? loaderVersion, CancellationToken ct)
    {
        var mc = Uri.EscapeDataString(minecraftVersion);

        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            var list = await Http.GetJsonAsync($"{Meta}/versions/loader/{mc}", ct) as JsonArray ?? [];
            var best = list.FirstOrDefault(x => x?["loader"]?["stable"].Bool() == true) ?? list.FirstOrDefault();
            loaderVersion = best?["loader"]?["version"].Str()
                            ?? throw new LauncherException($"Fabric не поддерживает Minecraft {minecraftVersion}.");
        }

        JsonNode profile;
        try
        {
            profile = await Http.GetJsonAsync($"{Meta}/versions/loader/{mc}/{Uri.EscapeDataString(loaderVersion)}/profile/json", ct);
        }
        catch (HttpRequestException ex) when ((int?)ex.StatusCode is 400 or 404)
        {
            throw new LauncherException($"Fabric {loaderVersion} для Minecraft {minecraftVersion} не найден.");
        }

        var id = profile["id"].Str()!;
        JsonFile.WriteAtomic(Paths.VersionJson(id), System.Text.Encoding.UTF8.GetBytes(profile.ToJsonString()));
        return (id, loaderVersion);
    }
}

public static class ForgeInstaller
{
    private const string Promotions = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private const string MavenUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge";

    /// <summary>
    /// Скачивает официальный установщик Forge и запускает его в режиме --installClient.
    /// Возвращает ID установленной версии и версию Forge.
    /// </summary>
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
        var installer = Path.Combine(Paths.Cache, $"forge-{full}-installer.jar");
        try
        {
            await Downloader.DownloadIfNeededAsync(new DownloadItem($"{MavenUrl}/{full}/forge-{full}-installer.jar", installer), ct);
        }
        catch (LauncherException ex)
        {
            throw new LauncherException($"Не удалось скачать установщик Forge {full}. Проверьте версию Forge.", ex);
        }

        // Установщик Forge требует launcher_profiles.json в папке игры.
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

        log?.Invoke($"[Forge] Установка Forge {full}…");
        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) log?.Invoke("[Forge] " + e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) log?.Invoke("[Forge] " + e.Data); };
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
            throw new LauncherException($"Установщик Forge завершился с ошибкой (код {proc.ExitCode}). " +
                                        "Подробности — в журнале. Очень старые версии Forge не поддерживают автоматическую установку.");

        var id = Directory.GetDirectories(Paths.Versions)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(n => n.Contains("forge", StringComparison.OrdinalIgnoreCase) &&
                        n.Contains(forgeVersion, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(Paths.VersionJson(n)))
            .OrderByDescending(n => File.GetLastWriteTimeUtc(Paths.VersionJson(n)))
            .FirstOrDefault()
            ?? throw new LauncherException("Forge установлен, но его версия не найдена в папке versions.");

        return (id, forgeVersion);
    }
}
