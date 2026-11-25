using System.Text.Json;

namespace GLauncher.Core.Games;

/// <summary>Арт актуальной версии игры: картинка файла и заголовок записи списка изменений.</summary>
public sealed record VersionArtInfo(string Path, string Title, string Version);

/// <summary>
/// Арт обложки Java и Bedrock меняется вместе с актуальной версией игры, как в официальном лаунчере:
/// берётся картинка записи списка изменений (launchercontent.mojang.com) для последнего релиза.
/// Картинки принадлежат Mojang — скачиваются во время работы и хранятся в кэше; без сети остаётся последний скачанный.
/// </summary>
public static class VersionArt
{
    private static string Folder => Path.Combine(Paths.Cache, "art");
    private static string MetaPath(PatchNotesKind kind) => Path.Combine(Folder, $"{kind}-version.json");

    /// <summary>Последний скачанный арт версии, если есть.</summary>
    public static VersionArtInfo? Cached(PatchNotesKind kind)
    {
        try
        {
            if (!File.Exists(MetaPath(kind))) return null;
            var info = JsonSerializer.Deserialize<VersionArtInfo>(File.ReadAllText(MetaPath(kind)));
            return info is not null && File.Exists(info.Path) ? info : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Проверяет актуальный релиз и докачивает его арт. Возвращает true, если арт сменился. Ошибки сети не бросает.</summary>
    public static async Task<bool> RefreshAsync(PatchNotesKind kind, CancellationToken ct)
    {
        try
        {
            // Записи отсортированы от новых к старым: первая не-снапшот запись — актуальный релиз
            var notes = await PatchNotes.GetAsync(kind, ct);
            var release = notes.FirstOrDefault(e => !e.IsSnapshot && e.ImageUrl is not null);
            if (release is null) return false;

            var current = Cached(kind);
            if (current is not null && current.Version == release.Version) return false;

            Directory.CreateDirectory(Folder);
            var ext = Path.GetExtension(new Uri(release.ImageUrl!).AbsolutePath);
            var safe = string.Concat(release.Version.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
            var file = Path.Combine(Folder, $"{kind}-{safe}{(string.IsNullOrEmpty(ext) ? ".img" : ext)}");
            var tmp = file + ".part";
            await Downloader.DownloadFileAsync(new DownloadItem(release.ImageUrl!, tmp), ct);
            File.Move(tmp, file, overwrite: true);

            File.WriteAllText(MetaPath(kind), JsonSerializer.Serialize(new VersionArtInfo(file, release.Title, release.Version)));
            if (current is not null && current.Path != file)
                try { File.Delete(current.Path); } catch { /* старый арт занят — удалится в следующий раз */ }
            return true;
        }
        catch (Exception)
        {
            return false; // нет сети — остаётся прошлый арт или арт магазина
        }
    }
}
