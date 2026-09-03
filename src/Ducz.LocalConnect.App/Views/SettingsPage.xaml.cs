using System.Windows.Controls;
using Ducz.LocalConnect.App.Services;

namespace Ducz.LocalConnect.App.Views;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        DataContext = AppServices.SettingsViewModel;
        InitializeComponent();
    }
}
