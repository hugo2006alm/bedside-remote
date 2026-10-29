using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Windows;
using BedsideRemote.Core;
using Haukcode.Mdns;

namespace BedsideRemote.Agent;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (NativeBridge.IsNativeInvocation(args)) return NativeBridge.RunHostAsync(args).GetAwaiter().GetResult();
        if (!WindowsStartup.IsValidInvocation(args)) return 2;
        var user = WindowsIdentity.GetCurrent().User!.Value;
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\BedsideRemote-Activate-" + user);
        using var mutex = new Mutex(true, "Local\\BedsideRemote-" + user, out var created);
        if (!created) { if (!WindowsStartup.IsStartup(args)) activation.Set(); return 0; }
        // This lightweight tray UI must also render reliably with remote sessions and GPU drivers.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            var storage = new Storage(); var settings = storage.Read<Settings>("settings.json") ?? new();
            var engine = new Engine(storage); var server = new RemoteServer(storage, engine);
            using var certificate = storage.Certificate();
            var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
            using var cancellation = new CancellationTokenSource();
            var window = new MainWindow(storage, settings, server, fingerprint);
            var activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => window.Dispatcher.BeginInvoke(window.OpenWindow), null, Timeout.Infinite, false);
            MdnsAdvertiser? discovery = null;
            application.Startup += async (_, _) =>
            {
                if (!WindowsStartup.ShouldStartHidden(args, settings)) window.OpenWindow();
                try { WindowsStartup.RefreshExistingRegistration(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { window.SetStatus("Could not refresh Windows startup registration. Check Preferences.", true); }
                try
                {
                    await server.StartAsync(settings.Port, certificate, cancellation.Token);
                    window.SetStatus("Ready · encrypted local control", false);
                    _ = RunNativeListenerAsync(new NativeBridge(engine), window, cancellation.Token);
                    try
                    {
                        discovery = new MdnsAdvertiser(new ServiceProfile(Environment.MachineName, "_bedside-remote._tcp", (ushort)settings.Port, new Dictionary<string, string> { ["version"] = "1", ["addresses"] = DiscoveryAddresses.ReadLocal() }));
                        discovery.Start();
                    }
                    catch (Exception ex) when (ex is System.Net.Sockets.SocketException or InvalidOperationException) { window.SetStatus("Ready · automatic discovery unavailable; use a manual address", false); }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Net.Sockets.SocketException) { window.SetStatus("Could not start: " + ex.Message, true); }
            };
            window.ExitRequested += async (_, _) =>
            {
                cancellation.Cancel(); discovery?.Dispose();
                await server.StopAsync(); application.Shutdown();
            };
            try { application.Run(); }
            finally { activationWait.Unregister(null); }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or FormatException)
        {
            System.Windows.MessageBox.Show("Bedside Remote could not open its local data. Your existing data has been preserved.\n\n" + ex.Message, "Bedside Remote", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        finally { mutex.ReleaseMutex(); }
    }
    private static async Task RunNativeListenerAsync(NativeBridge bridge, MainWindow window, CancellationToken token)
    {
        try { await bridge.ListenAsync(token); }
        catch (OperationCanceledException) { }
        catch (IOException) { window.SetStatus("Native browser integration unavailable. Restart Bedside Remote.", true); }
    }
    public static void OpenRelease() => Process.Start(new ProcessStartInfo("https://github.com/hugo2006alm/bedside-remote/releases/latest") { UseShellExecute = true });
}
