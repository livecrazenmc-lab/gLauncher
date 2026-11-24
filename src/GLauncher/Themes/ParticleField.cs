using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GLauncher.Themes;

/// <summary>
/// Анимированные частицы на фоне окна, свои у каждой темы:
/// зима — снежинки разной формы и размера, осень — жёлтые/оранжевые/красные листья, весна — розовые лепестки,
/// лето — пух одуванчика и светлячки, Хэллоуин — угольки и летучие мыши, Новый год — снег и золотые искры.
/// Плавность: кадр считается по реальному времени (Stopwatch), фигуры переиспользуются (пул),
/// геометрии и кисти заморожены, каждая фигура кэшируется в текстуру (BitmapCache) и только двигается.
/// </summary>
public sealed class ParticleField : Canvas
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(ParticleField), new PropertyMetadata(true, (d, _) => ((ParticleField)d).Restart()));

    /// <summary>Выключатель (настройка «Анимированный фон»).</summary>
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    private enum Sprite { Snowflake, SnowDot, Leaf, Petal, Seed, Firefly, Ember, Mote, Sparkle, Bat }

    private sealed class Particle
    {
        public readonly Path Shape = new() { Stretch = Stretch.Fill, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0 };
        public readonly ScaleTransform Scale = new();
        public readonly RotateTransform Rotate = new();
        public readonly TranslateTransform Move = new();
        public Sprite Kind;
        public double X, Y, Vx, Vy, Phase, Spin, Angle, Sway, SwaySpeed, Flutter, Age, Life, BaseOpacity, Size;

        public Particle()
        {
            Shape.RenderTransform = new TransformGroup { Children = { Scale, Rotate, Move } };
            Shape.CacheMode = new BitmapCache(2);
        }
    }

    private readonly List<Particle> _particles = [];
    private readonly Random _rnd = new();
    private readonly Stopwatch _clock = new();
    private ThemeDefinition? _theme;
    private double _lastSeconds;
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
            ParticleKind.Sparkles => 28,
            ParticleKind.Motes or ParticleKind.Fireflies => 32,
            ParticleKind.Snow => 70,
            _ => 46,
        };
        for (var i = 0; i < count; i++)
        {
            var p = new Particle();
            Children.Add(p.Shape);
            _particles.Add(p);
            Spawn(p, initial: true);
        }
        _running = true;
        _clock.Restart();
        _lastSeconds = 0;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        if (!_running) return;
        CompositionTarget.Rendering -= OnFrame;
        _clock.Stop();
        _running = false;
    }

    // ------------------------------------------------------------------ Формы

    private static Geometry Frozen(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    private static readonly Geometry[] LeafShapes =
    [
        // клён
        Frozen("M10,0 L12,5 L17,3 L15,8 L20,9 L15,12 L17,16 L11,14 L10.6,20 L9.4,20 L9,14 L3,16 L5,12 L0,9 L5,8 L3,3 L8,5 Z"),
        // дуб
        Frozen("M10,0 C13,1 12,4 14,4 C17,4 16,8 14,9 C17,10 17,13 14,13 C15,16 12,17 10.6,20 L9.4,20 C8,17 5,16 6,13 C3,13 3,10 6,9 C4,8 3,4 6,4 C8,4 7,1 10,0 Z"),
        // берёза / липа — простой овальный лист с черешком
        Frozen("M10,0 C16,4 17,12 10.6,17 L10.6,20 L9.4,20 L9.4,17 C3,12 4,4 10,0 Z"),
        // вытянутый (ива)
        Frozen("M10,0 C13,5 13,13 10.5,18 L10.5,20 L9.5,20 L9.5,18 C7,13 7,5 10,0 Z"),
    ];

    private static readonly Geometry[] PetalShapes =
    [
        Frozen("M10,20 C2,14 1,5 6.5,1 L10,4.5 L13.5,1 C19,5 18,14 10,20 Z"), // сакура с вырезом
        Frozen("M10,0 C16,5 16,15 10,20 C4,15 4,5 10,0 Z"),                     // овальный
        Frozen("M10,1 C17,1 19,11 10,19 C1,11 3,1 10,1 Z"),                      // округлый
        Frozen("M10,0 C18,3 18,12 12,20 L8,20 C2,12 2,3 10,0 Z"),                // широкий
    ];

    private static readonly Geometry[] SnowflakeShapes = [Snowflake(0), Snowflake(1), Snowflake(2)];

    /// <summary>Шестилучевая снежинка: 0 — простые лучи, 1 — с веточками, 2 — с двумя ярусами веточек.</summary>
    private static Geometry Snowflake(int detail)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            const double c = 10, r = 9.5;
            for (var k = 0; k < 6; k++)
            {
                var a = k * Math.PI / 3;
                Point At(double dist, double turn) => new(c + Math.Cos(a + turn) * dist, c + Math.Sin(a + turn) * dist);
                void Line(Point from, Point to) { ctx.BeginFigure(from, false, false); ctx.LineTo(to, true, false); }

                Line(new Point(c, c), At(r, 0));
                if (detail >= 1)
                {
                    var b = At(r * 0.58, 0);
                    Line(b, new Point(b.X + Math.Cos(a + 0.75) * 3.2, b.Y + Math.Sin(a + 0.75) * 3.2));
                    Line(b, new Point(b.X + Math.Cos(a - 0.75) * 3.2, b.Y + Math.Sin(a - 0.75) * 3.2));
                }
                if (detail >= 2)
                {
                    var b = At(r * 0.3, 0);
                    Line(b, new Point(b.X + Math.Cos(a + 0.9) * 2.2, b.Y + Math.Sin(a + 0.9) * 2.2));
                    Line(b, new Point(b.X + Math.Cos(a - 0.9) * 2.2, b.Y + Math.Sin(a - 0.9) * 2.2));
                    var t = At(r * 0.82, 0);
                    Line(t, new Point(t.X + Math.Cos(a + 0.6) * 1.8, t.Y + Math.Sin(a + 0.6) * 1.8));
                    Line(t, new Point(t.X + Math.Cos(a - 0.6) * 1.8, t.Y + Math.Sin(a - 0.6) * 1.8));
                }
            }
        }
        g.Freeze();
        return g;
    }

    /// <summary>Семечко одуванчика: стебелёк и веер пушинок.</summary>
    private static readonly Geometry SeedShape = BuildSeed();

    private static Geometry BuildSeed()
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            var top = new Point(10, 8);
            ctx.BeginFigure(new Point(10, 20), false, false);
            ctx.LineTo(top, true, false);
            for (var k = 0; k < 9; k++)
            {
                var a = -Math.PI + 0.25 + k * (Math.PI - 0.5) / 8;
                ctx.BeginFigure(top, false, false);
                ctx.LineTo(new Point(10 + Math.Cos(a) * 8, 8 + Math.Sin(a) * 8), true, false);
            }
        }
        g.Freeze();
        return g;
    }

    private static readonly Geometry DotShape = Frozen("M10,0 A10,10 0 1 1 10,20 A10,10 0 1 1 10,0 Z");
    private static readonly Geometry SparkleShape = Frozen("M10,0 L12,8 L20,10 L12,12 L10,20 L8,12 L0,10 L8,8 Z");
    private static readonly Geometry BatShape = Frozen(
        "M10,9 C9.5,7.5 8.5,7.3 7.6,8 C6,5.5 3,5.2 0,7 C1.8,8 2.6,10 2.8,12.5 C4.6,10.8 6.6,11.2 8,12.6 C8.8,11.6 9.4,11.4 10,11.4 " +
        "C10.6,11.4 11.2,11.6 12,12.6 C13.4,11.2 15.4,10.8 17.2,12.5 C17.4,10 18.2,8 20,7 C17,5.2 14,5.5 12.4,8 C11.5,7.3 10.5,7.5 10,9 Z");

    private static readonly Color[] SnowColors =
        [Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xF0, 0xF9, 0xFF), Color.FromRgb(0xE0, 0xF2, 0xFE), Color.FromRgb(0xBA, 0xE6, 0xFD)];

    private readonly Dictionary<Color, SolidColorBrush> _brushes = [];

    private SolidColorBrush BrushOf(Color color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _brushes[color] = brush;
        }
        return brush;
    }

    private T Pick<T>(IReadOnlyList<T> items) => items[_rnd.Next(items.Count)];
    private double Range(double min, double max) => min + _rnd.NextDouble() * (max - min);

    // ------------------------------------------------------------------ Появление

    private Sprite ChooseSprite()
    {
        var t = _theme!;
        var roll = _rnd.NextDouble();
        return t.Particles switch
        {
            ParticleKind.Snow when t.Id == ThemeId.NewYear => roll < 0.3 ? Sprite.Sparkle : roll < 0.6 ? Sprite.SnowDot : Sprite.Snowflake,
            ParticleKind.Snow => roll < 0.45 ? Sprite.SnowDot : Sprite.Snowflake,
            ParticleKind.Leaves => Sprite.Leaf,
            ParticleKind.Petals => Sprite.Petal,
            ParticleKind.Dandelions => roll < 0.7 ? Sprite.Seed : Sprite.Firefly,
            ParticleKind.Fireflies => Sprite.Firefly,
            ParticleKind.Embers when t.Id == ThemeId.Halloween => roll < 0.12 ? Sprite.Bat : Sprite.Ember,
            ParticleKind.Embers => Sprite.Ember,
            ParticleKind.Motes => Sprite.Mote,
            _ => Sprite.Sparkle,
        };
    }

    /// <summary>Цвета темы без белого — для искр и светлячков, где белый смотрится блёкло.</summary>
    private Color ThemeColor(bool allowWhite)
    {
        var colors = _theme!.ParticleColors;
        var pool = allowWhite ? colors : colors.Where(c => !(c.R > 0xF0 && c.G > 0xF0 && c.B > 0xF0)).ToArray();
        return Pick(pool.Length > 0 ? pool : colors);
    }

    private void Spawn(Particle p, bool initial)
    {
        double w = ActualWidth, h = ActualHeight;
        var s = p.Shape;
        p.Kind = ChooseSprite();
        p.Phase = _rnd.NextDouble() * Math.Tau;
        p.Age = 0;
        p.Life = 0;
        p.Spin = 0;
        p.Angle = _rnd.Next(360);
        p.Sway = 0;
        p.SwaySpeed = Range(0.6, 1.4);
        p.Flutter = 0;
        p.Scale.ScaleX = p.Scale.ScaleY = 1;
        s.Fill = null;
        s.Stroke = null;
        s.StrokeThickness = 0;
        s.Effect = null;

        switch (p.Kind)
        {
            case Sprite.Snowflake:
            {
                p.Size = Range(9, 22);
                s.Data = Pick(SnowflakeShapes);
                s.Stroke = BrushOf(Pick(SnowColors));
                s.StrokeThickness = p.Size > 14 ? 1.3 : 1;
                s.StrokeStartLineCap = s.StrokeEndLineCap = PenLineCap.Round;
                p.Vy = 16 + p.Size * 2.2;
                p.Sway = Range(10, 22);
                p.Spin = Range(-40, 40);
                p.BaseOpacity = Range(0.55, 0.95);
                SpawnFromTop(p, w, h, initial);
                break;
            }
            case Sprite.SnowDot:
            {
                p.Size = Range(2, 6);
                s.Data = DotShape;
                s.Fill = BrushOf(Pick(SnowColors));
                p.Vy = 14 + p.Size * 6;
                p.Sway = Range(6, 16);
                p.BaseOpacity = Range(0.35, 0.8);
                SpawnFromTop(p, w, h, initial);
                break;
            }
            case Sprite.Leaf:
            {
                p.Size = Range(11, 26);
                s.Data = Pick(LeafShapes);
                s.Fill = BrushOf(ThemeColor(allowWhite: false));
                p.Vy = Range(26, 48);
                p.Vx = Range(8, 26);
                p.Sway = Range(22, 42);
                p.Spin = Range(-90, 90);
                p.Flutter = Range(1.2, 3.2);
                p.BaseOpacity = Range(0.7, 0.95);
                SpawnFromTop(p, w, h, initial);
                break;
            }
            case Sprite.Petal:
            {
                p.Size = Range(8, 18);
                s.Data = Pick(PetalShapes);
                s.Fill = BrushOf(ThemeColor(allowWhite: true));
                p.Vy = Range(16, 34);
                p.Vx = Range(10, 28);
                p.Sway = Range(16, 34);
                p.Spin = Range(60, 200) * (_rnd.Next(2) == 0 ? -1 : 1);
                p.Flutter = Range(2, 4.5);
                p.BaseOpacity = Range(0.65, 0.95);
                SpawnFromTop(p, w, h, initial);
                break;
            }
            case Sprite.Seed:
            {
                p.Size = Range(12, 22);
                s.Data = SeedShape;
                s.Stroke = BrushOf(Pick(_theme!.ParticleColors.Take(2).ToArray()));
                s.StrokeThickness = 0.9;
                s.StrokeStartLineCap = s.StrokeEndLineCap = PenLineCap.Round;
                p.Vx = Range(10, 24);
                p.Vy = -Range(4, 12);
                p.Sway = Range(8, 16);
                p.Angle = Range(-20, 20);
                p.BaseOpacity = Range(0.55, 0.85);
                // летят снизу-слева вверх-вправо
                if (initial) { p.X = Range(0, w); p.Y = Range(0, h); }
                else if (_rnd.Next(2) == 0) { p.X = -20; p.Y = Range(h * 0.2, h); }
                else { p.X = Range(0, w * 0.7); p.Y = h + 20; }
                break;
            }
            case Sprite.Firefly:
            {
                p.Size = Range(4, 8);
                s.Data = DotShape;
                s.Fill = BrushOf(ThemeColor(allowWhite: false));
                s.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 3 };
                p.Vx = Range(-8, 8); p.Vy = Range(-5, 5);
                p.Life = Range(3, 7);
                p.Age = initial ? Range(0, p.Life) : 0;
                p.BaseOpacity = 0.85;
                p.X = Range(0, w); p.Y = Range(h * 0.3, h);
                break;
            }
            case Sprite.Ember or Sprite.Mote:
            {
                var ember = p.Kind == Sprite.Ember;
                p.Size = ember ? Range(2.5, 6) : Range(3, 7);
                s.Data = DotShape;
                s.Fill = BrushOf(ThemeColor(allowWhite: true));
                p.Vx = Range(-5, 5);
                p.Vy = -(ember ? Range(22, 52) : Range(8, 20));
                p.Sway = ember ? 6 : 4;
                p.BaseOpacity = ember ? 0.8 : 0.5;
                p.X = Range(0, w); p.Y = initial ? Range(0, h) : h + 8;
                break;
            }
            case Sprite.Bat:
            {
                p.Size = Range(18, 30);
                s.Data = BatShape;
                s.Fill = BrushOf(Color.FromRgb(0x1A, 0x10, 0x24));
                var right = _rnd.Next(2) == 0;
                p.Vx = (right ? 1 : -1) * Range(70, 120);
                p.Sway = Range(10, 24);
                p.Flutter = Range(12, 18);
                p.Angle = 0;
                p.BaseOpacity = 0.85;
                p.X = right ? -40 : w + 40;
                p.Y = Range(h * 0.08, h * 0.6);
                p.Age = -Range(0, 8); // мыши пролетают изредка
                break;
            }
            default: // Sparkle — вспыхивающие на месте звёздочки
            {
                p.Size = Range(6, 14);
                s.Data = SparkleShape;
                s.Fill = BrushOf(ThemeColor(allowWhite: true));
                p.Life = Range(2.5, 6);
                p.Age = initial ? Range(0, p.Life) : 0;
                p.Spin = Range(-30, 30);
                p.BaseOpacity = 0.9;
                p.X = Range(0, w); p.Y = Range(0, h);
                break;
            }
        }

        s.Width = p.Kind == Sprite.Bat ? p.Size * 1.6 : p.Size;
        s.Height = p.Size;
        s.Opacity = 0;
        Apply(p);
    }

    private void SpawnFromTop(Particle p, double w, double h, bool initial)
    {
        // С учётом сноса вправо начинаем чуть левее экрана
        p.X = Range(-w * 0.15, w);
        p.Y = initial ? Range(-20, h) : -p.Size - Range(0, 40);
        if (initial) p.Age = 5; // уже «давно летят» — без проявления
    }

    private static void Apply(Particle p)
    {
        p.Move.X = p.X;
        p.Move.Y = p.Y;
        p.Rotate.Angle = p.Angle;
    }

    // ------------------------------------------------------------------ Кадр

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - _lastSeconds, 0, 0.05); // после паузы не «прыгаем»
        _lastSeconds = now;
        if (dt <= 0 || _theme is null) return;

        double w = ActualWidth, h = ActualHeight;
        var wind = 6 + Math.Sin(now * 0.15) * 6; // медленно меняющийся ветер

        foreach (var p in _particles)
        {
            p.Phase += dt;
            p.Age += dt;
            var dead = false;
            var opacity = p.BaseOpacity;

            switch (p.Kind)
            {
                case Sprite.Snowflake or Sprite.SnowDot:
                    p.X += (wind * 0.6 + Math.Sin(p.Phase * p.SwaySpeed) * p.Sway) * dt;
                    p.Y += p.Vy * dt;
                    p.Angle += p.Spin * dt;
                    dead = p.Y > h + 24 || p.X > w + 24;
                    break;

                case Sprite.Leaf:
                    p.X += (p.Vx + wind + Math.Sin(p.Phase * p.SwaySpeed * 1.3) * p.Sway) * dt;
                    // лист покачивается при падении: замедляется в верхней точке «маятника»
                    p.Y += p.Vy * (0.75 + 0.35 * Math.Abs(Math.Cos(p.Phase * p.SwaySpeed * 1.3))) * dt;
                    p.Angle += p.Spin * dt;
                    p.Scale.ScaleX = Math.Cos(p.Phase * p.Flutter); // переворачивается
                    dead = p.Y > h + 30 || p.X > w + 30;
                    break;

                case Sprite.Petal:
                    p.X += (p.Vx + wind * 0.8 + Math.Sin(p.Phase * p.SwaySpeed * 1.6) * p.Sway) * dt;
                    p.Y += p.Vy * dt;
                    p.Angle += p.Spin * dt;
                    p.Scale.ScaleX = 0.35 + 0.65 * Math.Abs(Math.Cos(p.Phase * p.Flutter)); // кружится
                    dead = p.Y > h + 20 || p.X > w + 20;
                    break;

                case Sprite.Seed:
                    p.X += (p.Vx + wind * 0.5) * dt;
                    p.Y += (p.Vy + Math.Sin(p.Phase * p.SwaySpeed * 1.2) * p.Sway) * dt;
                    p.Rotate.Angle = p.Angle + Math.Sin(p.Phase * 0.9) * 14;
                    dead = p.X > w + 30 || p.Y < -30;
                    break;

                case Sprite.Firefly:
                    p.X += (p.Vx + Math.Sin(p.Phase * 0.7) * 10) * dt;
                    p.Y += (p.Vy + Math.Cos(p.Phase * 0.9) * 8) * dt;
                    opacity *= SmoothPulse(p.Age / p.Life);
                    dead = p.Age >= p.Life;
                    break;

                case Sprite.Ember or Sprite.Mote:
                    p.X += (p.Vx + Math.Sin(p.Phase * 1.7) * p.Sway) * dt;
                    p.Y += p.Vy * dt;
                    opacity *= 0.25 + 0.75 * Math.Clamp(p.Y / Math.Max(h, 1), 0, 1);
                    dead = p.Y < -10;
                    break;

                case Sprite.Bat:
                    if (p.Age < 0) { opacity = 0; break; }
                    p.X += p.Vx * dt;
                    p.Y += Math.Sin(p.Phase * 2.2) * p.Sway * dt;
                    p.Scale.ScaleY = 0.55 + 0.45 * Math.Sin(p.Phase * p.Flutter); // взмахи крыльев
                    dead = p.X < -60 || p.X > w + 60;
                    break;

                default: // Sparkle
                    p.Angle += p.Spin * dt;
                    var k = SmoothPulse(p.Age / p.Life);
                    opacity *= k;
                    p.Scale.ScaleX = p.Scale.ScaleY = 0.6 + 0.4 * k;
                    dead = p.Age >= p.Life;
                    break;
            }

            if (dead)
            {
                Spawn(p, initial: false);
                continue;
            }

            // Плавное проявление новых частиц
            if (p.Kind is not (Sprite.Firefly or Sprite.Sparkle)) opacity *= Math.Clamp(p.Age / 0.8, 0, 1);
            p.Shape.Opacity = opacity;
            if (p.Kind != Sprite.Seed) Apply(p);
            else { p.Move.X = p.X; p.Move.Y = p.Y; }
        }
    }

    /// <summary>0 → 1 → 0 с мягкими краями (для вспышек и светлячков).</summary>
    private static double SmoothPulse(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var s = Math.Sin(Math.PI * t);
        return s * s * (3 - 2 * s);
    }
}
