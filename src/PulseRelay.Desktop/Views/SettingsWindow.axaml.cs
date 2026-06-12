using Avalonia.Controls;
using PulseRelay.Desktop.ViewModels;

namespace PulseRelay.Desktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                viewModel.CloseRequested += (_, _) => Close();
            }
        };
    }
}
