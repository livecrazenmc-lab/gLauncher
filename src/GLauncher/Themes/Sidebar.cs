using System.Windows;

namespace GLauncher.Themes;

/// <summary>Ширина боковой панели (анимируется при сворачивании до иконок).</summary>
public static class Sidebar
{
    public const double ExpandedWidth = 268;
    public const double CollapsedWidth = 76;

    public static readonly DependencyProperty PanelWidthProperty = DependencyProperty.RegisterAttached(
        "PanelWidth", typeof(double), typeof(Sidebar), new PropertyMetadata(ExpandedWidth));

    public static double GetPanelWidth(DependencyObject d) => (double)d.GetValue(PanelWidthProperty);
    public static void SetPanelWidth(DependencyObject d, double value) => d.SetValue(PanelWidthProperty, value);
}
