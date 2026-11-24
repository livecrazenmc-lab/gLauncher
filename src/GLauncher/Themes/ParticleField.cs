using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GLauncher.Themes;

/// <summary>
/// Анимированные частицы на фоне окна, свои у каждой темы: снег, листья, лепестки, светлячки, угольки, искры, огоньки.
/// Лёгкая анимация (до ~45 фигур), не ловит мышь, останавливается, когда окно свёрнуто или частицы выключены.
/// </summary>
public sealed class ParticleField : Canvas
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(ParticleField), new PropertyMetadata(true, (d, _) => ((ParticleField)d).Restart()));

    /// <summary>Выключатель (настройка «Анимированный фон»).</summary>
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    private sealed class Particle
    {
        public required Shape Shape;
        public double X, Y, Vx, Vy, Phase, Spin, Life, MaxLife, BaseOpacity;
        public RotateTransform? Rotate;
    }

    private readonly List<Particle> _particles = [];
    private readonly Random _rnd = new();
    private ThemeDefinition? _theme;
    private TimeSpan _last;
    private bool _running;

    public ParticleField()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Loaded += (_, _) => { ThemeManager.Changed += OnThemeChanged; Restart(); };
        Unloaded += (_, _) => { ThemeManager.Changed -= OnThemeChanged; Stop(); };
        SizeChanged += (_, _) => { if (_particles.Count == 0) Restart(); };
        IsVisibleChanged += (_, _) => Restart();
    }

    private void OnThemeChanged() => Dispatcher.BeginInvoke(Restart);

    private void Restart()
    {
        Stop();
        Children.Clear();
        _particles.Clear();
        _theme = ThemeManager.Current;
        if (!IsActive || !IsVisible || _theme is null || _theme.Particles == ParticleKind.None || ActualWidth < 10) return;

        var count = _theme.Particles switch
        {
            ParticleKind.Fireflies or ParticleKind.Sparkles => 26,
            ParticleKind.Motes => 30,
            _ => 42,
        };
        for (var i = 0; i < count; i++) _particles.Add(Spawn(initial: true));
        _running = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        if (!_running) return;
        CompositionTarget.Rendering -= OnFrame;
        _running = false;
    }

    private Particle Spawn(bool initial)
    {
        var t = _theme!;
        var color = t.ParticleColors[_rnd.Next(t.ParticleColors.Length)];
        double w = ActualWidth, h = ActualHeight;
        Shape shape;
        var p = new Particle { Shape = null!, Phase = _rnd.NextDouble() * Math.Tau };

        switch (t.Particles)
        {
            case ParticleKind.Snow:
            {
                var r = 1.5 + _rnd.NextDouble() * 3;
                shape = new Ellipse { Width = r * 2, Height = r * 2, Fill = new SolidColorBrush(color) };
                p.X = _rnd.NextDouble() * w; p.Y = initial ? _rnd.NextDouble() * h : -10;
                p.Vx = 0; p.Vy = 18 + r * 9;
                p.BaseOpacity = 0.35 + _rnd.NextDouble() * 0.45;
                break;
            }
            case ParticleKind.Leaves or ParticleKind.Petals:
            {
                var leaf = t.Particles == ParticleKind.Leaves;
                var size = leaf ? 7 + _rnd.NextDouble() * 6 : 5 + _rnd.NextDouble() * 4;
                shape = new Path
                {
                    Data = leaf
                        ? Geometry.Parse("M 0,5 C 2,0 8,0 10,5 C 8,10 2,10 0,5 Z")
                        : Geometry.Parse("M 5,0 C 9,2 9,8 5,10 C 1,8 1,2 5,0 Z"),
                    Fill = new SolidColorBrush(color),
                    Width = size, Height = size, Stretch = Stretch.Fill,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                };
                p.Rotate = new RotateTransform(_rnd.Next(360));
                shape.RenderTransform = p.Rotate;
                p.X = _rnd.NextDouble() * w; p.Y = initial ? _rnd.NextDouble() * h : -14;
                p.Vx = 8 + _rnd.NextDouble() * 14; p.Vy = 22 + _rnd.NextDouble() * 26;
                p.Spin = (_rnd.NextDouble() - 0.5) * 120;
                p.BaseOpacity = 0.45 + _rnd.NextDouble() * 0.35;
                break;
            }
            case ParticleKind.Fireflies:
            {
                var r = 2 + _rnd.NextDouble() * 2;
                shape = new Ellipse
                {
                    Width = r * 2, Height = r * 2, Fill = new SolidColorBrush(color),
                    Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 3 },
                };
                p.X = _rnd.NextDouble() * w; p.Y = h * 0.35 + _rnd.NextDouble() * h * 0.65;
                p.Vx = (_rnd.NextDouble() - 0.5) * 16; p.Vy = (_rnd.NextDouble() - 0.5) * 10;
                p.BaseOpacity = 0.8;
                break;
            }
            case ParticleKind.Embers or ParticleKind.Motes:
            {
                var ember = t.Particles == ParticleKind.Embers;
                var r = ember ? 1.4 + _rnd.NextDouble() * 2 : 1.5 + _rnd.NextDouble() * 2.5;
                shape = new Ellipse { Width = r * 2, Height = r * 2, Fill = new SolidColorBrush(color) };
                p.X = _rnd.NextDouble() * w; p.Y = initial ? _rnd.NextDouble() * h : h + 6;
                p.Vx = (_rnd.NextDouble() - 0.5) * 10; p.Vy = -(ember ? 22 + _rnd.NextDouble() * 30 : 8 + _rnd.NextDouble() * 12);
                p.BaseOpacity = ember ? 0.75 : 0.5;
                break;
            }
            default: // Sparkles — вспыхивающие на месте четырёхлучевые звёздочки
            {
                var s = 6 + _rnd.NextDouble() * 7;
                shape = new Path
                {
                    Data = Geometry.Parse("M 5,0 L 6,4 L 10,5 L 6,6 L 5,10 L 4,6 L 0,5 L 4,4 Z"),
                    Fill = new SolidColorBrush(color), Width = s, Height = s, Stretch = Stretch.Fill,
                };
                p.X = _rnd.NextDouble() * w; p.Y = _rnd.NextDouble() * h;
                p.BaseOpacity = 0.9;
                break;
            }
        }

        p.MaxLife = t.Particles is ParticleKind.Sparkles or ParticleKind.Fireflies ? 3 + _rnd.NextDouble() * 4 : 0;
        p.Life = initial && p.MaxLife > 0 ? _rnd.NextDouble() * p.MaxLife : 0;
        p.Shape = shape;
        shape.Opacity = 0;
        Children.Add(shape);
        Place(p);
        return p;
    }

    private static void Place(Particle p)
    {
        SetLeft(p.Shape, p.X);
        SetTop(p.Shape, p.Y);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_last == TimeSpan.Zero) { _last = now; return; }
        var dt = Math.Min((now - _last).TotalSeconds, 0.05);
        _last = now;
        if (dt <= 0 || _theme is null) return;

        double w = ActualWidth, h = ActualHeight;
        for (var i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            p.Phase += dt;
            var dead = false;

            switch (_theme.Particles)
            {
                case ParticleKind.Snow:
                    p.X += Math.Sin(p.Phase * 0.9) * 14 * dt; p.Y += p.Vy * dt;
                    dead = p.Y > h + 10;
                    p.Shape.Opacity = p.BaseOpacity;
                    break;
                case ParticleKind.Leaves or ParticleKind.Petals:
                    p.X += (p.Vx * 0.4 + Math.Sin(p.Phase * 1.3) * 26) * dt; p.Y += p.Vy * dt;
                    p.Rotate!.Angle += p.Spin * dt;
                    dead = p.Y > h + 14 || p.X > w + 20;
                    p.Shape.Opacity = p.BaseOpacity;
                    break;
                case ParticleKind.Fireflies:
                    p.X += (p.Vx + Math.Sin(p.Phase * 0.7) * 10) * dt; p.Y += (p.Vy + Math.Cos(p.Phase * 0.9) * 8) * dt;
                    p.Life += dt;
                    p.Shape.Opacity = p.BaseOpacity * Math.Max(0, Math.Sin(Math.PI * p.Life / p.MaxLife));
                    dead = p.Life >= p.MaxLife;
                    break;
                case ParticleKind.Embers or ParticleKind.Motes:
                    p.X += (p.Vx + Math.Sin(p.Phase * 1.7) * 6) * dt; p.Y += p.Vy * dt;
                    var fade = Math.Clamp(p.Y / Math.Max(h, 1), 0, 1);
                    p.Shape.Opacity = p.BaseOpacity * (0.25 + 0.75 * fade);
                    dead = p.Y < -8;
                    break;
                default:
                    p.Life += dt;
                    p.Shape.Opacity = p.BaseOpacity * Math.Max(0, Math.Sin(Math.PI * p.Life / p.MaxLife));
                    dead = p.Life >= p.MaxLife;
                    break;
            }

            if (dead)
            {
                Children.Remove(p.Shape);
                _particles[i] = Spawn(initial: false);
            }
            else
            {
                Place(p);
            }
        }
    }
}

