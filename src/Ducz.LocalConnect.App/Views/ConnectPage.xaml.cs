using System.Windows.Controls;
using Ducz.LocalConnect.App.Services;

namespace Ducz.LocalConnect.App.Views;

public partial class ConnectPage : Page
{
    public ConnectPage()
    {
        DataContext = AppServices.ConnectViewModel;
        InitializeComponent();
    }
}
