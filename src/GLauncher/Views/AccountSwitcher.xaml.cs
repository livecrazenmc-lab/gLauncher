using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GLauncher.Core.Auth;
using GLauncher.ViewModels;

namespace GLauncher.Views;

/// <summary>
/// Карточка аккаунта в боковой панели: выпадающий список аккаунтов для быстрой смены,
/// перетаскивание для порядка, вход и проверка лицензий. Без аккаунта — кнопка «Добавить аккаунт».
/// </summary>
public partial class AccountSwitcher : UserControl
{
    private AccountsViewModel? _accounts;
    private DateTime _closedAt;
    private Point _downPoint;
    private Account? _downAccount;

    public AccountSwitcher()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach((DataContext as MainViewModel)?.Accounts);
    }

    private void Attach(AccountsViewModel? accounts)
    {
        if (_accounts is not null) _accounts.PropertyChanged -= OnAccountsChanged;
        _accounts = accounts;
        if (_accounts is not null) _accounts.PropertyChanged += OnAccountsChanged;
    }

    /// <summary>
    /// Начался вход — список закрывается: код показывается карточкой внутри окна лаунчера.
    /// (Popup в WPF всегда поверх всех окон и закрывал бы браузер, где вводят код.)
    /// </summary>
    private void OnAccountsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AccountsViewModel.DeviceCode)) return;
        if (_accounts?.DeviceCode is not null) Panel.IsOpen = false;
    }

    private void OnCardClick(object sender, RoutedEventArgs e)
    {
        // Щелчок по карточке при открытом списке сначала закрывает его — не открываем заново
        if (DateTime.UtcNow - _closedAt < TimeSpan.FromMilliseconds(250)) return;
        Panel.IsOpen = !Panel.IsOpen;
    }

    private void OnPanelOpened(object? sender, EventArgs e) => RotateChevron(180);

    private void OnPanelClosed(object? sender, EventArgs e)
    {
        _closedAt = DateTime.UtcNow;
        RotateChevron(0);
    }

    private void RotateChevron(double angle) =>
        ((RotateTransform)Chevron.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(angle, TimeSpan.FromMilliseconds(160)));

    // --- Выбор щелчком и перетаскивание ---

    private static Account? AccountAt(object source)
    {
        for (var d = source as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase) return null; // крестик «удалить» — не строка
            if (d is FrameworkElement { DataContext: Account a, Name: "Row" }) return a;
        }
        return null;
    }

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _downAccount = AccountAt(e.OriginalSource);
        _downPoint = e.GetPosition(List);
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (_downAccount is null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(List) - _downPoint;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance &&
            Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance) return;

        var dragged = _downAccount;
        _downAccount = null;
        DragDrop.DoDragDrop(List, new DataObject(typeof(Account), dragged), DragDropEffects.Move);
    }

    private void OnListMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Отпустили без перетаскивания на той же строке — аккаунт становится активным
        if (_downAccount is { } account && AccountAt(e.OriginalSource) == account)
        {
            _accounts?.ActivateCommand.Execute(account);
            Panel.IsOpen = false;
        }
        _downAccount = null;
    }

    /// <summary>Строки меняются местами прямо во время перетаскивания.</summary>
    private void OnListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(typeof(Account)) is not Account dragged || _accounts is null) return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        if (AccountAt(e.OriginalSource) is { } target && target != dragged)
            _accounts.Move(dragged, _accounts.Items.IndexOf(target));
    }

    private void OnListDrop(object sender, DragEventArgs e) => e.Handled = true;
}
