using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLauncher.Core;
using GLauncher.Core.Bedrock;
using GLauncher.Core.Games;

namespace GLauncher.ViewModels;

public sealed record BedrockChannelOption(BedrockEdition Edition, string Title)
{
    public override string ToString() => Title;
}

public partial class BedrockViewModel : ObservableObject
{
    private readonly AccountsViewModel _accounts;
    private readonly LauncherSettings _settings;

    public BedrockViewModel(AccountsViewModel accounts, LauncherSettings settings)
    {
        _accounts = accounts;
        _settings = settings;
        selectedChannel = Channels[settings.BedrockPreview ? 1 : 0];
        _accounts.ActiveAccountChanged += (_, _) => RefreshLicense();
        RefreshLicense();
        RefreshInstall();
    }

    public IReadOnlyList<BedrockChannelOption> Channels { get; } =
    [
        new(BedrockEdition.Release, "Последний выпуск"),
        new(BedrockEdition.Preview, "Minecraft Preview (тестовые версии)"),
    ];

    public PatchNotesViewModel PatchNotes { get; } = new(PatchNotesKind.Bedrock, hasSnapshots: true);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private BedrockChannelOption selectedChannel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool hasBedrockLicense;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool installed;

    [ObservableProperty] private string licenseStatus = "";
    [ObservableProperty] private string installStatus = "";
    [ObservableProperty] private string storeButtonText = "Установить";
    [ObservableProperty] private string? status;

    partial void OnSelectedChannelChanged(BedrockChannelOption value)
    {
        _settings.BedrockPreview = value.Edition == BedrockEdition.Preview;
        _settings.Save();
        RefreshInstall();
    }

    private void RefreshLicense()
    {
        var account = _accounts.Selected;
        HasBedrockLicense = account?.OwnsBedrock == true;
        LicenseStatus = account switch
        {
            null => "Войдите в аккаунт Microsoft, чтобы играть.",
            { OwnsBedrock: true } => $"Лицензия Bedrock: есть ({account.DisplayName})",
            _ => $"На аккаунте {account.DisplayName} нет лицензии Bedrock Edition.",
        };
    }

    [RelayCommand]
    private void RefreshInstall()
    {
        var info = BedrockService.GetInstallInfo(SelectedChannel.Edition);
        Installed = info.Installed;
        InstallStatus = info.Installed ? $"Установлена версия {info.Version}" : "Не установлена";
        StoreButtonText = info.Installed ? "Обновить" : "Установить";
    }

    private bool CanPlay() => HasBedrockLicense && Installed;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        try
        {
            Status = "Проверка лицензии…";
            var account = await _accounts.EnsureActiveValidAsync(CancellationToken.None);
            BedrockService.Launch(SelectedChannel.Edition, account);
            Status = "Игра запускается…";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    /// <summary>Установка и обновление Bedrock — через Microsoft Store, как в официальном лаунчере.</summary>
    [RelayCommand]
    private void OpenStore()
    {
        var uri = SelectedChannel.Edition == BedrockEdition.Release
            ? "ms-windows-store://pdp/?ProductId=9NBLGGH2JHXJ"
            : "ms-windows-store://search/?query=Minecraft%20Preview";
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { Status = ex.Message; }
    }
}
