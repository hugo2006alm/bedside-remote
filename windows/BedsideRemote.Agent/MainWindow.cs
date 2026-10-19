using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BedsideRemote.Core;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace BedsideRemote.Agent;

public sealed class MainWindow : Window
{
    private readonly RemoteServer server;
    private readonly Storage storage;
    private Settings settings;
    private readonly string fingerprint;
    private readonly TextBlock status = Text("Starting secure local control…", 13, Muted);
    private readonly TextBlock deviceCount = Text("0 phones", 12, Muted);
    private readonly TextBlock emptyDevices = Text("No phones paired yet. Pair your Android to begin.", 14, Muted);
    private readonly ListBox deviceList = new() { MaxHeight = 168, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Ivory, DisplayMemberPath = "DeviceName" };
    private readonly Button revoke;
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private bool exiting;
    private Window? pairingDialog;
    public event EventHandler? ExitRequested;
    private static readonly SolidColorBrush Canvas = Brush("#101113");
    private static readonly SolidColorBrush Surface = Brush("#181A1D");
    private static readonly SolidColorBrush Ivory = Brush("#F3EEE6");
    private static readonly SolidColorBrush Muted = Brush("#C0BDB7");
    private static readonly SolidColorBrush Brass = Brush("#D8B57A");
    private static readonly FontFamily BodyFont = new(new Uri("pack://application:,,,/"), "./Fonts/#DM Sans");
    private static readonly FontFamily DisplayFont = new(new Uri("pack://application:,,,/"), "./Fonts/#Manrope");

    public MainWindow(Storage storage, Settings settings, RemoteServer server, string fingerprint)
    {
        this.server = server; this.storage = storage; this.settings = settings; this.fingerprint = fingerprint;
        Title = "Bedside Remote"; Width = 640; Height = 700; MinWidth = 400; MinHeight = 360;
        var iconUri = new Uri("pack://application:,,,/BedsideRemote;component/Assets/AppIcon.ico");
        Icon = System.Windows.Media.Imaging.BitmapDecoder.Create(iconUri, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames.OrderByDescending(frame => frame.PixelWidth).First();
        using (var iconStream = System.Windows.Application.GetResourceStream(iconUri).Stream)
            trayIcon = new System.Drawing.Icon(iconStream, 32, 32);
        Background = Canvas; Foreground = Ivory; FontFamily = BodyFont; FontSize = 14;
        Resources = Styles();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var layout = new Grid { Margin = new Thickness(28, 24, 28, 16), Background = Canvas };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        var brand = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        brand.Children.Add(new System.Windows.Controls.Image { Source = Icon, Width = 28, Height = 28, Margin = new Thickness(0, 0, 10, 0) });
        var brandName = Text("BEDSIDE / REMOTE", 11, Brass); brandName.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(brandName); header.Children.Add(brand);
        header.Children.Add(new TextBlock { Text = "Your PC, within reach.", FontFamily = DisplayFont, FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Ivory, Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap });
        header.Children.Add(status); layout.Children.Add(header);
        var cards = new StackPanel();
        var pairRow = new Grid();
        pairRow.ColumnDefinitions.Add(new ColumnDefinition());
        pairRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pairCopy = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        pairCopy.Children.Add(Text("Make yourself comfortable.", 17, Ivory));
        pairCopy.Children.Add(Text("Secure control from your Android phone.", 13, Muted, 6));
        pairRow.Children.Add(pairCopy);
        var pairButton = Action("Pair a phone", (_, _) => ShowPairing(), primary: true);
        Grid.SetColumn(pairButton, 1); pairRow.Children.Add(pairButton); cards.Children.Add(Card(pairRow));
        var devices = new StackPanel();
        var deviceHeader = new DockPanel();
        DockPanel.SetDock(deviceCount, Dock.Right); deviceHeader.Children.Add(deviceCount);
        deviceHeader.Children.Add(Section("YOUR PHONES")); devices.Children.Add(deviceHeader);
        emptyDevices.Margin = new Thickness(0, 14, 0, 12); devices.Children.Add(emptyDevices);
        deviceList.Margin = new Thickness(0, 10, 0, 10);
        AutomationProperties.SetName(deviceList, "Paired Android phones"); devices.Children.Add(deviceList);
        revoke = Action("Revoke selected phone", (_, _) => RevokeSelected()); revoke.IsEnabled = false;
        deviceList.SelectionChanged += (_, _) => revoke.IsEnabled = deviceList.SelectedItem is PairedDevice;
        devices.Children.Add(revoke); cards.Children.Add(Card(devices));
        var network = new StackPanel();
        network.Children.Add(Section("THIS COMPUTER"));
        var addresses = Addresses(settings.Port);
        network.Children.Add(CopyBox(addresses.FirstOrDefault() ?? "No private network address available", "Preferred local address", 12));
        network.Children.Add(Text("Nearby discovery is automatic. Use this address for a manual connection.", 12, Muted, 8));
        if (addresses.Length > 1)
        {
            var other = new Expander { Header = "Other local addresses", Margin = new Thickness(0, 12, 0, 0), Foreground = Muted };
            other.Content = CopyBox(string.Join("\n", addresses.Skip(1)), "Other local addresses", 8); network.Children.Add(other);
        }
        cards.Children.Add(Card(network));
        var preferences = new Expander { Header = "Preferences", Foreground = Ivory };
        var options = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var startup = new CheckBox { Content = "Start after Windows sign-in", IsChecked = settings.Startup, Foreground = Ivory, Margin = new Thickness(0, 0, 0, 16), MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
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
        };
        options.Children.Add(startup); options.Children.Add(Text("Connection port", 13, Muted));
        var portRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var port = new TextBox { Text = settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 88, Margin = new Thickness(0, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(port, "Connection port, 1024 to 65535"); portRow.Children.Add(port);
        portRow.Children.Add(Action("Save port", (_, _) =>
        {
            if (!int.TryParse(port.Text, out var number) || number is < 1024 or > 65535) { SetStatus("Choose a port from 1024 to 65535.", true); return; }
            try { var updated = this.settings with { Port = number }; storage.Save("settings.json", updated); this.settings = updated; SetStatus("Port saved. Exit and reopen Bedside Remote to apply.", false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("Could not save the port.", true); }
        }));
        options.Children.Add(portRow); options.Children.Add(Text("A port change takes effect after restarting the agent.", 12, Muted, 8));
        preferences.Content = options; cards.Children.Add(Card(preferences));
        var scroll = new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var releases = Action("Check releases", (_, _) => Program.OpenRelease()); releases.Padding = new Thickness(12, 6, 12, 6); releases.MinHeight = 34;
        DockPanel.SetDock(releases, Dock.Right); footer.Children.Add(releases);
        footer.Children.Add(Text("Close to keep running in the system tray.", 11, Muted, 8));
        Grid.SetRow(footer, 2); layout.Children.Add(footer); Content = layout;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Bedside Remote", null, (_, _) => Dispatcher.Invoke(OpenWindow));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Exit));
        tray = new System.Windows.Forms.NotifyIcon { Icon = trayIcon, Text = "Bedside Remote", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenWindow);
        Closing += (_, e) => { if (!exiting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { tray.Dispose(); trayIcon.Dispose(); };
        SourceInitialized += (_, _) => { DarkTitleBar(this); FitWindow(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(FitWindow);
        server.Approve = ApproveAsync; server.DevicesChanged += () => Dispatcher.BeginInvoke(RefreshDevices); RefreshDevices();
        if (SystemParameters.ClientAreaAnimation) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }
    public void SetStatus(string message, bool error) => Dispatcher.Invoke(() => { status.Text = message; status.Foreground = error ? Brush("#FFB4AB") : Brass; });
    private void RefreshDevices()
    {
        var selected = (deviceList.SelectedItem as PairedDevice)?.DeviceId;
        var devices = server.Devices; deviceList.ItemsSource = devices; deviceList.SelectedItem = devices.FirstOrDefault(device => device.DeviceId == selected);
        deviceList.Visibility = devices.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        emptyDevices.Visibility = devices.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        deviceCount.Text = devices.Length == 1 ? "1 phone" : $"{devices.Length} phones";
    }
    internal void OpenWindow() { if (exiting) return; Show(); WindowState = WindowState.Normal; FitWindow(); Activate(); }
    private void FitWindow()
    {
        var pixels = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var origin = transform.Transform(new System.Windows.Point(pixels.Left, pixels.Top));
        var size = transform.Transform(new System.Windows.Point(pixels.Width, pixels.Height));
        var work = new Rect(origin.X, origin.Y, size.X, size.Y);
        var bounds = FitBounds(work, Width, Height);
        MinWidth = Math.Min(400, bounds.Width); MinHeight = Math.Min(360, bounds.Height); Width = bounds.Width; Height = bounds.Height;
        Left = double.IsNaN(Left) ? bounds.Left : Math.Clamp(Left, work.Left + 16, work.Right - 16 - Width);
        Top = double.IsNaN(Top) ? bounds.Top : Math.Clamp(Top, work.Top + 16, work.Bottom - 16 - Height);
    }
    internal static Rect FitBounds(Rect workArea, double width, double height)
    {
        var w = Math.Min(width, Math.Max(1, workArea.Width - 32)); var h = Math.Min(height, Math.Max(1, workArea.Height - 32));
        return new Rect(workArea.Left + (workArea.Width - w) / 2, workArea.Top + (workArea.Height - h) / 2, w, h);
    }
    private void Exit() { if (exiting) return; exiting = true; pairingDialog?.Close(); tray.Visible = false; tray.Dispose(); ExitRequested?.Invoke(this, EventArgs.Empty); }
    private void RevokeSelected()
    {
        if (deviceList.SelectedItem is PairedDevice device && System.Windows.MessageBox.Show(this, $"Revoke {device.DeviceName}? Its active connections will close immediately.", "Revoke phone", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try { server.Revoke(device.DeviceId); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("Could not save revocation. Check access to local app data.", true); }
        }
    }
    private void ShowPairing()
    {
        if (pairingDialog != null) { pairingDialog.Activate(); return; }
        var code = server.Pairing.Open(); var body = new StackPanel();
        body.Children.Add(Text("Verify this PC first.", 25, Ivory));
        body.Children.Add(Text("Compare the complete fingerprint on Android before sending the pairing code.", 14, Muted, 12));
        body.Children.Add(CopyBox(fingerprint, "SHA-256 certificate fingerprint", 16));
        body.Children.Add(Section("PAIRING CODE · VALID FOR FIVE MINUTES", 24));
        body.Children.Add(new TextBlock { Text = code, FontFamily = DisplayFont, FontSize = 42, Foreground = Brass, Margin = new Thickness(0, 8, 0, 12) });
        body.Children.Add(Text("Keep this window open, then approve your phone's request on this computer.", 14, Muted));
        var dialog = Dialog("Pair a phone", body, 520); pairingDialog = dialog;
        body.Children.Add(Action("Close pairing", (_, _) => dialog.Close(), top: 20));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        timer.Tick += (_, _) => dialog.Close(); timer.Start();
        dialog.Closed += (_, _) => { timer.Stop(); server.Pairing.Close(); pairingDialog = null; }; dialog.Show();
    }
    private Task<bool> ApproveAsync(string name, CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.BeginInvoke(() =>
        {
            if (token.IsCancellationRequested || exiting) { completion.TrySetResult(false); return; }
            OpenWindow(); var body = new StackPanel();
            body.Children.Add(Text("Allow desktop control?", 25, Ivory));
            body.Children.Add(Text($"{name} is requesting mouse, keyboard and media control. Approve only if you started this pairing.", 15, Muted, 12));
            var dialog = Dialog("Approve phone", body, 480); var approved = false;
            body.Children.Add(Action("Approve phone", (_, _) => { approved = true; dialog.Close(); }, primary: true, top: 24));
            body.Children.Add(Action("Deny", (_, _) => dialog.Close(), top: 8));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
            timer.Tick += (_, _) => dialog.Close(); timer.Start();
            var registration = token.Register(() => Dispatcher.BeginInvoke(() => dialog.Close()));
            dialog.Closed += (_, _) => { timer.Stop(); registration.Dispose(); completion.TrySetResult(approved && !token.IsCancellationRequested && !exiting); };
            dialog.Show(); dialog.Activate();
        });
        return completion.Task;
    }
    private Window Dialog(string title, FrameworkElement body, double width)
    {
        var dialog = new Window { Title = title, Owner = this, Width = Math.Min(width, SystemParameters.WorkArea.Width - 32), MaxHeight = SystemParameters.WorkArea.Height - 32, SizeToContent = SizeToContent.Height, Background = Canvas, Foreground = Ivory, FontFamily = BodyFont, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize };
        dialog.Resources = Styles();
        dialog.Content = new ScrollViewer { Content = body, Margin = new Thickness(28), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.SourceInitialized += (_, _) => DarkTitleBar(dialog); return dialog;
    }
    private static Border Card(UIElement child) => new() { Child = child, Background = Surface, CornerRadius = new CornerRadius(22), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 12) };
    private static TextBlock Text(string text, double size, System.Windows.Media.Brush color, double top = 0) => new() { Text = text, FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
    private static TextBlock Section(string text, double top = 0) => Text(text, 11, Brass, top);
    private static TextBox CopyBox(string text, string name, double top = 0)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0), FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        AutomationProperties.SetName(box, name); return box;
    }
    private static Button Action(string text, RoutedEventHandler handler, bool primary = false, double top = 0)
    {
        var button = new Button { Content = text, Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, top, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, MinHeight = 42 };
        if (primary) { button.Background = Brass; button.Foreground = Brush("#272014"); button.BorderBrush = Brass; }
        button.Click += handler; return button;
    }
    private static string[] Addresses(int port) => NetworkInterface.GetAllNetworkInterfaces().Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
        .OrderByDescending(adapter => adapter.GetIPProperties().GatewayAddresses.Count != 0)
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses).Select(item => item.Address)
        .Where(address => Protocol.IsLan(address) && !address.IsIPv6LinkLocal && !System.Net.IPAddress.IsLoopback(address)).Distinct()
        .Select(address => $"{(address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + address + "]" : address.ToString())}:{port}").ToArray();
    private static SolidColorBrush Brush(string hex) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; }
    private static ResourceDictionary Styles() => (ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="Button">
            <Setter Property="Background" Value="#24272B"/><Setter Property="Foreground" Value="#F3EEE6"/><Setter Property="BorderBrush" Value="#383A3D"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="Plate" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="12" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Plate" Property="Opacity" Value="0.85"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="Plate" Property="Opacity" Value="0.65"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Plate" Property="BorderBrush" Value="#D8B57A"/><Setter TargetName="Plate" Property="BorderThickness" Value="2"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Plate" Property="Opacity" Value="0.4"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="TextBox">
            <Setter Property="Background" Value="#24272B"/><Setter Property="Foreground" Value="#F3EEE6"/><Setter Property="BorderBrush" Value="#383A3D"/><Setter Property="CaretBrush" Value="#D8B57A"/><Setter Property="SelectionBrush" Value="#79571F"/><Setter Property="Padding" Value="12,10"/><Setter Property="MinHeight" Value="40"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border x:Name="Plate" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="10"><ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}"/></Border><ControlTemplate.Triggers><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Plate" Property="BorderBrush" Value="#D8B57A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ListBoxItem">
            <Setter Property="AutomationProperties.Name" Value="{Binding DeviceName}"/>
            <Setter Property="Padding" Value="12"/><Setter Property="Margin" Value="0,2"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="Plate" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" CornerRadius="10" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="Plate" Property="Background" Value="#403420"/><Setter TargetName="Plate" Property="BorderBrush" Value="#D8B57A"/></Trigger><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Plate" Property="BorderBrush" Value="#D8B57A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="Expander">
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Expander"><StackPanel>
              <ToggleButton x:Name="Header" Content="{TemplateBinding Header}" Foreground="{TemplateBinding Foreground}" HorizontalContentAlignment="Left" IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border x:Name="Focus" BorderBrush="Transparent" BorderThickness="1" CornerRadius="8" Padding="0,6"><DockPanel><TextBlock Text="›" Foreground="#D8B57A" FontSize="20" Width="22" VerticalAlignment="Center"/><ContentPresenter Content="{TemplateBinding Content}" VerticalAlignment="Center"/></DockPanel></Border><ControlTemplate.Triggers><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Focus" Property="BorderBrush" Value="#D8B57A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
              </ToggleButton>
              <ContentPresenter x:Name="Body" Visibility="Collapsed"/>
            </StackPanel><ControlTemplate.Triggers><Trigger Property="IsExpanded" Value="True"><Setter TargetName="Body" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="CheckBox">
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><Border x:Name="Focus" BorderBrush="Transparent" BorderThickness="1" CornerRadius="8" Padding="2,6"><DockPanel><Border BorderBrush="#C0BDB7" BorderThickness="1" Background="#24272B" CornerRadius="4" Width="18" Height="18" Margin="0,0,10,0"><Path x:Name="Tick" Data="M 3,8 L 7,12 L 14,4" Stroke="#D8B57A" StrokeThickness="2" Visibility="Collapsed"/></Border><ContentPresenter VerticalAlignment="Center"/></DockPanel></Border><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Tick" Property="Visibility" Value="Visible"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Focus" Property="BorderBrush" Value="#D8B57A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ScrollBar"><Setter Property="Width" Value="8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="{TemplateBinding Orientation}"><Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb MinHeight="28" Background="#50504D"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{TemplateBinding Background}" CornerRadius="4"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></ControlTemplate></Setter.Value></Setter></Style>
        </ResourceDictionary>
        """);
    private static void DarkTitleBar(Window window) { var enabled = 1; _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref enabled, sizeof(int)); }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
