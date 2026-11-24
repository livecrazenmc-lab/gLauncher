using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLauncher.Core;
using GLauncher.Core.Games;
using GLauncher.Core.Updates;
using GLauncher.Themes;

namespace GLauncher.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Settings = LauncherSettings.Load();
        Accounts = new AccountsViewModel(Settings);
        Java = new JavaViewModel(Accounts, Settings);
        Content = new ContentViewModel(Java);
        Skins = new SkinsViewModel(Accounts);
        Bedrock = new BedrockViewModel(Accounts, Settings);
        Dungeons = new GameViewModel(SpinoffGames.Dungeons, Accounts);
        DungeonsII = new GameViewModel(SpinoffGames.DungeonsII, Accounts);
        Legends = new GameViewModel(SpinoffGames.Legends, Accounts);
        Updates = new UpdatesViewModel(Settings);
        SettingsPage = new SettingsViewModel(Settings, Updates);

        // Плюшки зависят от активного аккаунта и списка благодарностей (он может обновиться с GitHub).
        Accounts.ActiveAccountChanged += (_, _) => UpdatePerks();
        // День рождения активного аккаунта превращает «Сезонную» тему в праздничную
        Accounts.BirthdayChanged += (_, _) => SettingsPage.RefreshSeasonal(Accounts.BirthdayGreeting is not null);
        GLauncher.Core.Players.CreditsService.Changed += () =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(UpdatePerks);
        UpdatePerks();

        // Автообновление не перезапускает лаунчер посреди игры, установки или загрузки
        Updates.IsLauncherBusy = () => Java.IsBusy || Java.IsGameRunning || Content.IsBusy || Skins.IsBusy;
        void Recheck(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is "IsBusy" or "IsGameRunning") Updates.TryRestartPending();
        }
        Java.PropertyChanged += Recheck;
        Content.PropertyChanged += Recheck;
        Skins.PropertyChanged += Recheck;
    }

    /// <summary>Человек из благодарностей, если активный аккаунт есть в списке (сверка по UUID).</summary>
    [ObservableProperty] private GLauncher.Core.Players.CreditPerson? creditedPerson;

    public bool IsCredited => CreditedPerson is not null;

    partial void OnCreditedPersonChanged(GLauncher.Core.Players.CreditPerson? value) => OnPropertyChanged(nameof(IsCredited));

    private void UpdatePerks()
    {
        CreditedPerson = GLauncher.Core.Players.CreditsService.Find(Accounts.Selected?.Uuid);
        SettingsPage.ApplyPerks(CreditedPerson);
    }

    // --- Боковая панель: свёрнута до иконок или развёрнута (запоминается) ---

    public bool SidebarCollapsed => Settings.SidebarCollapsed;
    public string SidebarToggleText => SidebarCollapsed ? "Развернуть панель" : "Свернуть панель";
    public string SidebarToggleGlyph => SidebarCollapsed ? "\uE76C" : "\uE76B";

    [RelayCommand]
    private void ToggleSidebar()
    {
        Settings.SidebarCollapsed = !Settings.SidebarCollapsed;
        Settings.Save();
        OnPropertyChanged(nameof(SidebarCollapsed));
        OnPropertyChanged(nameof(SidebarToggleText));
        OnPropertyChanged(nameof(SidebarToggleGlyph));
    }

    public LauncherSettings Settings { get; }
    public AccountsViewModel Accounts { get; }
    public JavaViewModel Java { get; }
    public PatchNotesViewModel JavaPatchNotes { get; } = new(PatchNotesKind.Java, hasSnapshots: true);
    public ContentViewModel Content { get; }
    public SkinsViewModel Skins { get; }
    public BedrockViewModel Bedrock { get; }
    public GameViewModel Dungeons { get; }
    public GameViewModel DungeonsII { get; }
    public GameViewModel Legends { get; }
    public UpdatesViewModel Updates { get; }
    public SettingsViewModel SettingsPage { get; }

    public async Task InitializeAsync()
    {
        var updates = Settings.CheckForUpdates ? Updates.CheckOnStartupAsync() : Task.CompletedTask;
        await Task.WhenAll(Accounts.InitializeAsync(), Java.LoadVersionsAsync(), Content.ReloadAsync(), updates,
            SettingsPage.Credits.RefreshAsync(), Skins.InitializeAsync(), RefreshGamesAsync(), Accounts.LoadInitialFaceAsync(),
            LoadArtsAsync());
    }

    // --- Официальные арты игр для обложек (каталог Microsoft Store, кэш на диске) ---

    private async Task LoadArtsAsync()
    {
        ShowArts();                                           // сразу — из кэша
        if ((await GameArt.RefreshAsync(CancellationToken.None)).Count > 0) ShowArts();
    }

    private void ShowArts()
    {
        Java.HeroArt = LoadArt(GameArtId.Java);
        Bedrock.HeroArt = LoadArt(GameArtId.Bedrock);
        Dungeons.HeroArt = LoadArt(GameArtId.Dungeons);
        DungeonsII.HeroArt = LoadArt(GameArtId.DungeonsII);
        Legends.HeroArt = LoadArt(GameArtId.Legends);
    }

    private static System.Windows.Media.ImageSource? LoadArt(GameArtId id)
    {
        if (GameArt.Cached(id) is not { } path) return null;
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            using var stream = System.IO.File.OpenRead(path);
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 1600;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null; // битый файл — останется свой куб
        }
    }

    /// <summary>Ищет установленные Dungeons, Dungeons II и Legends (Microsoft Store/Xbox и Steam).</summary>
    [RelayCommand]
    private async Task RefreshGamesAsync()
    {
        var found = await Task.Run(SpinoffGames.DetectAll);
        Dungeons.SetInstalls(found[SpinoffGames.Dungeons.Id]);
        DungeonsII.SetInstalls(found[SpinoffGames.DungeonsII.Id]);
        Legends.SetInstalls(found[SpinoffGames.Legends.Id]);
    }
}

/// <summary>Проверка и установка обновлений лаунчера.</summary>
public partial class UpdatesViewModel(LauncherSettings settings) : ObservableObject
{
    public string CurrentVersion => UpdateService.CurrentVersionText;
    public bool IsSupported => UpdateService.IsSupported;

    /// <summary>Не null — найдено обновление (показывается баннер в главном окне).</summary>
    [ObservableProperty] private UpdateInfo? available;
    [ObservableProperty] private string? status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand), nameof(InstallCommand))]
    private bool isBusy;

    [ObservableProperty] private double progressValue;

    /// <summary>Новая версия уже на месте exe, осталось перезапустить лаунчер (ждём, пока он освободится).</summary>
    [ObservableProperty] private bool restartPending;

    /// <summary>true — идёт игра, установка или загрузка: перезапуск откладывается.</summary>
    public Func<bool>? IsLauncherBusy { get; set; }

    public string AvailableText => Available is null ? ""
        : RestartPending ? $"gLauncher {Available.Tag} установлен. Лаунчер перезапустится, когда закончится игра или загрузка."
        : IsBusy && ProgressValue > 0 ? $"Загрузка gLauncher {Available.Tag}…"
        : $"Доступна новая версия gLauncher {Available.Tag} (у вас {CurrentVersion}).";

    public string InstallButtonText => RestartPending ? "Перезапустить сейчас" : "Обновить";

    partial void OnAvailableChanged(UpdateInfo? value) => OnPropertyChanged(nameof(AvailableText));
    partial void OnRestartPendingChanged(bool value)
    {
        OnPropertyChanged(nameof(AvailableText));
        OnPropertyChanged(nameof(InstallButtonText));
    }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(AvailableText));

    /// <summary>Проверка при запуске; если включено — сразу скачать и поставить обновление.</summary>
    public async Task CheckOnStartupAsync()
    {
        await CheckAsync(silent: true);
        if (Available is not null && settings.AutoInstallUpdates && UpdateService.IsSupported)
            await InstallAsync();
    }

    public async Task CheckAsync(bool silent)
    {
        if (silent && !UpdateService.IsSupported) return;
        IsBusy = true;
        try
        {
            Status = "Проверка обновлений…";
            Available = await UpdateService.CheckAsync(CancellationToken.None);
            Status = Available is null ? $"Установлена последняя версия ({CurrentVersion})." : AvailableText;
        }
        catch (Exception ex)
        {
            Status = "Не удалось проверить обновления: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool NotBusy() => !IsBusy;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task CheckNowAsync() => CheckAsync(silent: false);

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task InstallAsync()
    {
        if (Available is null) return;
        if (RestartPending)
        {
            RestartNow(); // кнопка «Перезапустить сейчас»
            return;
        }
        if (!UpdateService.IsSupported)
        {
            Status = "Автообновление работает только в опубликованной сборке. Скачайте релиз вручную.";
            Shell.Open(Available.PageUrl);
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new Progress<ProgressInfo>(p =>
            {
                Status = p.Stage;
                ProgressValue = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
                OnPropertyChanged(nameof(AvailableText));
            });
            await UpdateService.ApplyAsync(Available, progress, CancellationToken.None);
            // exe уже заменён: даже если лаунчер закроют сейчас, следующий запуск будет новой версией
            RestartPending = true;
            Status = $"Обновление {Available.Tag} установлено.";
        }
        catch (Exception ex)
        {
            Status = "Не удалось обновиться: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
        }
        TryRestartPending();
    }

    /// <summary>Перезапуск в новую версию, как только лаунчер ничем не занят.</summary>
    public void TryRestartPending()
    {
        if (!RestartPending || IsBusy || IsLauncherBusy?.Invoke() == true) return;
        RestartNow();
    }

    private void RestartNow()
    {
        Status = "Перезапуск…";
        UpdateService.Restart();
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (Available is not null) Shell.Open(Available.PageUrl);
    }

    [RelayCommand]
    private void Dismiss() => Available = null;

    public bool CheckOnStartup
    {
        get => settings.CheckForUpdates;
        set
        {
            settings.CheckForUpdates = value;
            settings.Save();
            OnPropertyChanged();
        }
    }

    public bool AutoInstall
    {
        get => settings.AutoInstallUpdates;
        set
        {
            settings.AutoInstallUpdates = value;
            settings.Save();
            OnPropertyChanged();
        }
    }
}

public sealed record AfterLaunchOption(AfterLaunchAction Action, string Title)
{
    public override string ToString() => Title;
}

public sealed record ThemeOption(ThemeId Id, string Title)
{
    public override string ToString() => Title;
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly LauncherSettings _settings;

    public SettingsViewModel(LauncherSettings settings, UpdatesViewModel updates)
    {
        _settings = settings;
        Updates = updates;
        clientId = settings.MicrosoftClientId;
        selectedAfterLaunch = AfterLaunchOptions.First(o => o.Action == settings.AfterLaunch);

        // «Сезонная» сама выбирает оформление по дате (праздник важнее времени года); отдельных сезонов в списке нет.
        ThemeOptions = [new(ThemeId.Auto, "Сезонная"), new(ThemeId.Blocks, ThemeManager.Get(ThemeId.Blocks).Title)];
        var themeId = ThemeManager.FromSetting(settings.Theme);
        if (themeId.ToString() != settings.Theme)
        {
            settings.Theme = themeId.ToString(); // старое значение (сезон/праздник) → «Сезонная»
            settings.Save();
        }
        selectedTheme = ThemeOptions.FirstOrDefault(o => o.Id == themeId) ?? ThemeOptions[0];
        currentThemeText = ThemeText(themeId);
#if DEBUG
        showDeveloper = true; // в отладочной сборке разработчик видит всё
#endif
    }

    public UpdatesViewModel Updates { get; }

    public CreditsViewModel Credits { get; } = new();

    // --- Шрифт интерфейса: пиксельный по умолчанию или любой из системы ---

    private List<FontOption>? _fontOptions;

    /// <summary>Список строится при первом открытии настроек (шрифтов в системе бывают сотни).</summary>
    public List<FontOption> FontOptions => _fontOptions ??= FontManager.Options();

    public FontOption SelectedFont
    {
        get => FontOptions.FirstOrDefault(o => string.Equals(o.Family, _settings.UiFont, StringComparison.OrdinalIgnoreCase)) ?? FontOptions[0];
        set
        {
            if (value is null) return;
            _settings.UiFont = value.Family;
            _settings.Save();
            FontManager.Apply(value.Family);
            OnPropertyChanged();
        }
    }

    /// <summary>Системная рамка окна вместо своей; применяется сразу.</summary>
    public bool SystemFrame
    {
        get => _settings.SystemFrame;
        set
        {
            _settings.SystemFrame = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Анимированные частицы на фоне (снег, листья…). Выключаются в «Оформлении».</summary>
    public bool AnimatedBackground
    {
        get => _settings.AnimatedBackground;
        set
        {
            _settings.AnimatedBackground = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    // --- Плюшки для людей из благодарностей ---

    /// <summary>Блок «Для разработчиков» — только для людей из благодарностей (по UUID аккаунта).</summary>
    [ObservableProperty] private bool showDeveloper;

    [ObservableProperty] private bool hasExclusiveThemes;

    /// <summary>Вызывается при смене аккаунта и при обновлении credits.json.</summary>
    public void ApplyPerks(GLauncher.Core.Players.CreditPerson? person)
    {
        var credited = person is not null;
#if DEBUG
        ShowDeveloper = true;
#else
        ShowDeveloper = credited;
#endif
        HasExclusiveThemes = credited;

        var exclusive = ThemeManager.Themes.Where(t => ThemeManager.IsExclusive(t.Id)).ToList();
        if (credited)
        {
            foreach (var t in exclusive.Where(t => ThemeOptions.All(o => o.Id != t.Id)))
                ThemeOptions.Add(new ThemeOption(t.Id, t.Title));
            // Тема, выбранная раньше, но скрытая до проверки аккаунта
            if (Enum.TryParse<ThemeId>(_settings.Theme, out var saved) && ThemeManager.IsExclusive(saved) && SelectedTheme.Id != saved)
                SelectedTheme = ThemeOptions.First(o => o.Id == saved);
        }
        else
        {
            if (ThemeManager.IsExclusive(SelectedTheme.Id)) SelectedTheme = ThemeOptions[0];
            foreach (var o in ThemeOptions.Where(o => ThemeManager.IsExclusive(o.Id)).ToList()) ThemeOptions.Remove(o);
        }
    }

    // --- Оформление ---

    public System.Collections.ObjectModel.ObservableCollection<ThemeOption> ThemeOptions { get; }

    [ObservableProperty] private ThemeOption selectedTheme;
    [ObservableProperty] private string currentThemeText;

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        _settings.Theme = value.Id.ToString();
        _settings.Save();
        ThemeManager.Apply(ThemeManager.Resolve(value.Id));
        CurrentThemeText = ThemeText(value.Id);
    }

    /// <summary>Пересчитать «Сезонную» тему (например, наступил или указан день рождения).</summary>
    public void RefreshSeasonal(bool birthday)
    {
        if (ThemeManager.BirthdayToday == birthday) return;
        ThemeManager.BirthdayToday = birthday;
        if (SelectedTheme.Id != ThemeId.Auto) return;
        ThemeManager.Apply(ThemeManager.Resolve(ThemeId.Auto));
        CurrentThemeText = ThemeText(ThemeId.Auto);
    }

    private static string ThemeText(ThemeId id) =>
        id == ThemeId.Auto ? $"Сейчас: {ThemeManager.Resolve(ThemeId.Auto).Title}" : "";

    public IReadOnlyList<AfterLaunchOption> AfterLaunchOptions { get; } =
    [
        new(AfterLaunchAction.KeepOpen, "Оставить лаунчер открытым"),
        new(AfterLaunchAction.Minimize, "Свернуть лаунчер (развернуть после выхода из игры)"),
        new(AfterLaunchAction.Close, "Закрыть лаунчер"),
    ];

    [ObservableProperty] private string clientId;
    [ObservableProperty] private AfterLaunchOption selectedAfterLaunch;
    [ObservableProperty] private string? status;

    public string DataFolder => Paths.Root;

    public string BuiltInClientIdStatus => GLauncher.Core.Auth.AuthConfig.HasBuiltInClientId
        ? $"Вход через Microsoft настроен (встроенный Client ID {GLauncher.Core.Auth.AuthConfig.BuiltInClientId})."
        : "В эту сборку не вшит Client ID: вход через Microsoft не работает. Укажите его в Directory.Build.props (GLauncherClientId) и выпустите новую версию.";

    partial void OnSelectedAfterLaunchChanged(AfterLaunchOption value)
    {
        _settings.AfterLaunch = value.Action;
        _settings.Save();
    }

    [RelayCommand]
    private void Save()
    {
        _settings.MicrosoftClientId = ClientId.Trim();
        _settings.Save();
        Status = "Сохранено.";
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(Paths.Root);
        Shell.Open(Paths.Root);
    }

    [RelayCommand]
    private void OpenAzureGuide() =>
        Shell.Open("https://learn.microsoft.com/entra/identity-platform/quickstart-register-app");

    [RelayCommand]
    private void OpenApprovalForm() => Shell.Open("https://aka.ms/mce-reviewappid");
}

