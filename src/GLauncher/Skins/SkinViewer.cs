using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace GLauncher.Skins;

/// <summary>
/// 3D-превью скина. Тянуть левой кнопкой мыши — вращать, двойной щелчок — вернуть исходный ракурс.
/// В XAML: &lt;skins:SkinViewer SkinPath="{Binding ...}" Slim="{Binding ...}" CapePng="{Binding ...}" /&gt;
/// </summary>
public sealed class SkinViewer : Border
{
    public static readonly DependencyProperty SkinPathProperty = DependencyProperty.Register(
        nameof(SkinPath), typeof(string), typeof(SkinViewer), new PropertyMetadata(null, (d, _) => ((SkinViewer)d).Rebuild()));

    public static readonly DependencyProperty SlimProperty = DependencyProperty.Register(
        nameof(Slim), typeof(bool), typeof(SkinViewer), new PropertyMetadata(false, (d, _) => ((SkinViewer)d).Rebuild()));

    public static readonly DependencyProperty CapePngProperty = DependencyProperty.Register(
        nameof(CapePng), typeof(byte[]), typeof(SkinViewer), new PropertyMetadata(null, (d, _) => ((SkinViewer)d).Rebuild()));

    public string? SkinPath { get => (string?)GetValue(SkinPathProperty); set => SetValue(SkinPathProperty, value); }
    public bool Slim { get => (bool)GetValue(SlimProperty); set => SetValue(SlimProperty, value); }
    public byte[]? CapePng { get => (byte[]?)GetValue(CapePngProperty); set => SetValue(CapePngProperty, value); }

    private const double DefaultYaw = -28, DefaultPitch = 8, CameraDistance = 70;
    private readonly ModelVisual3D _model = new();
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), DefaultYaw);
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), DefaultPitch);
    private readonly TextBlock _placeholder = new()
    {
        Text = "Выберите скин",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = 0.6,
    };
    private Point? _drag;

    public SkinViewer()
    {
        var camera = new PerspectiveCamera(new Point3D(0, 16, CameraDistance), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 30);
        var viewport = new Viewport3D { Camera = camera };
        // FieldOfView в WPF — горизонтальный угол; подгоняем его так, чтобы фигурка (с плащом и запасом ~42 единицы)
        // всегда помещалась по высоте при любой ширине превью.
        SizeChanged += (_, _) =>
        {
            if (ActualHeight <= 0 || ActualWidth <= 0) return;
            var horizontalSpan = 42 * ActualWidth / ActualHeight;
            camera.FieldOfView = 2 * Math.Atan(horizontalSpan / 2 / CameraDistance) * 180 / Math.PI;
        };

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(0xB8, 0xB8, 0xB8)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x58, 0x58, 0x58), new Vector3D(-0.4, -0.5, -1)));
        viewport.Children.Add(new ModelVisual3D { Content = lights });

        var rotation = new Transform3DGroup();
        rotation.Children.Add(new RotateTransform3D(_yaw, 0, 16, 0));
        rotation.Children.Add(new RotateTransform3D(_pitch, 0, 16, 0));
        _model.Transform = rotation;
        viewport.Children.Add(_model);

        // Курсор «Переместить» (крестик со стрелками) — подсказывает, что фигурку можно вращать.
        var grid = new Grid { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        grid.Children.Add(viewport);
        grid.Children.Add(_placeholder);
        Child = grid;

        grid.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { _yaw.Angle = DefaultYaw; _pitch.Angle = DefaultPitch; return; }
            _drag = e.GetPosition(this);
            grid.CaptureMouse();
        };
        grid.MouseLeftButtonUp += (_, _) => { _drag = null; grid.ReleaseMouseCapture(); };
        grid.MouseMove += (_, e) =>
        {
            if (_drag is not { } start) return;
            var p = e.GetPosition(this);
            _yaw.Angle += (p.X - start.X) * 0.7;
            _pitch.Angle = Math.Clamp(_pitch.Angle + (p.Y - start.Y) * 0.4, -35, 35);
            _drag = p;
        };
    }

    private void Rebuild()
    {
        _model.Content = null;
        _placeholder.Visibility = Visibility.Visible;
        if (string.IsNullOrEmpty(SkinPath) || !System.IO.File.Exists(SkinPath)) return;
        try
        {
            _model.Content = PlayerModel.Build(SkinImage.FromFile(SkinPath), Slim, CapePng);
            _placeholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            _placeholder.Text = "Не удалось прочитать скин";
        }
    }
}
