using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLauncher.Core;
using GLauncher.Core.Auth;

namespace GLauncher.ViewModels;

public partial class AccountsViewModel : ObservableObject
{
    private readonly LauncherSettings _settings;
    private readonly AccountStore _store = AccountStore.Load();
    private CancellationTokenSource? _loginCts;

    public AccountsViewModel(LauncherSettings settings)
    {
        _settings = settings;
        Items = new ObservableCollection<Account>(_store.Accounts);
        selected = Items.FirstOrDefault(a => a.Id == _store.SelectedId) ?? Items.FirstOrDefault();
    }

    public ObservableCollection<Account> Items { get; }

    /// <summary>Активный аккаунт, с которым запускается игра.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(RefreshCommand))]
    private Account? selected;

    /// <summary>Не null, пока идёт вход: код, который нужно ввести на сайте Microsoft.</summary>
    [ObservableProperty]
    private DeviceCodeInfo? deviceCode;

    [ObservableProperty]
    private string? status;

    /// <summary>Вызывается при смене активного аккаунта или его лицензий.</summary>
    public event EventHandler? ActiveAccountChanged;

    partial void OnSelectedChanged(Account? value)
    {
        _store.SelectedId = value?.Id;
        _store.Save();
        ActiveAccountChanged?.Invoke(this, EventArgs.Empty);
        _ = LoadFaceAsync(value);
    }

    /// <summary>Лицо скина активного аккаунта — для карточки игрока в боковой панели.</summary>
    [ObservableProperty] private System.Windows.Media.ImageSource? activeFace;

    /// <summary>Подпись под ником в карточке игрока.</summary>
    public string ActiveSubtitle => Selected switch
    {
        null => "Нажмите, чтобы войти",
        { OwnsJava: true, OwnsBedrock: true } => "Java и Bedrock",
        { OwnsJava: true } => "Java Edition",
        { OwnsBedrock: true } => "Bedrock Edition",
        _ => "Нет лицензии Minecraft",
    };

    private async Task LoadFaceAsync(Account? account)
    {
        OnPropertyChanged(nameof(ActiveSubtitle));
        ActiveFace = null;
        if (account?.Uuid is not { } uuid) return;
        if (GLauncher.Core.Players.PlayerProfileService.LoadCached(uuid) is { SkinPath: { } cached })
            ActiveFace = SkinFace.Load(cached);
        try
        {
            var fresh = await GLauncher.Core.Players.PlayerProfileService.FetchAsync(uuid, CancellationToken.None);
            if (Selected == account && fresh.SkinPath is { } path) ActiveFace = SkinFace.Load(path);
        }
        catch { /* офлайн — остаётся лицо из кэша */ }
    }

    /// <summary>Загрузить лицо при старте лаунчера.</summary>
    public Task LoadInitialFaceAsync() => LoadFaceAsync(Selected);

    private MicrosoftAuthService CreateAuth() => new(AuthConfig.Resolve(_settings));

    [RelayCommand]
    private async Task AddAsync()
    {
        _loginCts = new CancellationTokenSource();
        var ct = _loginCts.Token;
        try
        {
            Status = "Получение кода входа…";
            var auth = CreateAuth();
            var code = await auth.StartDeviceCodeAsync(ct);
            DeviceCode = code;
            Status = "Откройте страницу входа и введите код.";
            CopyCode();
            Shell.Open(code.VerificationUri);

            var account = await auth.WaitForDeviceCodeAsync(code, ct);
            account = _store.AddOrUpdate(account);
            _store.Save();
            SyncItems();
            Selected = account;
            Status = $"Вход выполнен: {account.DisplayName}";
        }
        catch (OperationCanceledException)
        {
            Status = "Вход отменён.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            DeviceCode = null;
            _loginCts = null;
        }
    }

    [RelayCommand]
    private void CancelLogin() => _loginCts?.Cancel();

    [RelayCommand]
    private void CopyCode()
    {
        if (DeviceCode is null) return;
        try { Clipboard.SetText(DeviceCode.UserCode); } catch { /* буфер занят */ }
    }

    [RelayCommand]
    private void OpenLoginPage()
    {
        if (DeviceCode is not null) Shell.Open(DeviceCode.VerificationUri);
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove() => RemoveAccount(Selected);

    /// <summary>Удалить конкретный аккаунт (крестик в списке аккаунтов).</summary>
    [RelayCommand]
    private void RemoveAccount(Account? account)
    {
        if (account is null) return;
        if (MessageBox.Show($"Удалить аккаунт {account.DisplayName}?", "gLauncher",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var wasActive = account == Selected;
        _store.Accounts.Remove(account);
        _store.Save();
        Items.Remove(account);
        if (wasActive) Selected = Items.FirstOrDefault();
    }

    /// <summary>Сделать аккаунт активным (щелчок в выпадающем списке).</summary>
    [RelayCommand]
    private void Activate(Account? account)
    {
        if (account is not null) Selected = account;
    }

    /// <summary>Переставить аккаунт (перетаскивание в списке); порядок сохраняется.</summary>
    public void Move(Account account, int newIndex)
    {
        var oldIndex = _store.Accounts.IndexOf(account);
        if (oldIndex < 0) return;
        newIndex = Math.Clamp(newIndex, 0, _store.Accounts.Count - 1);
        if (newIndex == oldIndex) return;
        _store.Accounts.RemoveAt(oldIndex);
        _store.Accounts.Insert(newIndex, account);
        _store.Save();
        Items.Move(Items.IndexOf(account), newIndex);
    }

    /// <summary>Принудительно обновить токены и перепроверить лицензии.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RefreshAsync()
    {
        if (Selected is null) return;
        try
        {
            Status = "Проверка аккаунта…";
            await CreateAuth().RefreshAsync(Selected, CancellationToken.None);
            Status = $"Аккаунт {Selected.DisplayName} проверен.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            AfterAccountChanged();
        }
    }

    /// <summary>Возвращает активный аккаунт с действующим токеном (при необходимости обновляет его).</summary>
    public async Task<Account> EnsureActiveValidAsync(CancellationToken ct)
    {
        var account = Selected ?? throw new LauncherException("Сначала войдите в аккаунт Microsoft — кнопка «Добавить аккаунт» вверху слева.");
        try
        {
            await CreateAuth().EnsureValidAsync(account, ct);
        }
        finally
        {
            AfterAccountChanged();
        }
        return account;
    }

    /// <summary>Тихая проверка при запуске лаунчера.</summary>
    public async Task InitializeAsync()
    {
        if (Selected is null || string.IsNullOrWhiteSpace(AuthConfig.Resolve(_settings))) return;
        try { await EnsureActiveValidAsync(CancellationToken.None); }
        catch (Exception ex) { Status = ex.Message; }
    }

    private void AfterAccountChanged()
    {
        _store.Save();
        CollectionViewSource.GetDefaultView(Items).Refresh();
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(ActiveSubtitle));
        ActiveAccountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncItems()
    {
        Items.Clear();
        foreach (var a in _store.Accounts) Items.Add(a);
    }
}

