using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace GLauncher.Core.Updates;

public sealed record UpdateInfo(Version Version, string Tag, string Notes, string PageUrl, string AssetUrl, long Size, string? Sha256);

/// <summary>
/// Обновление лаунчера из GitHub Releases. Релиз должен содержать файл <see cref="AssetName"/>
/// (его собирает .github/workflows/release.yml при пуше тега vX.Y.Z).
/// </summary>
public static class UpdateService
{
    public const string Owner = "livecrazenmc-lab";
    public const string Repository = "gLauncher";
    public const string AssetName = "gLauncher.exe";

    public static Version CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    /// <summary>
    /// Обновлять можно только опубликованную сборку из одного exe-файла.
    /// При запуске из Visual Studio / dotnet run обновление отключено, чтобы не затереть отладочную сборку.
    /// </summary>
#pragma warning disable IL3000 // пустой Location — как раз признак сборки из одного файла
    public static bool IsSupported =>
        string.IsNullOrEmpty(typeof(UpdateService).Assembly.Location) && Environment.ProcessPath is not null;
#pragma warning restore IL3000

    private static string ExePath => Environment.ProcessPath!;
    private static string NewPath => ExePath + ".new";
    private static string OldPath => ExePath + ".old";

    /// <summary>Возвращает свежий релиз, если он новее текущей версии, иначе null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var resp = await Http.Client.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new LauncherException("Релизы не найдены: репозиторий приватный или в нём ещё нет ни одного релиза.");
        if (resp.StatusCode == HttpStatusCode.Forbidden)
            throw new LauncherException("GitHub временно ограничил запросы. Попробуйте позже.");
        resp.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))!;
        var tag = json["tag_name"].Str() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-', '+')[0], out var version))
            throw new LauncherException($"Не удалось разобрать версию релиза «{tag}».");

        var latest = new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
        var current = new Version(CurrentVersion.Major, CurrentVersion.Minor, Math.Max(CurrentVersion.Build, 0));
        if (latest <= current) return null;

        var asset = (json["assets"] as JsonArray ?? [])
            .FirstOrDefault(a => string.Equals(a?["name"].Str(), AssetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new LauncherException($"В релизе {tag} нет файла {AssetName}.");

        // GitHub указывает SHA-256 файла в поле digest: «sha256:…».
        var digest = asset["digest"].Str();
        var sha256 = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..] : null;

        return new UpdateInfo(latest, tag, json["body"].Str() ?? "", json["html_url"].Str() ?? "",
            asset["browser_download_url"].Str()!, asset["size"].Long(), sha256);
    }

    /// <summary>
    /// Скачивает новую версию и подменяет exe. Windows позволяет переименовать запущенный файл,
    /// поэтому текущий exe переносится в «.old» (удаляется при следующем запуске), а новый встаёт на его место.
    /// После этого нужно вызвать <see cref="Restart"/>.
    /// </summary>
    public static async Task ApplyAsync(UpdateInfo update, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        if (!IsSupported)
            throw new LauncherException("Обновление доступно только в опубликованной сборке (один файл gLauncher.exe).");

        TryDelete(NewPath);
        using (var resp = await Http.Client.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? update.Size;
            await using var input = await resp.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(NewPath);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                progress?.Report(new($"Загрузка обновления {update.Tag}", (int)(done / 1024), (int)(total / 1024)));
            }
        }

        var size = new FileInfo(NewPath).Length;
        if (update.Size > 0 && size != update.Size)
        {
            TryDelete(NewPath);
            throw new LauncherException("Обновление скачалось не полностью. Попробуйте ещё раз.");
        }
        if (update.Sha256 is not null)
        {
            await using var fs = File.OpenRead(NewPath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                fs.Close();
                TryDelete(NewPath);
                throw new LauncherException("Контрольная сумма обновления не совпадает — файл повреждён. Обновление отменено.");
            }
        }

        TryDelete(OldPath);
        File.Move(ExePath, OldPath);
        try
        {
            File.Move(NewPath, ExePath);
        }
        catch
        {
            File.Move(OldPath, ExePath); // откат
            throw;
        }
    }

    /// <summary>Запускает новую версию. Текущий процесс после этого нужно завершить.</summary>
    public static void Restart() =>
        Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = false });

    /// <summary>Удаляет файл прошлой версии, оставшийся после обновления.</summary>
    public static void CleanupAfterUpdate()
    {
        if (!IsSupported) return;
        TryDelete(OldPath);
        TryDelete(NewPath);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* файл ещё занят завершающимся процессом — удалим в следующий раз */ }
    }
}
