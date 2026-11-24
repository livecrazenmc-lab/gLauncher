using System.Windows;
using System.Windows.Controls;
using GLauncher.ViewModels;

namespace GLauncher.Views;

public partial class PatchNotesView : UserControl
{
    public PatchNotesView()
    {
        InitializeComponent();
        // Список грузится, только когда вкладку впервые открыли.
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true && DataContext is PatchNotesViewModel vm) await vm.EnsureLoadedAsync();
        };
    }
}
