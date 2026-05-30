using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace oru.Views;

public sealed partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user taps the back arrow.</summary>
    public event Action? BackRequested;

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }
}
