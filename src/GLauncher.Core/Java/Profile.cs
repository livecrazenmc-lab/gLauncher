namespace GLauncher.Core.Java;

public enum ModLoader { Vanilla, Fabric, Forge }

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "Новый профиль";

    public ModLoader Loader { get; set; } = ModLoader.Vanilla;
    public string MinecraftVersion { get; set; } = "";

    /// <summary>Версия Fabric/Forge. Пусто — последняя стабильная (рекомендованная).</summary>
    public string? LoaderVersion { get; set; }

    /// <summary>ID установленной версии в папке versions (например «fabric-loader-0.16.9-1.21.1»). Заполняется лаунчером.</summary>
    public string? InstalledVersionId { get; set; }

    /// <summary>Папка игры. Пусто — instances\&lt;Id&gt;.</summary>
    public string? GameDir { get; set; }

    public int MemoryMb { get; set; } = 4096;
    public string JvmArgs { get; set; } = "";

    /// <summary>Путь к java.exe. Пусто — лаунчер сам скачает нужную версию Java.</summary>
    public string? JavaPath { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }

    public string ResolveGameDir() =>
        string.IsNullOrWhiteSpace(GameDir) ? Path.Combine(Paths.Instances, Id) : GameDir;

    public override string ToString() => Name;
}

public sealed class ProfileStore
{
    public List<Profile> Profiles { get; set; } = [];
    public string? SelectedId { get; set; }

    public static ProfileStore Load() => JsonFile.Load<ProfileStore>(Paths.ProfilesFile) ?? new ProfileStore();

    public void Save() => JsonFile.Save(Paths.ProfilesFile, this);
}
