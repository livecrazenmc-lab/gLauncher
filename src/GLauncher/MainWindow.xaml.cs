using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GLauncher.Themes;
using GLauncher.ViewModels;

namespace GLauncher;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        SourceInitialized += (_, _) => ApplyWindowStyle();
        Loaded += async (_, _) => await _vm.InitializeAsync();
        Logo.MouseLeftButtonUp += OnLogoClick;

        // Кнопки своего заголовка окна
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        CloseButton.Click += (_, _) => Close();
        StateChanged += (_, _) => OnStateChanged();

        // Плавная смена страниц и вкладок: содержимое проявляется и чуть всплывает
        Root.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(OnTabChanged));
    }

    private static readonly IEasingFunction PageEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is not TabControl || e.AddedItems.Count == 0 || e.AddedItems[0] is not TabItem { Content: FrameworkElement page })
            return;
        if (page.RenderTransform is not TranslateTransform shift)
            page.RenderTransform = shift = new TranslateTransform();
        var duration = TimeSpan.FromMilliseconds(260);
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = PageEase });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, duration) { EasingFunction = PageEase });
    }

    /// <summary>
    /// В развёрнутом виде окно без системной рамки выходит за края экрана на толщину рамки —
    /// компенсируем отступом и меняем значок кнопки «развернуть/восстановить».
    /// </summary>
    private void OnStateChanged()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Восстановить" : "Развернуть";
    }

    // --- Пасхалка для людей из благодарностей: 5 щелчков по логотипу → дождь из блоков ---

    private int _logoClicks;
    private DateTime _lastLogoClick;

    private void OnLogoClick(object sender, MouseButtonEventArgs e)
    {
        if (!_vm.IsCredited) return; // секрет только для своих
        var now = DateTime.UtcNow;
        _logoClicks = now - _lastLogoClick < TimeSpan.FromSeconds(1.5) ? _logoClicks + 1 : 1;
        _lastLogoClick = now;
        if (_logoClicks < 5) return;
        _logoClicks = 0;
        BlockRain();
    }

    private static readonly PixelTextureKind[] RainBlocks =
    [
        PixelTextureKind.GrassBlock, PixelTextureKind.Stone, PixelTextureKind.Planks, PixelTextureKind.Dirt,
        PixelTextureKind.LegendsGrass, PixelTextureKind.CrimsonNylium, PixelTextureKind.EndStoneTop, PixelTextureKind.SnowyGrass,
    ];

    /// <summary>Падающие пиксельные блоки и благодарность по центру окна.</summary>
    public void BlockRain()
    {
        var rnd = new Random();
        var width = Math.Max(ActualWidth, 400);
        var height = Math.Max(ActualHeight, 300);

        for (var i = 0; i < 46; i++)
        {
            var size = rnd.Next(18, 38);
            var block = new Border
            {
                Width = size, Height = size,
                Background = PixelTextures.CreateBrush(RainBlocks[rnd.Next(RainBlocks.Length)], size),
                BorderBrush = Brushes.Black, BorderThickness = new Thickness(1.5),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(rnd.Next(-30, 30)),
            };
            Canvas.SetLeft(block, rnd.NextDouble() * (width - size));
            Canvas.SetTop(block, -size - rnd.Next(0, 300));
            FxLayer.Children.Add(block);

            var duration = TimeSpan.FromSeconds(1.6 + rnd.NextDouble() * 1.8);
            var fall = new DoubleAnimation(Canvas.GetTop(block), height + 40, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            fall.Completed += (_, _) => FxLayer.Children.Remove(block);
            block.BeginAnimation(Canvas.TopProperty, fall);
            ((RotateTransform)block.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(rnd.Next(-200, 200), duration));
        }

        var name = _vm.Accounts.Selected?.DisplayName ?? "друг";
        var thanks = new TextBlock
        {
            Text = $"★ Спасибо, {name}! ★",
            FontSize = 34, FontWeight = FontWeights.Black,
            Foreground = (Brush)FindResource("Brush.TextGold"),
            Effect = (System.Windows.Media.Effects.Effect)FindResource("Effect.TextShadow"),
        };
        thanks.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(thanks, (width - thanks.DesiredSize.Width) / 2);
        Canvas.SetTop(thanks, height / 2 - 40);
        FxLayer.Children.Add(thanks);
        var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3.2) };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.12)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.75)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        fade.Completed += (_, _) => FxLayer.Children.Remove(thanks);
        thanks.BeginAnimation(OpacityProperty, fade);
    }

    // --- Окно: тёмный режим и скруглённые углы Windows 11 (на Windows 10 игнорируется) ---

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void ApplyWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var on = 1;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref on, sizeof(int));
        var round = DwmCornerRound;
        DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    // --- Развёрнутое окно = рабочая область монитора (без панели задач, с любой стороны) ---
    // Окно без системной рамки Windows по умолчанию разворачивает на весь монитор, поверх панели задач.

    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 2;
    private const int AbmGetState = 4, AbmGetTaskbarPos = 5, AbsAutoHide = 1;
    private const int AbeLeft = 0, AbeTop = 1, AbeRight = 2, AbeBottom = 3;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO { public int Size; public RECT Monitor, Work; public int Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA { public int Size; public IntPtr Hwnd; public uint CallbackMessage, Edge; public RECT Rect; public IntPtr LParam; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(int message, ref APPBARDATA data);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo) return IntPtr.Zero;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var work = info.Work;
        var mon = info.Monitor;
        // Автоскрываемая панель задач не уменьшает рабочую область — оставляем 2 пикселя у её края,
        // иначе её нельзя будет вызвать мышью поверх развёрнутого окна
        if (work.Equals(mon) && TaskbarAutoHideEdge() is { } edge)
        {
            switch (edge)
            {
                case AbeLeft: work.Left += 2; break;
                case AbeTop: work.Top += 2; break;
                case AbeRight: work.Right -= 2; break;
                case AbeBottom: work.Bottom -= 2; break;
            }
        }

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.MaxPosition = new POINT { X = work.Left - mon.Left, Y = work.Top - mon.Top };
        mmi.MaxSize = new POINT { X = work.Right - work.Left, Y = work.Bottom - work.Top };
        mmi.MaxTrackSize = new POINT { X = Math.Max(mmi.MaxTrackSize.X, mmi.MaxSize.X), Y = Math.Max(mmi.MaxTrackSize.Y, mmi.MaxSize.Y) };
        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    private static int? TaskbarAutoHideEdge()
    {
        var data = new APPBARDATA { Size = Marshal.SizeOf<APPBARDATA>() };
        if (((long)SHAppBarMessage(AbmGetState, ref data) & AbsAutoHide) == 0) return null;
        return SHAppBarMessage(AbmGetTaskbarPos, ref data) != IntPtr.Zero ? (int)data.Edge : AbeBottom;
    }
}

