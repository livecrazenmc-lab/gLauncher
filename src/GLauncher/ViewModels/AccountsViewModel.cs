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
    }

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
    private void Remove()
    {
        if (Selected is null) return;
        if (MessageBox.Show($"Удалить аккаунт {Selected.DisplayName}?", "gLauncher",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _store.Accounts.Remove(Selected);
        _store.Save();
        SyncItems();
        Selected = Items.FirstOrDefault();
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
        var account = Selected ?? throw new LauncherException("Сначала войдите в аккаунт Microsoft на вкладке «Аккаунты».");
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
        ActiveAccountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncItems()
    {
        Items.Clear();
        foreach (var a in _store.Accounts) Items.Add(a);
    }
}

