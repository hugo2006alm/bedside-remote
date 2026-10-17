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
        if (args.Length != 0) return 2;
        using var mutex = new Mutex(true, "Local\\BedsideRemote-" + WindowsIdentity.GetCurrent().User!.Value, out var created);
        if (!created) return 0;
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            var storage = new Storage(); var settings = storage.Read<Settings>("settings.json") ?? new();
            var engine = new Engine(storage); var server = new RemoteServer(storage, engine);
            using var certificate = storage.Certificate();
            var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
            using var cancellation = new CancellationTokenSource();
            var window = new MainWindow(storage, settings, server, fingerprint);
            MdnsAdvertiser? discovery = null;
            window.Loaded += async (_, _) =>
            {
                try
                {
                    await server.StartAsync(settings.Port, certificate, cancellation.Token);
                    window.SetStatus("Ready · encrypted local control", false);
                    _ = RunNativeListenerAsync(new NativeBridge(engine), window, cancellation.Token);
                    try
                    {
                        discovery = new MdnsAdvertiser(new ServiceProfile(Environment.MachineName, "_bedside-remote._tcp", (ushort)settings.Port, new Dictionary<string, string> { ["version"] = "1" }));
                        discovery.Start();
                    }
                    catch (Exception ex) when (ex is System.Net.Sockets.SocketException or InvalidOperationException) { window.SetStatus("Ready · automatic discovery unavailable; use a manual address", false); }
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Net.Sockets.SocketException) { window.SetStatus("Could not start: " + ex.Message, true); }
            };
            window.ExitRequested += async (_, _) =>
            {
                cancellation.Cancel(); discovery?.Dispose();
                await server.StopAsync(); application.Shutdown();
            };
            application.Run(window);
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
