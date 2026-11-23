using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLauncher.Core;
using GLauncher.Core.Java;

namespace GLauncher.ViewModels;

public partial class JavaViewModel : ObservableObject
{
    private const int MaxLogLines = 5000;

    private readonly AccountsViewModel _accounts;
    private readonly LauncherSettings _settings;
    private readonly ProfileStore _store = ProfileStore.Load();
    private IReadOnlyList<MinecraftVersionInfo> _allVersions = [];

    public JavaViewModel(AccountsViewModel accounts, LauncherSettings settings)
    {
        _accounts = accounts;
        _settings = settings;
        showSnapshots = settings.ShowSnapshots;

        if (_store.Profiles.Count == 0)
        {
            _store.Profiles.Add(new Profile { Name = "Последний релиз" });
            _store.Save();
        }
        Profiles = new ObservableCollection<Profile>(_store.Profiles);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _store.SelectedId) ?? Profiles.First();

        _accounts.ActiveAccountChanged += (_, _) => RefreshLicense();
        RefreshLicense();
    }

    public ObservableCollection<Profile> Profiles { get; }
    public IReadOnlyList<ModLoader> Loaders { get; } = Enum.GetValues<ModLoader>();
    public ObservableCollection<string> McVersions { get; } = [];
    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(DeleteProfileCommand))]
    private Profile? selectedProfile;

    // --- Поля редактора профиля ---
    [ObservableProperty] private string editName = "";
    [ObservableProperty] private ModLoader editLoader;
    [ObservableProperty] private string editMcVersion = "";
    [ObservableProperty] private string editLoaderVersion = "";
    [ObservableProperty] private string editGameDir = "";
    [ObservableProperty] private string editMemoryMb = "4096";
    [ObservableProperty] private string editJvmArgs = "";
    [ObservableProperty] private string editJavaPath = "";
    [ObservableProperty] private string editWidth = "";
    [ObservableProperty] private string editHeight = "";

    [ObservableProperty] private bool showSnapshots;

    // --- Состояние запуска ---
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool isBusy;

    [ObservableProperty] private string? status;
    [ObservableProperty] private double progressValue;
    [ObservableProperty] private bool progressIndeterminate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool hasJavaLicense;

    [ObservableProperty] private string licenseStatus = "";

    private CancellationTokenSource? _playCts;

    public async Task LoadVersionsAsync()
    {
        try
        {
            _allVersions = await VersionRepository.GetVersionsAsync();
            FillVersions();
        }
        catch (Exception ex)
        {
            Status = "Не удалось загрузить список версий: " + ex.Message;
        }
    }

    partial void OnShowSnapshotsChanged(bool value)
    {
        _settings.ShowSnapshots = value;
        _settings.Save();
        FillVersions();
    }

    private void FillVersions()
    {
        var current = EditMcVersion;
        McVersions.Clear();
        foreach (var v in _allVersions.Where(v => v.Type == "release" || (ShowSnapshots && v.Type == "snapshot")))
            McVersions.Add(v.Id);
        if (SelectedProfile is not null && string.IsNullOrEmpty(SelectedProfile.MinecraftVersion) && McVersions.Count > 0)
            EditMcVersion = McVersions[0];
        else
            EditMcVersion = current;
    }

    partial void OnSelectedProfileChanged(Profile? value)
    {
        if (value is null) return;
        _store.SelectedId = value.Id;
        _store.Save();

        EditName = value.Name;
        EditLoader = value.Loader;
        EditMcVersion = string.IsNullOrEmpty(value.MinecraftVersion) && McVersions.Count > 0 ? McVersions[0] : value.MinecraftVersion;
        EditLoaderVersion = value.LoaderVersion ?? "";
        EditGameDir = value.GameDir ?? "";
        EditMemoryMb = value.MemoryMb.ToString();
        EditJvmArgs = value.JvmArgs;
        EditJavaPath = value.JavaPath ?? "";
        EditWidth = value.Width?.ToString() ?? "";
        EditHeight = value.Height?.ToString() ?? "";
    }

    private void RefreshLicense()
    {
        var account = _accounts.Selected;
        HasJavaLicense = account?.OwnsJava == true;
        LicenseStatus = account switch
        {
            null => "Войдите в аккаунт Microsoft, чтобы играть.",
            { OwnsJava: true } => $"Игрок: {account.Username}",
            _ => $"На аккаунте {account.DisplayName} нет лицензии Java Edition.",
        };
    }

    [RelayCommand]
    private void NewProfile()
    {
        var profile = new Profile { MinecraftVersion = McVersions.FirstOrDefault() ?? "" };
        _store.Profiles.Add(profile);
        _store.Save();
        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    private bool HasProfile() => SelectedProfile is not null;

    [RelayCommand(CanExecute = nameof(HasProfile))]
    private void DeleteProfile()
    {
        if (SelectedProfile is null) return;
        if (MessageBox.Show($"Удалить профиль «{SelectedProfile.Name}»?\nПапка игры с мирами останется на диске.",
                "gLauncher", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _store.Profiles.Remove(SelectedProfile);
        Profiles.Remove(SelectedProfile);
        if (Profiles.Count == 0) NewProfile();
        _store.Save();
        SelectedProfile = Profiles.First();
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (SelectedProfile is null) return;
        var p = SelectedProfile;
        var loaderVersion = string.IsNullOrWhiteSpace(EditLoaderVersion) ? null : EditLoaderVersion.Trim();

        // Сменили версию или модлоадер — нужно переустановить.
        if (p.Loader != EditLoader || p.MinecraftVersion != EditMcVersion.Trim() || p.LoaderVersion != loaderVersion)
            p.InstalledVersionId = null;

        p.Name = string.IsNullOrWhiteSpace(EditName) ? "Профиль" : EditName.Trim();
        p.Loader = EditLoader;
        p.MinecraftVersion = EditMcVersion.Trim();
        p.LoaderVersion = loaderVersion;
        p.GameDir = string.IsNullOrWhiteSpace(EditGameDir) ? null : EditGameDir.Trim();
        p.MemoryMb = int.TryParse(EditMemoryMb, out var mem) ? Math.Clamp(mem, 512, 65536) : 4096;
        p.JvmArgs = EditJvmArgs;
        p.JavaPath = string.IsNullOrWhiteSpace(EditJavaPath) ? null : EditJavaPath.Trim();
        p.Width = int.TryParse(EditWidth, out var w) && w > 0 ? w : null;
        p.Height = int.TryParse(EditHeight, out var h) && h > 0 ? h : null;
        _store.Save();

        // Обновить имя в списке
        System.Windows.Data.CollectionViewSource.GetDefaultView(Profiles).Refresh();
        EditMemoryMb = p.MemoryMb.ToString();
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        if (SelectedProfile is null) return;
        var dir = SelectedProfile.ResolveGameDir();
        Directory.CreateDirectory(dir);
        Shell.Open(dir);
    }

    private bool CanPlay() => SelectedProfile is not null && HasJavaLicense && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        SaveProfile();
        var profile = SelectedProfile!;
        IsBusy = true;
        _playCts = new CancellationTokenSource();
        var progress = new Progress<ProgressInfo>(p =>
        {
            Status = p.Total > 0 ? $"{p.Stage}: {p.Done} / {p.Total}" : p.Stage;
            ProgressIndeterminate = p.Total == 0;
            ProgressValue = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
        });

        try
        {
            Status = "Проверка лицензии…";
            ProgressIndeterminate = true;
            var account = await _accounts.EnsureActiveValidAsync(_playCts.Token);

            var launcher = new JavaGameLauncher(_settings);
            launcher.Log += line => Application.Current.Dispatcher.BeginInvoke(() => AppendLog(line));
            var process = await launcher.LaunchAsync(profile, account, progress, _playCts.Token);
            _store.Save(); // мог измениться InstalledVersionId

            Status = $"Игра запущена ({profile.Name})";
            process.Exited += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
                Status = $"Игра закрыта (код {process.ExitCode})");
        }
        catch (OperationCanceledException)
        {
            Status = "Запуск отменён.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            AppendLog("[gLauncher] Ошибка: " + ex);
        }
        finally
        {
            IsBusy = false;
            ProgressIndeterminate = false;
            ProgressValue = 0;
            _playCts = null;
        }
    }

    [RelayCommand]
    private void CancelPlay() => _playCts?.Cancel();

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    private void AppendLog(string line)
    {
        Log.Add(line);
        while (Log.Count > MaxLogLines) Log.RemoveAt(0);
    }
}
