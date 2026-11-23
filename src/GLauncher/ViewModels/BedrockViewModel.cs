using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLauncher.Core.Bedrock;

namespace GLauncher.ViewModels;

public partial class BedrockViewModel : ObservableObject
{
    private readonly AccountsViewModel _accounts;

    public BedrockViewModel(AccountsViewModel accounts)
    {
        _accounts = accounts;
        _accounts.ActiveAccountChanged += (_, _) => RefreshLicense();
        RefreshLicense();
        RefreshInstall();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayReleaseCommand), nameof(PlayPreviewCommand))]
    private bool hasBedrockLicense;

    [ObservableProperty] private string licenseStatus = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayReleaseCommand))]
    private bool releaseInstalled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPreviewCommand))]
    private bool previewInstalled;

    [ObservableProperty] private string releaseStatus = "";
    [ObservableProperty] private string previewStatus = "";
    [ObservableProperty] private string? status;

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
        var release = BedrockService.GetInstallInfo(BedrockEdition.Release);
        ReleaseInstalled = release.Installed;
        ReleaseStatus = release.Installed ? $"Установлен, версия {release.Version}" : "Не установлен";

        var preview = BedrockService.GetInstallInfo(BedrockEdition.Preview);
        PreviewInstalled = preview.Installed;
        PreviewStatus = preview.Installed ? $"Установлен, версия {preview.Version}" : "Не установлен";
    }

    private bool CanPlayRelease() => HasBedrockLicense && ReleaseInstalled;
    private bool CanPlayPreview() => HasBedrockLicense && PreviewInstalled;

    [RelayCommand(CanExecute = nameof(CanPlayRelease))]
    private Task PlayReleaseAsync() => PlayAsync(BedrockEdition.Release);

    [RelayCommand(CanExecute = nameof(CanPlayPreview))]
    private Task PlayPreviewAsync() => PlayAsync(BedrockEdition.Preview);

    private async Task PlayAsync(BedrockEdition edition)
    {
        try
        {
            Status = "Проверка лицензии…";
            var account = await _accounts.EnsureActiveValidAsync(CancellationToken.None);
            BedrockService.Launch(edition, account);
            Status = "Игра запускается…";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }
}
