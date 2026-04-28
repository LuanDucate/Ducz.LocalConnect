using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Ducz.LocalConnect.App;

public partial class DuczLocalConnectForm : Form
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
    private TextBox _hostPinInput = null!;
    private TextBox _clientHostInput = null!;
    private NumericUpDown _clientPortInput = null!;
    private TextBox _clientPinInput = null!;
    private Button _connectButton = null!;
    private Button _disconnectButton = null!;
    private Button _fullScreenButton = null!;
    private Button _keyboardCaptureButton = null!;
    private Button _sendClipboardButton = null!;
    private Button _receiveClipboardButton = null!;
    private Button _sendFileButton = null!;
    private Label _clientStatusLabel = null!;
    private FocusablePictureBox _remoteScreen = null!;
    private Label _clientHintLabel = null!;
    private Label _clientMonitorsLabel = null!;
    private FlowLayoutPanel _clientMonitorsPanel = null!;
    private Panel _remoteScreenHost = null!;

    private bool _isFullScreen;
    private bool _isKeyboardCaptureEnabled;
    private bool _updatingMonitorOptions;
    private FormBorderStyle _previousBorderStyle;
    private FormWindowState _previousWindowState;
    private bool _previousTopMost;
    private Size _remoteDesktopSize = Size.Empty;
    private Bitmap? _remoteDesktopBitmap;
    private long _lastMouseMoveSentAt;

    public DuczLocalConnectForm()
    {
        InitializeComponent();
        BuildLayout();
        WireEvents();
        RefreshNetworkInfo();
        ApplyBranding();
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
        _clientTab = new TabPage("Client");

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
            Text = "Share this computer on your local network"
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
            Text = "Port"
        });

        _hostPortInput = new NumericUpDown
        {
            Minimum = 1024,
            Maximum = 65535,
            Value = 5050,
            Width = 100
        };
        settingsPanel.Controls.Add(_hostPortInput);

        settingsPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(12, 8, 8, 0),
            Text = "PIN"
        });

        _hostPinInput = new TextBox
        {
            Width = 120,
            Text = "123456",
            UseSystemPasswordChar = true
        };
        settingsPanel.Controls.Add(_hostPinInput);

        _startHostButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(12, 0, 0, 0),
            Text = "Start host"
        };
        settingsPanel.Controls.Add(_startHostButton);

        _stopHostButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            Text = "Stop"
        };
        settingsPanel.Controls.Add(_stopHostButton);

        _hostIpLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 18, 0, 0),
            Text = "Local IPs: loading..."
        };

        _hostStatusLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0),
            Text = "Status: host stopped."
        };

        var notes = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            MaximumSize = new Size(800, 0),
            Text = "The host now exposes video, remote input and system audio. The client can choose which monitor to view after connecting. Audio uses the next port after video, so if video uses 5050, audio uses 5051. The client must provide the PIN before connecting."
        };

        root.Controls.Add(BuildTitleHeader(title), 0, 0);
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
            Text = "Control another computer on your local network"
        };

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
            Text = "Host IP"
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
            Text = "Port"
        });

        _clientPortInput = new NumericUpDown
        {
            Minimum = 1024,
            Maximum = 65535,
            Value = 5050,
            Width = 100
        };
        connectPanel.Controls.Add(_clientPortInput);

        connectPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(12, 8, 8, 0),
            Text = "PIN"
        });

        _clientPinInput = new TextBox
        {
            Width = 120,
            Text = "123456",
            UseSystemPasswordChar = true
        };
        connectPanel.Controls.Add(_clientPinInput);

        _connectButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(12, 0, 0, 0),
            Text = "Connect"
        };
        connectPanel.Controls.Add(_connectButton);

        _disconnectButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            Text = "Disconnect"
        };
        connectPanel.Controls.Add(_disconnectButton);

        _fullScreenButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            Text = "Full screen"
        };
        connectPanel.Controls.Add(_fullScreenButton);

        _keyboardCaptureButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            Text = "Capture keyboard"
        };
        connectPanel.Controls.Add(_keyboardCaptureButton);

        _sendClipboardButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            Text = "Send clipboard"
        };
        connectPanel.Controls.Add(_sendClipboardButton);

        _receiveClipboardButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            Text = "Fetch remote clipboard"
        };
        connectPanel.Controls.Add(_receiveClipboardButton);

        _sendFileButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            Text = "Send file"
        };
        connectPanel.Controls.Add(_sendFileButton);

        _clientStatusLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0),
            Text = "Status: client disconnected."
        };

        _clientHintLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            MaximumSize = new Size(900, 0),
            Text = "Click the remote image or use Capture keyboard to send keystrokes to the host. Press Ctrl+Alt+Home to toggle keyboard capture. While capture is paused, local shortcuts such as Alt+Tab stay on this PC. Remote audio connects automatically. Clipboard and files are transferred manually using the buttons above."
        };

        _clientMonitorsLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            Text = "Remote monitors: connect to load options."
        };

        _clientMonitorsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            WrapContents = true
        };

        _remoteScreen = new FocusablePictureBox
        {
            BackColor = Color.FromArgb(24, 24, 24),
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            SizeMode = PictureBoxSizeMode.Zoom,
            TabStop = true
        };

        _remoteScreenHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 16, 0, 0),
            BackColor = Color.Black
        };
        _remoteScreenHost.Controls.Add(_remoteScreen);

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
        infoPanel.Controls.Add(_clientMonitorsLabel);
        infoPanel.Controls.Add(_clientMonitorsPanel);

        root.Controls.Add(BuildTitleHeader(title), 0, 0);
        root.Controls.Add(connectPanel, 0, 1);
        root.Controls.Add(infoPanel, 0, 2);
        root.Controls.Add(_remoteScreenHost, 0, 3);

        _clientTab.Controls.Add(root);
    }

    private void WireEvents()
    {
        _startHostButton.Click += StartHostButton_Click;
        _stopHostButton.Click += StopHostButton_Click;
        _connectButton.Click += ConnectButton_Click;
        _disconnectButton.Click += DisconnectButton_Click;
        _fullScreenButton.Click += FullScreenButton_Click;
        _keyboardCaptureButton.Click += KeyboardCaptureButton_Click;
        _sendClipboardButton.Click += SendClipboardButton_Click;
        _receiveClipboardButton.Click += ReceiveClipboardButton_Click;
        _sendFileButton.Click += SendFileButton_Click;

        _remoteScreen.MouseMove += RemoteScreen_MouseMove;
        _remoteScreen.MouseDown += RemoteScreen_MouseDown;
        _remoteScreen.MouseUp += RemoteScreen_MouseUp;
        _remoteScreen.MouseWheel += RemoteScreen_MouseWheel;
        _remoteScreen.MouseClick += (_, _) =>
        {
            _remoteScreen.Focus();
            SetKeyboardCapture(true);
        };

        KeyDown += Form_KeyDown;
        KeyUp += Form_KeyUp;
        FormClosing += Form_FormClosing;

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
        _clientService.MonitorsChanged += monitors => RunOnUiThread(() => UpdateMonitorOptions(monitors));
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
            ? "Local IPs: no IPv4 address detected."
            : $"Local IPs: {string.Join(", ", addresses)}";

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
        _hostPinInput.Enabled = !_hostService.IsRunning;

        _connectButton.Enabled = !_clientService.IsConnected;
        _disconnectButton.Enabled = _clientService.IsConnected;
        _fullScreenButton.Enabled = _clientService.IsConnected;
        _keyboardCaptureButton.Enabled = _clientService.IsConnected;
        _sendClipboardButton.Enabled = _clientService.IsConnected;
        _receiveClipboardButton.Enabled = _clientService.IsConnected;
        _sendFileButton.Enabled = _clientService.IsConnected;
        _clientHostInput.Enabled = !_clientService.IsConnected;
        _clientPortInput.Enabled = !_clientService.IsConnected;
        _clientPinInput.Enabled = !_clientService.IsConnected;
        _fullScreenButton.Text = _isFullScreen ? "Exit full screen" : "Full screen";
        _keyboardCaptureButton.Text = _isKeyboardCaptureEnabled ? "Pause keyboard" : "Capture keyboard";
    }

    private void UpdateMonitorOptions(IReadOnlyList<RemoteMonitorInfo> monitors)
    {
        _updatingMonitorOptions = true;

        try
        {
            _clientMonitorsPanel.SuspendLayout();
            _clientMonitorsPanel.Controls.Clear();

            if (monitors.Count == 0)
            {
                _clientMonitorsLabel.Text = _clientService.IsConnected ? "Remote monitors: unavailable." : "Remote monitors: connect to load options.";
                return;
            }

            _clientMonitorsLabel.Text = "Remote monitors: click another option to switch displays.";

            foreach (var monitor in monitors)
            {
                var option = new CheckBox
                {
                    AutoSize = true,
                    Checked = monitor.IsSelected,
                    Margin = new Padding(0, 0, 8, 0),
                    Tag = monitor.DeviceName,
                    Text = monitor.DisplayName
                };
                option.CheckedChanged += MonitorOption_CheckedChanged;
                _clientMonitorsPanel.Controls.Add(option);
            }
        }
        finally
        {
            _clientMonitorsPanel.ResumeLayout();
            _updatingMonitorOptions = false;
        }
    }

    private void UpdateRemoteFrame(RemoteFrame frame)
    {
        using var stream = new MemoryStream(frame.ImageBytes, writable: false);
        using var sourceImage = Image.FromStream(stream);
        using var bitmap = new Bitmap(sourceImage);

        if (frame.Kind == RemoteFrameKind.Full || _remoteDesktopBitmap is null || _remoteDesktopBitmap.Width != frame.DesktopWidth || _remoteDesktopBitmap.Height != frame.DesktopHeight)
        {
            var previousImage = _remoteDesktopBitmap;
            _remoteDesktopBitmap = new Bitmap(bitmap);
            _remoteScreen.Image = _remoteDesktopBitmap;
            previousImage?.Dispose();
        }
        else
        {
            using var graphics = Graphics.FromImage(_remoteDesktopBitmap);
            graphics.DrawImage(bitmap, new Rectangle(frame.X, frame.Y, frame.Width, frame.Height));
            _remoteScreen.Invalidate();
        }

        _remoteDesktopSize = new Size(frame.DesktopWidth, frame.DesktopHeight);
    }

    private void StartHostButton_Click(object? sender, EventArgs e)
    {
        try
        {
            _hostService.Start((int)_hostPortInput.Value, _hostPinInput.Text.Trim());
            UpdateActionState();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Failed to start host", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
            await _clientService.ConnectAsync(_clientHostInput.Text.Trim(), (int)_clientPortInput.Value, _clientPinInput.Text.Trim());
            _remoteScreen.Focus();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Failed to connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateActionState();
        }
    }

    private async void DisconnectButton_Click(object? sender, EventArgs e)
    {
        ExitFullScreen();
        await _clientService.DisconnectAsync();
        UpdateActionState();
    }

    private void FullScreenButton_Click(object? sender, EventArgs e)
    {
        ToggleFullScreen();
    }

    private void KeyboardCaptureButton_Click(object? sender, EventArgs e)
    {
        SetKeyboardCapture(!_isKeyboardCaptureEnabled);
    }

    private async void SendClipboardButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await _clientService.SendClipboardTextAsync(WindowsClipboard.GetText());
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Failed to send clipboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void ReceiveClipboardButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await _clientService.RequestClipboardAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Failed to fetch remote clipboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void SendFileButton_Click(object? sender, EventArgs e)
    {
        using var fileDialog = new OpenFileDialog
        {
            Title = "Choose a file to send to the host"
        };

        if (fileDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _clientService.SendFileAsync(fileDialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Failed to send file", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void RemoteScreen_MouseMove(object? sender, MouseEventArgs e)
    {
        var point = TranslateClientPoint(e.Location);
        if (!_clientService.IsConnected || point is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsedMilliseconds = (now - _lastMouseMoveSentAt) * 1000d / Stopwatch.Frequency;
        if (elapsedMilliseconds < 12)
        {
            return;
        }

        _lastMouseMoveSentAt = now;
        _ = _clientService.SendMouseMoveAsync(point.Value.X, point.Value.Y);
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

    private async void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F11 && _clientService.IsConnected)
        {
            e.Handled = true;
            ToggleFullScreen();
            return;
        }

        if (e.Control && e.Alt && e.KeyCode == Keys.Home && _clientService.IsConnected)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            SetKeyboardCapture(!_isKeyboardCaptureEnabled);
            return;
        }

        if (e.KeyCode == Keys.Escape && _isFullScreen)
        {
            e.Handled = true;
            ExitFullScreen();
            return;
        }

        if (!ShouldForwardKeyboard())
        {
            return;
        }

        e.Handled = true;
        await _clientService.SendKeyAsync((int)e.KeyCode, isDown: true);
    }

    private async void Form_KeyUp(object? sender, KeyEventArgs e)
    {
        if ((e.KeyCode == Keys.F11 && _clientService.IsConnected) || (e.KeyCode == Keys.Escape && _isFullScreen))
        {
            e.Handled = true;
            return;
        }

        if (!ShouldForwardKeyboard())
        {
            return;
        }

        e.Handled = true;
        await _clientService.SendKeyAsync((int)e.KeyCode, isDown: false);
    }

    private async void Form_FormClosing(object? sender, FormClosingEventArgs e)
    {
        ExitFullScreen();
        _remoteDesktopBitmap?.Dispose();
        await _clientService.DisconnectAsync();
        await _hostService.StopAsync();
    }

    private bool ShouldForwardKeyboard()
    {
        return _clientService.IsConnected
            && _isKeyboardCaptureEnabled
            && (_isFullScreen || _mainTabs.SelectedTab == _clientTab)
            && _remoteScreen.ContainsFocus;
    }

    private void SetKeyboardCapture(bool enabled)
    {
        _isKeyboardCaptureEnabled = enabled && _clientService.IsConnected;
        UpdateActionState();
    }

    private async void MonitorOption_CheckedChanged(object? sender, EventArgs e)
    {
        if (_updatingMonitorOptions || sender is not CheckBox option)
        {
            return;
        }

        if (!option.Checked)
        {
            if (_clientMonitorsPanel.Controls.OfType<CheckBox>().All(checkBox => !checkBox.Checked))
            {
                _updatingMonitorOptions = true;
                option.Checked = true;
                _updatingMonitorOptions = false;
            }

            return;
        }

        var selectedDeviceName = option.Tag as string;
        if (string.IsNullOrWhiteSpace(selectedDeviceName))
        {
            return;
        }

        _updatingMonitorOptions = true;
        foreach (var otherOption in _clientMonitorsPanel.Controls.OfType<CheckBox>())
        {
            if (!ReferenceEquals(otherOption, option))
            {
                otherOption.Checked = false;
            }
        }
        _updatingMonitorOptions = false;

        try
        {
            await _clientService.SelectMonitorAsync(selectedDeviceName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Failed to change monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

    private Control BuildTitleHeader(Label title)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            WrapContents = false
        };

        var logo = CreateLogoPictureBox();
        if (logo is not null)
        {
            panel.Controls.Add(logo);
        }

        title.Margin = new Padding(0, 10, 0, 0);
        panel.Controls.Add(title);
        return panel;
    }

    private PictureBox? CreateLogoPictureBox()
    {
        var logoPath = Path.Combine(AppContext.BaseDirectory, "Ducz_icon.png");
        if (!File.Exists(logoPath))
        {
            return null;
        }

        return new PictureBox
        {
            Image = Image.FromFile(logoPath),
            Margin = new Padding(0, 0, 12, 0),
            Size = new Size(44, 44),
            SizeMode = PictureBoxSizeMode.Zoom
        };
    }

    private void ApplyBranding()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "favicon.ico");
        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }
    }

    private void ToggleFullScreen()
    {
        if (!_clientService.IsConnected)
        {
            return;
        }

        if (_isFullScreen)
        {
            ExitFullScreen();
            return;
        }

        _previousBorderStyle = FormBorderStyle;
        _previousWindowState = WindowState;
        _previousTopMost = TopMost;

        _remoteScreenHost.Controls.Remove(_remoteScreen);
        Controls.Add(_remoteScreen);
        _remoteScreen.Dock = DockStyle.Fill;
        _remoteScreen.BringToFront();

        _mainTabs.Visible = false;
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        TopMost = true;
        _isFullScreen = true;
        _remoteScreen.Focus();
        SetKeyboardCapture(true);
        UpdateActionState();
    }

    private void ExitFullScreen()
    {
        if (!_isFullScreen)
        {
            return;
        }

        Controls.Remove(_remoteScreen);
        _remoteScreenHost.Controls.Add(_remoteScreen);
        _remoteScreen.Dock = DockStyle.Fill;

        _mainTabs.Visible = true;
        FormBorderStyle = _previousBorderStyle;
        WindowState = _previousWindowState;
        TopMost = _previousTopMost;
        _mainTabs.SelectedTab = _clientTab;
        _isFullScreen = false;
        _remoteScreen.Focus();
        UpdateActionState();
    }
}