using Avalonia.Controls;
using Avalonia.Interactivity;
using PulseRelay.Desktop.ViewModels;

namespace PulseRelay.Desktop.Views;

public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DiagnosticsViewModel viewModel)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(viewModel.BuildReport());
        viewModel.ShowCopiedFeedback();
    }
}
