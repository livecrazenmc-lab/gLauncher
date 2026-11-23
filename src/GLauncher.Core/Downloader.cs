using System.Security.Cryptography;

namespace GLauncher.Core;

public sealed record DownloadItem(string Url, string Path, string? Sha1 = null, long Size = 0);

public static class Downloader
{
    /// <summary>Параллельно скачивает отсутствующие или повреждённые файлы.</summary>
    public static async Task DownloadAllAsync(IEnumerable<DownloadItem> items, string stage,
        IProgress<ProgressInfo>? progress, CancellationToken ct, bool verifyHash = false)
    {
        progress?.Report(new(stage, 0, 0));
        var todo = items
            .DistinctBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
            .Where(i => NeedsDownload(i, verifyHash))
            .ToList();
        if (todo.Count == 0) return;

        progress?.Report(new(stage, 0, todo.Count));
        var done = 0;
        await Parallel.ForEachAsync(todo,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct },
            async (item, token) =>
            {
                await DownloadFileAsync(item, token);
                progress?.Report(new(stage, Interlocked.Increment(ref done), todo.Count));
            });
    }

    public static async Task DownloadIfNeededAsync(DownloadItem item, CancellationToken ct)
    {
        if (NeedsDownload(item)) await DownloadFileAsync(item, ct);
    }

    public static bool NeedsDownload(DownloadItem item, bool verifyHash = false)
    {
        var file = new FileInfo(item.Path);
        if (!file.Exists) return true;
        if (item.Size > 0 && file.Length != item.Size) return true;
        if (verifyHash && !string.IsNullOrEmpty(item.Sha1))
            return !Sha1(item.Path).Equals(item.Sha1, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    public static async Task DownloadFileAsync(DownloadItem item, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.Url))
            throw new LauncherException($"Нет ссылки для загрузки файла {item.Path}");

        Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);
        var tmp = $"{item.Path}.{Guid.NewGuid():N}.part";
        Exception? last = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using (var resp = await Http.Client.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    await using var fs = File.Create(tmp);
                    await resp.Content.CopyToAsync(fs, ct);
                }

                if (!string.IsNullOrEmpty(item.Sha1) &&
                    !Sha1(tmp).Equals(item.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("контрольная сумма не совпадает");

                File.Move(tmp, item.Path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                TryDelete(tmp);
                if (attempt < 3) await Task.Delay(500 * attempt, ct);
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
        }

        throw new LauncherException($"Не удалось скачать {item.Url}: {last?.Message}", last);
    }

    public static string Sha1(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(fs));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }
}
