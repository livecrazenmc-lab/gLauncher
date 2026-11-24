using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace GLauncher.Core.Skins;

public enum SkinModel { Classic, Slim }

/// <summary>Скин в библиотеке лаунчера.</summary>
public sealed class SkinEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "Скин";
    public SkinModel Model { get; set; } = SkinModel.Classic;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Сохранённые скины: %APPDATA%\.glauncher\skins\&lt;id&gt;.png + library.json.</summary>
public sealed class SkinLibrary
{
    public static string Folder => Path.Combine(Paths.Root, "skins");
    private static string IndexFile => Path.Combine(Folder, "library.json");

    public List<SkinEntry> Skins { get; set; } = [];

    public static SkinLibrary Load()
    {
        var lib = JsonFile.Load<SkinLibrary>(IndexFile) ?? new SkinLibrary();
        lib.Skins.RemoveAll(s => !File.Exists(PathOf(s))); // файл удалили вручную
        return lib;
    }

    public void Save() => JsonFile.Save(IndexFile, this);

    public static string PathOf(SkinEntry entry) => Path.Combine(Folder, entry.Id + ".png");

    public SkinEntry AddFile(string pngPath, SkinModel model) =>
        AddBytes(File.ReadAllBytes(pngPath), Path.GetFileNameWithoutExtension(pngPath), model);

    public SkinEntry AddBytes(byte[] png, string name, SkinModel model)
    {
        Validate(png);
        var entry = new SkinEntry { Name = string.IsNullOrWhiteSpace(name) ? "Скин" : name.Trim(), Model = model };
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(PathOf(entry), png);
        Skins.Insert(0, entry);
        Save();
        return entry;
    }

    public void Remove(SkinEntry entry)
    {
        Skins.Remove(entry);
        try { File.Delete(PathOf(entry)); } catch (IOException) { }
        Save();
    }

    /// <summary>Скачивает скин игрока по нику (публичное API Mojang, вход не нужен) и кладёт в библиотеку.</summary>
    public async Task<SkinEntry> ImportFromPlayerAsync(string nickname, CancellationToken ct)
    {
        nickname = nickname.Trim();
        if (nickname.Length is < 3 or > 16 || nickname.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
            throw new LauncherException("Ник Minecraft — от 3 до 16 латинских букв, цифр или «_».");

        string uuid, name;
        try
        {
            var lookup = await Http.GetJsonAsync(
                $"https://api.minecraftservices.com/minecraft/profile/lookup/name/{Uri.EscapeDataString(nickname)}", ct);
            uuid = lookup["id"].Str() ?? throw new LauncherException($"Игрок «{nickname}» не найден.");
            name = lookup["name"].Str() ?? nickname;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
        {
            throw new LauncherException($"Игрок «{nickname}» не найден.");
        }

        var profile = await Http.GetJsonAsync($"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}", ct);
        var textures = (profile["properties"] as JsonArray ?? [])
            .FirstOrDefault(p => p?["name"].Str() == "textures")?["value"].Str();
        var skin = textures is null ? null
            : JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(textures)))?["textures"]?["SKIN"];
        var url = skin?["url"].Str()
                  ?? throw new LauncherException($"У игрока «{name}» стандартный скин — копировать нечего.");
        var model = skin["metadata"]?["model"].Str() == "slim" ? SkinModel.Slim : SkinModel.Classic;

        var png = await SkinApi.DownloadTextureAsync(url, ct);
        return AddBytes(png, name, model);
    }

    /// <summary>Скин — PNG 64×64 (или старый формат 64×32).</summary>
    public static void Validate(byte[] png)
    {
        var (w, h) = PngSize(png);
        if (w != 64 || (h != 64 && h != 32))
            throw new LauncherException($"Скин должен быть картинкой PNG 64×64 (или 64×32), а это {w}×{h}.");
    }

    /// <summary>Размер PNG из заголовка IHDR. Бросает LauncherException, если это не PNG.</summary>
    public static (int Width, int Height) PngSize(byte[] png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(signature))
            throw new LauncherException("Файл не является картинкой PNG.");
        int Read(int o) => png[o] << 24 | png[o + 1] << 16 | png[o + 2] << 8 | png[o + 3];
        return (Read(16), Read(20));
    }
}
