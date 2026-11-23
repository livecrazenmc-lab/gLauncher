using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLauncher.Core;
using GLauncher.Core.Content;
using GLauncher.Core.Java;

namespace GLauncher.ViewModels;

public sealed record ContentTypeOption(ContentType Type, string Title)
{
    public override string ToString() => Title;
}

/// <summary>Строка результата поиска Modrinth.</summary>
public partial class SearchResultViewModel(ModrinthHit hit, bool installed) : ObservableObject
{
    public ModrinthHit Hit { get; } = hit;
    public string Title => Hit.Title;
    public string Description => Hit.Description;
    public string Author => Hit.Author;
    public string Downloads => Hit.Downloads.ToString("N0");
    public string? IconUrl => Hit.IconUrl;
    public string PageUrl => $"https://modrinth.com/{ModrinthClient.ProjectTypeName(Hit.Type)}/{Hit.Slug}";

    [ObservableProperty] private bool isInstalled = installed;
}

/// <summary>Строка установленного контента; IsEnabled переключает «.disabled».</summary>
public partial class InstalledItemViewModel : ObservableObject
{
    private readonly ContentViewModel _owner;

    public InstalledItemViewModel(InstalledItem item, ContentViewModel owner)
    {
        Item = item;
        _owner = owner;
        isEnabled = item.Enabled;
    }

    public InstalledItem Item { get; private set; }
    public string Title => Item.Title;
    public string FileName => Item.DisplayFileName;
    public string? Version => Item.Version;

    [ObservableProperty] private bool isEnabled;

    partial void OnIsEnabledChanged(bool value) => _owner.SetEnabled(this, value);
}

public partial class ContentViewModel : ObservableObject
{
    private const int PageSize = 20;

    private readonly JavaViewModel _java;
    private int _offset;

    public ContentViewModel(JavaViewModel java)
    {
        _java = java;
        selectedType = Types[0];
        _java.ProfileChanged += (_, _) => _ = ReloadAsync();
    }

    public IReadOnlyList<ContentTypeOption> Types { get; } =
    [
        new(ContentType.Mod, "Моды"),
        new(ContentType.ResourcePack, "Ресурспаки"),
        new(ContentType.Shader, "Шейдеры"),
        new(ContentType.Modpack, "Сборки"),
    ];

    public ObservableCollection<InstalledItemViewModel> Installed { get; } = [];
    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    [ObservableProperty] private ContentTypeOption selectedType;
    [ObservableProperty] private string searchQuery = "";
    [ObservableProperty] private string? status;
    [ObservableProperty] private string profileInfo = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(LoadMoreCommand), nameof(InstallCommand))]
    private bool isBusy;

    /// <summary>Сборки создают новый профиль, поэтому список «установлено» для них не показывается.</summary>
    public bool ShowsInstalled => SelectedType.Type != ContentType.Modpack;

    private Profile? Profile => _java.SelectedProfile;

    partial void OnSelectedTypeChanged(ContentTypeOption value)
    {
        OnPropertyChanged(nameof(ShowsInstalled));
        _ = ReloadAsync();
    }

    /// <summary>Обновить всё при открытии вкладки или смене профиля.</summary>
    public async Task ReloadAsync()
    {
        RefreshInstalled();
        await SearchAsync();
    }

    private void RefreshInstalled()
    {
        Installed.Clear();
        var profile = Profile;
        if (profile is null)
        {
            ProfileInfo = "Профиль не выбран.";
            return;
        }

        var loader = profile.Loader == ModLoader.Vanilla ? "без модлоадера" : profile.Loader.ToString();
        ProfileInfo = $"Профиль: {profile.Name} — Minecraft {profile.MinecraftVersion}, {loader}";
        if (!ShowsInstalled) return;

        foreach (var item in new ContentManager(profile).List(SelectedType.Type))
            Installed.Add(new InstalledItemViewModel(item, this));
    }

    private bool CanSearch() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        _offset = 0;
        Results.Clear();
        await LoadPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private Task LoadMoreAsync() => LoadPageAsync();

    private async Task LoadPageAsync()
    {
        var profile = Profile;
        if (profile is null) return;

        var type = SelectedType.Type;
        if (type == ContentType.Mod && profile.Loader == ModLoader.Vanilla)
        {
            Status = "Чтобы ставить моды, выберите в профиле модлоадер (Fabric, Quilt, Forge или NeoForge) и сохраните профиль.";
            return;
        }

        IsBusy = true;
        try
        {
            Status = "Поиск…";
            // Сборки ищем без фильтра по версии: они сами создают профиль нужной версии.
            var gameVersion = type == ContentType.Modpack ? null : profile.MinecraftVersion;
            var loaders = type == ContentType.Mod ? ModrinthClient.LoadersFor(profile.Loader) : null;
            var hits = await ModrinthClient.SearchAsync(SearchQuery.Trim(), type, gameVersion, loaders, _offset, PageSize, CancellationToken.None);

            var installed = type == ContentType.Modpack ? [] : new ContentManager(profile).InstalledProjectIds(type);
            foreach (var hit in hits)
                Results.Add(new SearchResultViewModel(hit, installed.Contains(hit.ProjectId)));
            _offset += hits.Count;
            Status = Results.Count == 0 ? "Ничего не найдено." : null;
        }
        catch (Exception ex)
        {
            Status = "Ошибка поиска: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task InstallAsync(SearchResultViewModel? result)
    {
        var profile = Profile;
        if (result is null || profile is null) return;

        if (result.Hit.Type == ContentType.Modpack)
        {
            if (MessageBox.Show($"Установить сборку «{result.Title}»? Для неё будет создан новый профиль.",
                    "gLauncher", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            IsBusy = true;
            Status = "Установка сборки — ход установки на вкладке Java Edition.";
            try { await _java.InstallModpackAsync(result.Hit.ProjectId); }
            finally { IsBusy = false; }
            Status = _java.Status;
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new Progress<ProgressInfo>(p => Status = p.Stage);
            var installed = await new ContentManager(profile).InstallAsync(result.Hit.ProjectId, result.Hit.Type, progress, CancellationToken.None);
            result.IsInstalled = true;
            Status = installed.Count > 1
                ? $"Установлено: {string.Join(", ", installed)} (вместе с зависимостями)."
                : $"Установлено: {installed.FirstOrDefault()}.";
            RefreshInstalled();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenPage(SearchResultViewModel? result)
    {
        if (result is not null) Shell.Open(result.PageUrl);
    }

    [RelayCommand]
    private void Delete(InstalledItemViewModel? item)
    {
        if (item is null || Profile is null) return;
        if (MessageBox.Show($"Удалить «{item.Title}»?", "gLauncher", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;
        try
        {
            new ContentManager(Profile).Delete(item.Item);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        RefreshInstalled();
        foreach (var r in Results) r.IsInstalled = false;
        _ = MarkInstalledAsync();
    }

    private Task MarkInstalledAsync()
    {
        if (Profile is null || !ShowsInstalled) return Task.CompletedTask;
        var installed = new ContentManager(Profile).InstalledProjectIds(SelectedType.Type);
        foreach (var r in Results) r.IsInstalled = installed.Contains(r.Hit.ProjectId);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (Profile is null || !ShowsInstalled) return;
        var folder = new ContentManager(Profile).Folder(SelectedType.Type);
        Directory.CreateDirectory(folder);
        Shell.Open(folder);
    }

    [RelayCommand]
    private void RefreshInstalledList() => RefreshInstalled();

    internal void SetEnabled(InstalledItemViewModel vm, bool enabled)
    {
        if (Profile is null) return;
        try
        {
            new ContentManager(Profile).SetEnabled(vm.Item, enabled);
        }
        catch (Exception ex)
        {
            Status = ex.Message; // например, игра запущена и файл занят
        }
        // Обновляем список после того, как привязка чекбокса закончит работу.
        Application.Current.Dispatcher.BeginInvoke(RefreshInstalled);
    }
}
