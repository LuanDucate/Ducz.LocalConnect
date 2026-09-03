using System.Windows.Controls;
using Ducz.LocalConnect.App.Services;

namespace Ducz.LocalConnect.App.Views;

public partial class HostPage : Page
{
    public HostPage()
    {
        DataContext = AppServices.HostViewModel;
        InitializeComponent();
    }
}
