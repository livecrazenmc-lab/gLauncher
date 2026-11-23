using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLauncher.Core;

namespace GLauncher.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Settings = LauncherSettings.Load();
        Accounts = new AccountsViewModel(Settings);
        Java = new JavaViewModel(Accounts, Settings);
        Bedrock = new BedrockViewModel(Accounts);
        SettingsPage = new SettingsViewModel(Settings);
    }

    public LauncherSettings Settings { get; }
    public AccountsViewModel Accounts { get; }
    public JavaViewModel Java { get; }
    public BedrockViewModel Bedrock { get; }
    public SettingsViewModel SettingsPage { get; }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Accounts.InitializeAsync(), Java.LoadVersionsAsync());
    }
}

public partial class SettingsViewModel(LauncherSettings settings) : ObservableObject
{
    [ObservableProperty] private string clientId = settings.MicrosoftClientId;
    [ObservableProperty] private string? status;

    public string DataFolder => Paths.Root;

    [RelayCommand]
    private void Save()
    {
        settings.MicrosoftClientId = ClientId.Trim();
        settings.Save();
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
