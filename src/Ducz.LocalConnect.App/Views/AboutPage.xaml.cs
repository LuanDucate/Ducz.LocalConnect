using System.Windows.Controls;
using Ducz.LocalConnect.App.Services;

namespace Ducz.LocalConnect.App.Views;

public partial class AboutPage : Page
{
    public AboutPage()
    {
        DataContext = AppServices.AboutViewModel;
        InitializeComponent();
    }
}
