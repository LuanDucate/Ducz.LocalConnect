using System.Net;
using System.Net.Sockets;

namespace Ducz.LocalConnect.App;

public partial class Form1 : Form
{
    private readonly RemoteHostService _hostService = new();
    private readonly RemoteClientService _clientService = new();

    private TabControl _mainTabs = null!;
    private TabPage _hostTab = null!;
    private TabPage _clientTab = null!;
    private Label _hostIpLabel = null!;
    private NumericUpDown _hostPortInput = null!;
    private Button _startHostButton = null!;
    private Button _stopHostButton = null!;
    private Label _hostStatusLabel = null!;
    private TextBox _clientHostInput = null!;
    private NumericUpDown _clientPortInput = null!;
    private Button _connectButton = null!;
    private Button _disconnectButton = null!;
    private Label _clientStatusLabel = null!;
    private FocusablePictureBox _remoteScreen = null!;
    private Label _clientHintLabel = null!;

    private Size _remoteDesktopSize = Size.Empty;

    public Form1()
    {
        InitializeComponent();
        BuildLayout();
        WireEvents();
        RefreshNetworkInfo();
        UpdateActionState();
    }

    private void BuildLayout()
    {
        Text = "Ducz LocalConnect";
        MinimumSize = new Size(980, 720);
        KeyPreview = true;

        _mainTabs = new TabControl
        {
            Dock = DockStyle.Fill
        };

        _hostTab = new TabPage("Host");
        _clientTab = new TabPage("Cliente");

        BuildHostTab();
        BuildClientTab();

        _mainTabs.TabPages.Add(_hostTab);
        _mainTabs.TabPages.Add(_clientTab);

        Controls.Add(_mainTabs);
    }

    private void BuildHostTab()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Compartilhe este computador na rede local"
        };

        var settingsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 12, 0, 0)
        };

        settingsPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 8, 0),
            Text = "Porta"
        });

        _hostPortInput = new NumericUpDown
        {
            Minimum = 1024,
            Maximum = 65535,
            Value = 5050,
            Width = 100
        };
        settingsPanel.Controls.Add(_hostPortInput);

        _startHostButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(12, 0, 0, 0),
            Text = "Iniciar host"
        };
        settingsPanel.Controls.Add(_startHostButton);

        _stopHostButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            Text = "Parar"
        };
        settingsPanel.Controls.Add(_stopHostButton);

        _hostIpLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 18, 0, 0),
            Text = "IPs locais: carregando..."
        };

        _hostStatusLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0),
            Text = "Status: host parado."
        };

        var notes = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            MaximumSize = new Size(800, 0),
            Text = "MVP para LAN: um cliente por vez, captura da tela principal e controle básico de mouse e teclado. Use apenas em rede confiável."
        };

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(settingsPanel, 0, 1);
        root.Controls.Add(_hostIpLabel, 0, 2);

        var statusPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0),
            WrapContents = false
        };
        statusPanel.Controls.Add(_hostStatusLabel);
        statusPanel.Controls.Add(notes);

        root.Controls.Add(statusPanel, 0, 3);
        _hostTab.Controls.Add(root);
    }

    private void BuildClientTab()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var connectPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        connectPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 8, 0),
            Text = "IP do host"
        });

        _clientHostInput = new TextBox
        {
            Width = 180,
            Text = "127.0.0.1"
        };
        connectPanel.Controls.Add(_clientHostInput);

        connectPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(12, 8, 8, 0),
            Text = "Porta"
        });

        _clientPortInput = new NumericUpDown
        {
            Minimum = 1024,
            Maximum = 65535,
            Value = 5050,
            Width = 100
        };
        connectPanel.Controls.Add(_clientPortInput);

        _connectButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(12, 0, 0, 0),
            Text = "Conectar"
        };
        connectPanel.Controls.Add(_connectButton);

        _disconnectButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            Text = "Desconectar"
        };
        connectPanel.Controls.Add(_disconnectButton);

        _clientStatusLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0),
            Text = "Status: cliente desconectado."
        };

        _clientHintLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            MaximumSize = new Size(900, 0),
            Text = "Clique na imagem remota para capturar mouse e teclado. A tela usa modo Zoom, então os cliques são ajustados para a resolução do host."
        };

        _remoteScreen = new FocusablePictureBox
        {
            BackColor = Color.FromArgb(24, 24, 24),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 16, 0, 0),
            SizeMode = PictureBoxSizeMode.Zoom,
            TabStop = true
        };

        root.Controls.Add(connectPanel, 0, 0);

        var infoPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0),
            WrapContents = false
        };
        infoPanel.Controls.Add(_clientStatusLabel);
        infoPanel.Controls.Add(_clientHintLabel);

        root.Controls.Add(infoPanel, 0, 1);
        root.Controls.Add(_remoteScreen, 0, 2);

        _clientTab.Controls.Add(root);
    }

    private void WireEvents()
    {
        _startHostButton.Click += StartHostButton_Click;
        _stopHostButton.Click += StopHostButton_Click;
        _connectButton.Click += ConnectButton_Click;
        _disconnectButton.Click += DisconnectButton_Click;

        _remoteScreen.MouseMove += RemoteScreen_MouseMove;
        _remoteScreen.MouseDown += RemoteScreen_MouseDown;
        _remoteScreen.MouseUp += RemoteScreen_MouseUp;
        _remoteScreen.MouseWheel += RemoteScreen_MouseWheel;
        _remoteScreen.MouseClick += (_, _) => _remoteScreen.Focus();

        KeyDown += Form1_KeyDown;
        KeyUp += Form1_KeyUp;
        FormClosing += Form1_FormClosing;

        _hostService.StatusChanged += message => RunOnUiThread(() =>
        {
            _hostStatusLabel.Text = $"Status: {message}";
            UpdateActionState();
        });

        _clientService.StatusChanged += message => RunOnUiThread(() =>
        {
            _clientStatusLabel.Text = $"Status: {message}";
            UpdateActionState();
        });

        _clientService.FrameReceived += frame => RunOnUiThread(() => UpdateRemoteFrame(frame));
        _clientService.ConnectionChanged += _ => RunOnUiThread(UpdateActionState);
    }

    private void RefreshNetworkInfo()
    {
        var addresses = Dns.GetHostEntry(Dns.GetHostName())
            .AddressList
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            .Select(address => address.ToString())
            .Distinct()
            .ToArray();

        _hostIpLabel.Text = addresses.Length == 0
            ? "IPs locais: nenhum IPv4 detectado."
            : $"IPs locais: {string.Join(", ", addresses)}";

        if (addresses.Length > 0 && string.Equals(_clientHostInput.Text, "127.0.0.1", StringComparison.Ordinal))
        {
            _clientHostInput.Text = addresses[0];
        }
    }

    private void UpdateActionState()
    {
        _startHostButton.Enabled = !_hostService.IsRunning;
        _stopHostButton.Enabled = _hostService.IsRunning;
        _hostPortInput.Enabled = !_hostService.IsRunning;

        _connectButton.Enabled = !_clientService.IsConnected;
        _disconnectButton.Enabled = _clientService.IsConnected;
        _clientHostInput.Enabled = !_clientService.IsConnected;
        _clientPortInput.Enabled = !_clientService.IsConnected;
    }

    private void UpdateRemoteFrame(RemoteFrame frame)
    {
        using var stream = new MemoryStream(frame.ImageBytes, writable: false);
        using var sourceImage = Image.FromStream(stream);
        var bitmap = new Bitmap(sourceImage);

        var previousImage = _remoteScreen.Image;
        _remoteScreen.Image = bitmap;
        previousImage?.Dispose();
        _remoteDesktopSize = new Size(frame.Width, frame.Height);
    }

    private async void StartHostButton_Click(object? sender, EventArgs e)
    {
        try
        {
            _hostService.Start((int)_hostPortInput.Value);
            UpdateActionState();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Erro ao iniciar host", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        await Task.CompletedTask;
    }

    private async void StopHostButton_Click(object? sender, EventArgs e)
    {
        await _hostService.StopAsync();
        UpdateActionState();
    }

    private async void ConnectButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await _clientService.ConnectAsync(_clientHostInput.Text.Trim(), (int)_clientPortInput.Value);
            _remoteScreen.Focus();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Erro ao conectar", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateActionState();
        }
    }

    private async void DisconnectButton_Click(object? sender, EventArgs e)
    {
        await _clientService.DisconnectAsync();
        UpdateActionState();
    }

    private async void RemoteScreen_MouseMove(object? sender, MouseEventArgs e)
    {
        var point = TranslateClientPoint(e.Location);
        if (!_clientService.IsConnected || point is null)
        {
            return;
        }

        await _clientService.SendMouseMoveAsync(point.Value.X, point.Value.Y);
    }

    private async void RemoteScreen_MouseDown(object? sender, MouseEventArgs e)
    {
        var point = TranslateClientPoint(e.Location);
        var button = ToRemoteMouseButton(e.Button);
        if (!_clientService.IsConnected || point is null || button is null)
        {
            return;
        }

        _remoteScreen.Focus();
        await _clientService.SendMouseButtonAsync(point.Value.X, point.Value.Y, button.Value, isDown: true);
    }

    private async void RemoteScreen_MouseUp(object? sender, MouseEventArgs e)
    {
        var point = TranslateClientPoint(e.Location);
        var button = ToRemoteMouseButton(e.Button);
        if (!_clientService.IsConnected || point is null || button is null)
        {
            return;
        }

        await _clientService.SendMouseButtonAsync(point.Value.X, point.Value.Y, button.Value, isDown: false);
    }

    private async void RemoteScreen_MouseWheel(object? sender, MouseEventArgs e)
    {
        var point = TranslateClientPoint(e.Location);
        if (!_clientService.IsConnected || point is null)
        {
            return;
        }

        await _clientService.SendMouseWheelAsync(point.Value.X, point.Value.Y, e.Delta);
    }

    private async void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!ShouldForwardKeyboard())
        {
            return;
        }

        e.Handled = true;
        await _clientService.SendKeyAsync((int)e.KeyCode, isDown: true);
    }

    private async void Form1_KeyUp(object? sender, KeyEventArgs e)
    {
        if (!ShouldForwardKeyboard())
        {
            return;
        }

        e.Handled = true;
        await _clientService.SendKeyAsync((int)e.KeyCode, isDown: false);
    }

    private async void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        await _clientService.DisconnectAsync();
        await _hostService.StopAsync();
    }

    private bool ShouldForwardKeyboard()
    {
        return _clientService.IsConnected
            && _mainTabs.SelectedTab == _clientTab
            && _remoteScreen.ContainsFocus;
    }

    private Point? TranslateClientPoint(Point localPoint)
    {
        if (_remoteDesktopSize.Width <= 0 || _remoteDesktopSize.Height <= 0 || _remoteScreen.Width <= 0 || _remoteScreen.Height <= 0)
        {
            return null;
        }

        var imageRatio = _remoteDesktopSize.Width / (double)_remoteDesktopSize.Height;
        var boxRatio = _remoteScreen.Width / (double)_remoteScreen.Height;

        int renderedWidth;
        int renderedHeight;
        int offsetX;
        int offsetY;

        if (imageRatio > boxRatio)
        {
            renderedWidth = _remoteScreen.Width;
            renderedHeight = (int)Math.Round(renderedWidth / imageRatio);
            offsetX = 0;
            offsetY = (_remoteScreen.Height - renderedHeight) / 2;
        }
        else
        {
            renderedHeight = _remoteScreen.Height;
            renderedWidth = (int)Math.Round(renderedHeight * imageRatio);
            offsetX = (_remoteScreen.Width - renderedWidth) / 2;
            offsetY = 0;
        }

        var renderedArea = new Rectangle(offsetX, offsetY, renderedWidth, renderedHeight);
        if (!renderedArea.Contains(localPoint))
        {
            return null;
        }

        var normalizedX = (localPoint.X - renderedArea.X) / (double)renderedArea.Width;
        var normalizedY = (localPoint.Y - renderedArea.Y) / (double)renderedArea.Height;

        return new Point(
            Math.Clamp((int)Math.Round(normalizedX * _remoteDesktopSize.Width), 0, _remoteDesktopSize.Width - 1),
            Math.Clamp((int)Math.Round(normalizedY * _remoteDesktopSize.Height), 0, _remoteDesktopSize.Height - 1));
    }

    private static RemoteMouseButton? ToRemoteMouseButton(MouseButtons button)
    {
        return button switch
        {
            MouseButtons.Left => RemoteMouseButton.Left,
            MouseButtons.Right => RemoteMouseButton.Right,
            MouseButtons.Middle => RemoteMouseButton.Middle,
            _ => null
        };
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }
}
