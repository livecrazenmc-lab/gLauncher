using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GLauncher;

public sealed class BoolToTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "да";
    public string FalseText { get; set; } = "нет";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? TrueText : FalseText;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true, если все значения равны (например, элемент списка == активный аккаунт).</summary>
public sealed class EqualsMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is not null && Equals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>UUID игрока → лицо скина из кэша (без сети).</summary>
public sealed class UuidToFaceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is string uuid && GLauncher.Core.Players.PlayerProfileService.LoadCached(uuid) is { SkinPath: { } path }
            ? GLauncher.ViewModels.SkinFace.Load(path)
            : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal static class Shell
{
    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "gLauncher"); }
    }
}
