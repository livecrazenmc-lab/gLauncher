using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLauncher.Core;
using GLauncher.Core.Content;
using GLauncher.Core.Java;
using Microsoft.Win32;

namespace GLauncher.ViewModels;

public partial class JavaViewModel : ObservableObject
{
    private const int MaxLogLines = 5000;

    private readonly AccountsViewModel _accounts;
    private readonly LauncherSettings _settings;
    private readonly ProfileStore _store = ProfileStore.Load();
    private IReadOnlyList<MinecraftVersionInfo> _allVersions = [];
    private CancellationTokenSource? _cts;

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

    /// <summary>Профиль выбран или его сохранённые настройки изменились (версия, модлоадер, папка).</summary>
    public event EventHandler? ProfileChanged;

    public ObservableCollection<Profile> Profiles { get; }
    public IReadOnlyList<ModLoader> Loaders { get; } = Enum.GetValues<ModLoader>();
    public ObservableCollection<string> McVersions { get; } = [];
    public ObservableCollection<string> Log { get; } = [];
    public string MemoryHint { get; } = $"В компьютере {SystemInfo.TotalMemoryMb} МБ ОЗУ";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(DeleteProfileCommand), nameof(ExportMrpackCommand))]
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
    [ObservableProperty] private string editServerAddress = "";
    [ObservableProperty] private string? memoryWarning;

    [ObservableProperty] private bool showSnapshots;

    // --- Состояние длительных операций ---
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(ImportMrpackCommand), nameof(ExportMrpackCommand))]
    private bool isBusy;

    [ObservableProperty] private string? status;
    [ObservableProperty] private double progressValue;
    [ObservableProperty] private bool progressIndeterminate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool hasJavaLicense;

    [ObservableProperty] private string licenseStatus = "";

    public async Task LoadVersionsAsync()
    {
        // Сначала кэш — список появится сразу, даже при медленной сети.
        _allVersions = VersionRepository.GetCachedVersions();
        if (_allVersions.Count > 0) FillVersions();
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

    partial void OnEditMemoryMbChanged(string value) =>
        MemoryWarning = int.TryParse(value, out var mb) ? SystemInfo.MemoryWarning(mb) : null;

    private void FillVersions()
    {
        var current = EditMcVersion;
        McVersions.Clear();
        foreach (var v in _allVersions.Where(v => v.Type == "release" || (ShowSnapshots && v.Type == "snapshot")))
            McVersions.Add(v.Id);
        if (SelectedProfile is not null && string.IsNullOrEmpty(SelectedProfile.MinecraftVersion) && McVersions.Count > 0)
        {
            // Профиль без версии (например, созданный при первом запуске) получает последний релиз.
            EditMcVersion = McVersions[0];
            SelectedProfile.MinecraftVersion = McVersions[0];
            _store.Save();
            CollectionViewSource.GetDefaultView(Profiles).Refresh();
            ProfileChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            EditMcVersion = current;
        }
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
        EditServerAddress = value.ServerAddress ?? "";
        ProfileChanged?.Invoke(this, EventArgs.Empty);
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

    public void AddProfile(Profile profile)
    {
        _store.Profiles.Add(profile);
        _store.Save();
        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    [RelayCommand]
    private void NewProfile() => AddProfile(new Profile { MinecraftVersion = McVersions.FirstOrDefault() ?? "" });

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
        var gameDir = string.IsNullOrWhiteSpace(EditGameDir) ? null : EditGameDir.Trim();

        // Сменили версию или модлоадер — нужно переустановить.
        var changed = p.Loader != EditLoader || p.MinecraftVersion != EditMcVersion.Trim() || p.LoaderVersion != loaderVersion;
        if (changed) p.InstalledVersionId = null;
        changed |= p.GameDir != gameDir;

        p.Name = string.IsNullOrWhiteSpace(EditName) ? "Профиль" : EditName.Trim();
        p.Loader = EditLoader;
        p.MinecraftVersion = EditMcVersion.Trim();
        p.LoaderVersion = loaderVersion;
        p.GameDir = gameDir;
        p.MemoryMb = int.TryParse(EditMemoryMb, out var mem) ? Math.Clamp(mem, 512, 65536) : 4096;
        p.JvmArgs = EditJvmArgs;
        p.JavaPath = string.IsNullOrWhiteSpace(EditJavaPath) ? null : EditJavaPath.Trim();
        p.Width = int.TryParse(EditWidth, out var w) && w > 0 ? w : null;
        p.Height = int.TryParse(EditHeight, out var h) && h > 0 ? h : null;
        p.ServerAddress = string.IsNullOrWhiteSpace(EditServerAddress) ? null : EditServerAddress.Trim();
        _store.Save();

        CollectionViewSource.GetDefaultView(Profiles).Refresh(); // обновить имя в списке
        EditMemoryMb = p.MemoryMb.ToString();
        if (changed) ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        if (SelectedProfile is null) return;
        var dir = SelectedProfile.ResolveGameDir();
        Directory.CreateDirectory(dir);
        Shell.Open(dir);
    }

    // --- Запуск ---

    private bool CanPlay() => SelectedProfile is not null && HasJavaLicense && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        SaveProfile();
        var profile = SelectedProfile!;
        if (SystemInfo.MemoryWarning(profile.MemoryMb) is { } warning &&
            MessageBox.Show(warning + "\n\nВсё равно запустить?", "gLauncher",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunBusyAsync("Проверка лицензии…", async (progress, ct) =>
        {
            var account = await _accounts.EnsureActiveValidAsync(ct);

            var launcher = new JavaGameLauncher(_settings);
            launcher.Log += line => Application.Current.Dispatcher.BeginInvoke(() => AppendLog(line));
            var startedUtc = DateTime.UtcNow;
            var process = await launcher.LaunchAsync(profile, account, progress, ct);
            _store.Save(); // мог измениться InstalledVersionId / LoaderVersion
            ProfileChanged?.Invoke(this, EventArgs.Empty);

            Status = $"Игра запущена ({profile.Name})";
            RunningGames++;
            process.Exited += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
                OnGameExited(profile, process.ExitCode, startedUtc));
            ApplyAfterLaunch();
        });
    }

    private void ApplyAfterLaunch()
    {
        var window = Application.Current.MainWindow;
        switch (_settings.AfterLaunch)
        {
            case AfterLaunchAction.Minimize when window is not null:
                window.WindowState = WindowState.Minimized;
                break;
            case AfterLaunchAction.Close:
                Application.Current.Shutdown();
                break;
        }
    }

    /// <summary>Сколько запущенных из лаунчера копий Java Edition ещё работает.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameRunning))]
    private int runningGames;

    public bool IsGameRunning => RunningGames > 0;

    private void OnGameExited(Profile profile, int exitCode, DateTime startedUtc)
    {
        RunningGames = Math.Max(0, RunningGames - 1);
        var window = Application.Current.MainWindow;
        if (window is { WindowState: WindowState.Minimized })
        {
            window.WindowState = WindowState.Normal;
            window.Activate();
        }

        // 0 — обычный выход; -1 / 1 при закрытии окна крестиком тоже бывают, но краш-репорта тогда нет.
        if (exitCode == 0)
        {
            Status = "Игра закрыта.";
            return;
        }

        Status = $"Игра завершилась с ошибкой (код {exitCode}).";
        var report = CrashReports.Find(profile.ResolveGameDir(), startedUtc);
        if (report is null) return;

        var isCrash = !report.EndsWith("latest.log", StringComparison.OrdinalIgnoreCase);
        var question = isCrash
            ? $"Игра аварийно завершилась (код {exitCode}).\n\nОткрыть отчёт о сбое?\n{report}"
            : $"Игра завершилась с ошибкой (код {exitCode}).\n\nОткрыть журнал игры?\n{report}";
        if (MessageBox.Show(question, "gLauncher", MessageBoxButton.YesNo, MessageBoxImage.Error) == MessageBoxResult.Yes)
            Shell.Open(report);
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    // --- Сборки .mrpack ---

    private bool CanImport() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportMrpackAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Сборка Modrinth (*.mrpack)|*.mrpack", Title = "Импорт сборки" };
        if (dialog.ShowDialog() != true) return;

        await RunBusyAsync("Импорт сборки…", async (progress, ct) =>
        {
            var profile = await MrpackService.ImportAsync(dialog.FileName, progress, ct);
            AddProfile(profile);
            Status = $"Сборка «{profile.Name}» импортирована.";
        });
    }

    /// <summary>Установка сборки из поиска Modrinth (вызывается со вкладки «Контент»).</summary>
    public Task InstallModpackAsync(string projectId) =>
        RunBusyAsync("Установка сборки…", async (progress, ct) =>
        {
            var profile = await MrpackService.InstallFromModrinthAsync(projectId, progress, ct);
            AddProfile(profile);
            Status = $"Сборка «{profile.Name}» установлена — профиль создан.";
        });

    private bool CanExport() => SelectedProfile is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportMrpackAsync()
    {
        SaveProfile();
        var profile = SelectedProfile!;
        var dialog = new SaveFileDialog
        {
            Filter = "Сборка Modrinth (*.mrpack)|*.mrpack",
            FileName = string.Join("_", profile.Name.Split(Path.GetInvalidFileNameChars())) + ".mrpack",
            Title = "Экспорт сборки",
        };
        if (dialog.ShowDialog() != true) return;

        await RunBusyAsync("Экспорт сборки…", async (progress, ct) =>
        {
            await MrpackService.ExportAsync(profile, dialog.FileName, "1.0.0", progress, ct);
            Status = $"Сборка сохранена: {dialog.FileName}";
        });
    }

    // --- Общее ---

    private async Task RunBusyAsync(string initialStatus, Func<IProgress<ProgressInfo>, CancellationToken, Task> action)
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        Status = initialStatus;
        ProgressIndeterminate = true;
        var progress = new Progress<ProgressInfo>(p =>
        {
            Status = p.Total > 0 ? $"{p.Stage}: {p.Done} / {p.Total}" : p.Stage;
            ProgressIndeterminate = p.Total == 0;
            ProgressValue = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
        });

        try
        {
            await action(progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "Отменено.";
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
            _cts = null;
        }
    }

    private void AppendLog(string line)
    {
        Log.Add(line);
        while (Log.Count > MaxLogLines) Log.RemoveAt(0);
    }
}
