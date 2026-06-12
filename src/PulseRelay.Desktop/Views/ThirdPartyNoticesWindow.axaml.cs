using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PulseRelay.Desktop.Views;

public partial class ThirdPartyNoticesWindow : Window
{
    public ThirdPartyNoticesWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
