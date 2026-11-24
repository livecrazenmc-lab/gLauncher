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
    }

    /// <summary>
    /// В развёрнутом виде окно без системной рамки выходит за края экрана на толщину рамки —
    /// компенсируем отступом и меняем значок кнопки «развернуть/восстановить».
    /// </summary>
    private void OnStateChanged()
    {
        var maximized = WindowState == WindowState.Maximized;
        var border = SystemParameters.WindowResizeBorderThickness;
        Root.Margin = maximized ? new Thickness(border.Left + 1, border.Top + 1, border.Right + 1, border.Bottom + 1) : new Thickness(0);
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
    }
}

