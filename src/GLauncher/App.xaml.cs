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
        var theme = Enum.TryParse<ThemeId>(settings.Theme, out var id) ? id : ThemeId.Auto;
        ThemeManager.Apply(ThemeManager.Resolve(theme));

        base.OnStartup(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "gLauncher — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
