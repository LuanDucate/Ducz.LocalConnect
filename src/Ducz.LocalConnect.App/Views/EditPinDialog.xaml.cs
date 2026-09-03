using Ducz.LocalConnect.App.Services;
using Ducz.LocalConnect.Core.Protocol;
using Wpf.Ui.Controls;

namespace Ducz.LocalConnect.App.Views;

public partial class EditPinDialog : FluentWindow
{
    public EditPinDialog(PinnedConnection pin)
    {
        InitializeComponent();

        NameBox.Text = pin.Name;
        HostBox.Text = pin.Host;
        PortBox.Value = pin.Port;
        PortBox.Minimum = ProtocolLimits.MinPort;
        PortBox.Maximum = ProtocolLimits.MaxPort;
        PinBox.Password = pin.Pin;
    }

    public string ConnectionName => string.IsNullOrWhiteSpace(NameBox.Text) ? HostAddress : NameBox.Text.Trim();

    public string HostAddress => HostBox.Text.Trim();

    public int PortValue => (int)(PortBox.Value ?? ProtocolLimits.DefaultPort);

    public string PinValue => PinBox.Password.Trim();

    private void Save_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(HostAddress))
        {
            HostBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
