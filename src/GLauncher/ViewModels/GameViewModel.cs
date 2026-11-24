using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLauncher.Core.Games;

namespace GLauncher.ViewModels;

/// <summary>Страница Minecraft Dungeons / Dungeons II / Legends: найденные установки, запуск, магазин, список изменений.</summary>
public partial class GameViewModel : ObservableObject
{
    /// <summary>Официальный арт для обложки «Играть» (null — пока не скачан, показывается свой куб).</summary>
    [ObservableProperty] private System.Windows.Media.ImageSource? heroArt;

    private readonly AccountsViewModel _accounts;

    public GameViewModel(SpinoffGame game, AccountsViewModel accounts)
    {
        Game = game;
        _accounts = accounts;
        PatchNotes = game.PatchNotes is { } kind ? new PatchNotesViewModel(kind) : null;
        _accounts.ActiveAccountChanged += (_, _) => RefreshLicense();
        RefreshLicense();
    }

    public SpinoffGame Game { get; }
    public string Title => Game.Title;
    public string Description => Game.Description;
    public PatchNotesViewModel? PatchNotes { get; }
    public bool HasPatchNotes => PatchNotes is not null;

    public ObservableCollection<GameInstall> Installs { get; } = [];
    public bool HasInstalls => Installs.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private GameInstall? selectedInstall;

    [ObservableProperty] private string installStatus = "Поиск установленной игры…";
    [ObservableProperty] private string? licenseStatus;
    [ObservableProperty] private string? status;

    public void SetInstalls(IEnumerable<GameInstall> installs)
    {
        Installs.Clear();
        foreach (var i in installs) Installs.Add(i);
        SelectedInstall = Installs.FirstOrDefault();
        OnPropertyChanged(nameof(HasInstalls));
        InstallStatus = Installs.Count switch
        {
            0 => "Игра не установлена. Купите или установите её в Microsoft Store, Xbox или Steam.",
            1 => $"Установлена: {Installs[0].Source}{(Installs[0].Version is { } v ? $", версия {v}" : "")}",
            _ => $"Найдено установок: {Installs.Count} — выберите, какую запускать.",
        };
    }

    private void RefreshLicense()
    {
        var account = _accounts.Selected;
        // Показываем только подтверждённую лицензию: названия прав на спин-оффы Mojang не документирует,
        // а саму проверку покупки при запуске делают Microsoft Store/Xbox или Steam.
        LicenseStatus = account is not null && Game.Entitlements.Any(e => account.Entitlements.Contains(e))
            ? $"Лицензия на аккаунте {account.DisplayName}: есть"
            : null;
    }

    private bool CanPlay() => SelectedInstall is not null;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        try
        {
            Status = $"Запуск {Title}…";
            await SelectedInstall!.LaunchAsync();
            Status = $"{Title} запускается.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenStore()
    {
        try { SpinoffGames.OpenStore(Game); }
        catch (Exception ex) { Status = ex.Message; }
    }
}
