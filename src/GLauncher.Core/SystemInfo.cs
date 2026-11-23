namespace GLauncher.Core;

public static class SystemInfo
{
    /// <summary>Объём оперативной памяти компьютера в МБ.</summary>
    public static long TotalMemoryMb => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);

    /// <summary>
    /// Предупреждение о памяти для профиля или null, если всё в порядке.
    /// Игре стоит отдавать не больше ~70% ОЗУ: остальное нужно Windows и самой JVM сверх -Xmx.
    /// </summary>
    public static string? MemoryWarning(int requestedMb)
    {
        var total = TotalMemoryMb;
        if (total <= 0) return null;
        if (requestedMb >= total)
            return $"Выделено {requestedMb} МБ, а в компьютере всего {total} МБ — игра не запустится.";
        if (requestedMb > total * 0.7)
            return $"Выделено {requestedMb} МБ из {total} МБ — системе может не хватить памяти. Рекомендуется не больше {(long)(total * 0.6)} МБ.";
        return null;
    }
}

public static class CrashReports
{
    /// <summary>
    /// Файл с причиной падения игры: свежий краш-репорт, иначе hs_err (падение самой JVM), иначе logs/latest.log.
    /// </summary>
    public static string? Find(string gameDir, DateTime startedUtc)
    {
        var candidates = new[]
            {
                Path.Combine(gameDir, "crash-reports"),
                gameDir, // hs_err_pid*.log
            }
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.*"))
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return name.StartsWith("crash-", StringComparison.OrdinalIgnoreCase) ||
                       name.StartsWith("hs_err_pid", StringComparison.OrdinalIgnoreCase);
            })
            .Select(f => new FileInfo(f))
            .Where(f => f.LastWriteTimeUtc >= startedUtc.AddSeconds(-5))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        if (candidates is not null) return candidates.FullName;

        var latestLog = Path.Combine(gameDir, "logs", "latest.log");
        return File.Exists(latestLog) ? latestLog : null;
    }
}
