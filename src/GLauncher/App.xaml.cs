using System.Windows;
using System.Windows.Threading;
using GLauncher.Core;
using GLauncher.Themes;

namespace GLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledException;
        GLauncher.Core.Updates.UpdateService.CleanupAfterUpdate();

        // Тема должна быть применена до создания главного окна.
        var settings = LauncherSettings.Load();
        var theme = ThemeManager.FromSetting(settings.Theme);
        var accounts = GLauncher.Core.Auth.AccountStore.Load();
        ThemeManager.BirthdayToday = accounts.Accounts.FirstOrDefault(a => a.Id == accounts.SelectedId)?.IsBirthday(DateTime.Today) == true;
        if (ThemeManager.IsExclusive(theme))
        {
            // Эксклюзивная тема — только если активный аккаунт есть в благодарностях.
            var store = GLauncher.Core.Auth.AccountStore.Load();
            var uuid = store.Accounts.FirstOrDefault(a => a.Id == store.SelectedId)?.Uuid;
            if (GLauncher.Core.Players.CreditsService.Find(uuid) is null) theme = ThemeId.Auto;
        }
        ThemeManager.Apply(ThemeManager.Resolve(theme));
        FontManager.Apply(settings.UiFont);

        base.OnStartup(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "gLauncher — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
