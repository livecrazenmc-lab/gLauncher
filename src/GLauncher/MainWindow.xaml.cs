using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GLauncher.ViewModels;

namespace GLauncher;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        Loaded += async (_, _) => await _vm.InitializeAsync();
    }

    // --- Тёмный заголовок окна в цвет темы (Windows 10 20H1+ / Windows 11) ---

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void ApplyDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var on = 1;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref on, sizeof(int));
        var caption = 0x00201C1C; // COLORREF 0x00BBGGRR → #1C1C20
        DwmSetWindowAttribute(hwnd, DwmCaptionColor, ref caption, sizeof(int)); // только Windows 11, на 10 игнорируется
    }
}
