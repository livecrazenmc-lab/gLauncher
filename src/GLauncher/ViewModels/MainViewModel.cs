using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLauncher.Core;
using GLauncher.Core.Updates;

namespace GLauncher.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Settings = LauncherSettings.Load();
        Accounts = new AccountsViewModel(Settings);
        Java = new JavaViewModel(Accounts, Settings);
        Content = new ContentViewModel(Java);
        Bedrock = new BedrockViewModel(Accounts);
        Updates = new UpdatesViewModel(Settings);
        SettingsPage = new SettingsViewModel(Settings, Updates);
    }

    public LauncherSettings Settings { get; }
    public AccountsViewModel Accounts { get; }
    public JavaViewModel Java { get; }
    public ContentViewModel Content { get; }
    public BedrockViewModel Bedrock { get; }
    public UpdatesViewModel Updates { get; }
    public SettingsViewModel SettingsPage { get; }

    public async Task InitializeAsync()
    {
        var updates = Settings.CheckForUpdates ? Updates.CheckAsync(silent: true) : Task.CompletedTask;
        await Task.WhenAll(Accounts.InitializeAsync(), Java.LoadVersionsAsync(), Content.ReloadAsync(), updates);
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

    public string AvailableText => Available is null ? "" : $"Доступна новая версия gLauncher {Available.Tag} (у вас {CurrentVersion}).";

    partial void OnAvailableChanged(UpdateInfo? value) => OnPropertyChanged(nameof(AvailableText));

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
            });
            await UpdateService.ApplyAsync(Available, progress, CancellationToken.None);
            Status = "Перезапуск…";
            UpdateService.Restart();
            Application.Current.Shutdown();
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
}

public sealed record AfterLaunchOption(AfterLaunchAction Action, string Title)
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
    }

    public UpdatesViewModel Updates { get; }

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
