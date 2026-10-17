using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BedsideRemote.Core;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace BedsideRemote.Agent;

public sealed class MainWindow : Window
{
    private readonly RemoteServer server;
    private readonly Storage storage;
    private Settings settings;
    private readonly string fingerprint;
    private readonly TextBlock status = new();
    private readonly ListBox deviceList = new() { MinHeight = 92, MaxHeight = 160, DisplayMemberPath = "DeviceName", Background = new SolidColorBrush(Color.FromRgb(29, 37, 45)), Foreground = Brushes.WhiteSmoke, BorderThickness = new Thickness(0), Padding = new Thickness(10) };
    private readonly TextBlock emptyDevices = new() { Text = "No paired devices yet", Foreground = new SolidColorBrush(Color.FromRgb(167, 180, 190)), Margin = new Thickness(12), IsHitTestVisible = false };
    private readonly System.Windows.Forms.NotifyIcon tray;
    private bool exiting;
    private Window? pairingDialog;
    public event EventHandler? ExitRequested;
    private static readonly SolidColorBrush BackgroundBrush = new(Color.FromRgb(18, 23, 29));
    private static readonly SolidColorBrush Accent = new(Color.FromRgb(173, 219, 196));
    public MainWindow(Storage storage, Settings settings, RemoteServer server, string fingerprint)
    {
        this.server = server; this.storage = storage; this.settings = settings; this.fingerprint = fingerprint;
        Title = "Bedside Remote"; Width = 620; Height = 790; MinWidth = 480; MinHeight = 530;
        Background = BackgroundBrush; Foreground = Brushes.WhiteSmoke; FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 15;
        Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = DarkScrollBarStyle();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(32) };
        panel.Children.Add(Label("BEDSIDE REMOTE", 12, Accent));
        panel.Children.Add(Label("Your PC. Within reach.", 30));
        status.Text = "Starting secure local control…"; status.Margin = new Thickness(0, 10, 0, 24); status.TextWrapping = TextWrapping.Wrap; panel.Children.Add(status);
        var pair = Action("Pair an Android device", (_, _) => ShowPairing()); panel.Children.Add(pair);
        panel.Children.Add(Label("Only devices you approve can control this desktop.", 13));
        panel.Children.Add(Label("LOCAL ADDRESSES", 12, Accent, 24));
        panel.Children.Add(CopyBox(Addresses(settings.Port)));
        panel.Children.Add(Label("If discovery is unavailable, enter one of these addresses on Android.", 13));
        panel.Children.Add(Label("PAIRED DEVICES", 12, Accent, 24));
        AutomationProperties.SetName(deviceList, "Paired Android devices");
        var devicePanel = new System.Windows.Controls.Grid(); devicePanel.Children.Add(deviceList); devicePanel.Children.Add(emptyDevices); panel.Children.Add(devicePanel);
        panel.Children.Add(Action("Revoke selected device", (_, _) =>
        {
            if (deviceList.SelectedItem is PairedDevice device && System.Windows.MessageBox.Show(this, $"Revoke {device.DeviceName}? Its active connections will close immediately.", "Revoke device", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) server.Revoke(device.DeviceId);
        }));
        var startup = new CheckBox { Content = "Start after Windows sign-in", IsChecked = settings.Startup, Margin = new Thickness(0, 24, 0, 8), Foreground = Brushes.WhiteSmoke };
        startup.Click += (_, _) =>
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (startup.IsChecked == true) key.SetValue("BedsideRemote", "\"" + Environment.ProcessPath + "\"");
                else key.DeleteValue("BedsideRemote", false);
                this.settings = this.settings with { Startup = startup.IsChecked == true }; storage.Save("settings.json", this.settings);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { startup.IsChecked = this.settings.Startup; SetStatus("Could not update startup settings.", true); }
        }; panel.Children.Add(startup);
        var portPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        portPanel.Children.Add(Label("HTTPS port", 14));
        var port = new TextBox { Text = settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 80, Margin = new Thickness(12, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromRgb(29, 37, 45)), Foreground = Brushes.WhiteSmoke, BorderThickness = new Thickness(0), Padding = new Thickness(8) };
        AutomationProperties.SetName(port, "HTTPS port (1024 to 65535)"); portPanel.Children.Add(port);
        portPanel.Children.Add(Action("Save port", (_, _) =>
        {
            if (!int.TryParse(port.Text, out var number) || number is < 1024 or > 65535) { SetStatus("Choose a port from 1024 to 65535.", true); return; }
            try
            {
                var updated = this.settings with { Port = number }; storage.Save("settings.json", updated); this.settings = updated;
                SetStatus("Port saved. Exit and reopen Bedside Remote to apply.", false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("Could not save the port. Check access to local app data.", true); }
        })); panel.Children.Add(portPanel);
        panel.Children.Add(Action("Check latest release", (_, _) => Program.OpenRelease()));
        panel.Children.Add(Label("Universal controls work without the browser extension.\nClosing this window keeps the agent in the notification area.", 12));
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Bedside Remote", null, (_, _) => Dispatcher.Invoke(OpenWindow));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Exit));
        tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Bedside Remote", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenWindow);
        Closing += (_, e) => { if (!exiting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => tray.Dispose();
        server.Approve = ApproveAsync; server.DevicesChanged += () => Dispatcher.BeginInvoke(RefreshDevices); RefreshDevices();
        if (SystemParameters.ClientAreaAnimation) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }
    public void SetStatus(string message, bool error) => Dispatcher.Invoke(() => { status.Text = message; status.Foreground = error ? Brushes.LightSalmon : Accent; });
    private void RefreshDevices()
    {
        var devices = server.Devices; deviceList.ItemsSource = devices;
        emptyDevices.Visibility = devices.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OpenWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void Exit() { if (exiting) return; exiting = true; tray.Visible = false; tray.Dispose(); ExitRequested?.Invoke(this, EventArgs.Empty); }
    private void ShowPairing()
    {
        if (pairingDialog != null) { pairingDialog.Activate(); return; }
        var code = server.Pairing.Open();
        var dialog = Dialog("Pair an Android device", 560, 480);
        pairingDialog = dialog;
        var body = new StackPanel { Margin = new Thickness(28) };
        body.Children.Add(Label("Verify this PC first", 24));
        body.Children.Add(Label("On Android, enter the SHA-256 fingerprint below before sending the pairing code. Never accept an unexpected fingerprint.", 14));
        body.Children.Add(CopyBox(fingerprint));
        body.Children.Add(Label("PAIRING CODE · EXPIRES IN FIVE MINUTES", 12, Accent, 20));
        body.Children.Add(Label(code, 40, Accent));
        body.Children.Add(Label("Keep this window open. Approve the request here after entering the code on Android.", 14));
        body.Children.Add(Action("Close pairing", (_, _) => dialog.Close()));
        dialog.Content = body;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        timer.Tick += (_, _) => dialog.Close(); timer.Start();
        dialog.Closed += (_, _) => { timer.Stop(); server.Pairing.Close(); pairingDialog = null; };
        dialog.Show();
    }
    private Task<bool> ApproveAsync(string name, CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.BeginInvoke(() =>
        {
            if (token.IsCancellationRequested) { completion.TrySetResult(false); return; }
            OpenWindow();
            var dialog = Dialog("Approve new device", 500, 300);
            var body = new StackPanel { Margin = new Thickness(28) };
            body.Children.Add(Label("Allow desktop control?", 24));
            body.Children.Add(Label($"{name} is requesting mouse, keyboard and media control on this PC. Approve only if you initiated this pairing.", 16));
            var approved = false;
            body.Children.Add(Action("Approve device", (_, _) => { approved = true; dialog.Close(); }));
            body.Children.Add(Action("Deny", (_, _) => dialog.Close())); dialog.Content = body;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
            timer.Tick += (_, _) => dialog.Close(); timer.Start();
            var registration = token.Register(() => Dispatcher.BeginInvoke(() => dialog.Close()));
            dialog.Closed += (_, _) => { timer.Stop(); registration.Dispose(); completion.TrySetResult(approved && !token.IsCancellationRequested); };
            dialog.Show(); dialog.Activate();
        });
        return completion.Task;
    }
    private Window Dialog(string title, double width, double height) => new() { Title = title, Owner = this, Width = width, Height = height, Background = BackgroundBrush, Foreground = Brushes.WhiteSmoke, FontFamily = FontFamily, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
    private static TextBlock Label(string text, double size, System.Windows.Media.Brush? color = null, double top = 8) => new() { Text = text, FontSize = size, Foreground = color ?? Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 8) };
    private static TextBox CopyBox(string text) => new() { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(10), Background = new SolidColorBrush(Color.FromRgb(29, 37, 45)), Foreground = Brushes.WhiteSmoke, BorderThickness = new Thickness(0), FontFamily = new System.Windows.Media.FontFamily("Consolas") };
    private static Button Action(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, MinHeight = 42, Background = Accent, Foreground = BackgroundBrush, BorderThickness = new Thickness(0) };
        button.Click += handler; return button;
    }
    private static string Addresses(int port)
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up)
            .OrderByDescending(x => x.GetIPProperties().GatewayAddresses.Count != 0)
            .SelectMany(x => x.GetIPProperties().UnicastAddresses).Select(x => x.Address).Where(x => Protocol.IsLan(x) && !x.IsIPv6LinkLocal && !System.Net.IPAddress.IsLoopback(x)).Distinct()
            .Select(x => $"https://{(x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + x + "]" : x.ToString())}:{port}");
        return string.Join("\n", addresses.DefaultIfEmpty($"https://127.0.0.1:{port} · no LAN adapter found"));
    }
    private static Style DarkScrollBarStyle() => (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
          <Setter Property="Width" Value="12"/>
          <Setter Property="Background" Value="#12171D"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ScrollBar">
                <Border Background="{TemplateBinding Background}">
                  <Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="{TemplateBinding Orientation}">
                    <Track.DecreaseRepeatButton>
                      <RepeatButton Command="ScrollBar.PageUpCommand" Focusable="False">
                        <RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template>
                      </RepeatButton>
                    </Track.DecreaseRepeatButton>
                    <Track.Thumb>
                      <Thumb MinHeight="28" Margin="3,2" Background="#50636F">
                        <Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{TemplateBinding Background}" CornerRadius="3"/></ControlTemplate></Thumb.Template>
                      </Thumb>
                    </Track.Thumb>
                    <Track.IncreaseRepeatButton>
                      <RepeatButton Command="ScrollBar.PageDownCommand" Focusable="False">
                        <RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template>
                      </RepeatButton>
                    </Track.IncreaseRepeatButton>
                  </Track>
                </Border>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);
}
