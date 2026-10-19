using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BedsideRemote.Agent;

public sealed record MediaApp(string AppId, string Name, string Kind);
public sealed class MediaApps(Action<ProcessStartInfo>? start = null)
{
    private readonly object sync = new();
    private readonly Dictionary<string, (MediaApp App, string Target)> available = [];
    private DateTime refreshed;
    public MediaApp[] List()
    {
        lock (sync)
        {
            if (DateTime.UtcNow - refreshed < TimeSpan.FromSeconds(30)) return available.Values.Select(value => value.App).ToArray();
            available.Clear();
            Add("youtube", "YouTube", "https://www.youtube.com/", "browser");
            Add("netflix", "Netflix", "https://www.netflix.com/", "browser");
            ReadStoreApps();
            Find("spotify", "Spotify", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe"));
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var folder in new[] { "Programs/LNV/Stremio-4", "Programs/LNV/Stremio-5", "Programs/Stremio", "Programs/stremio", "Smart Code ltd/Stremio" })
                Find("stremio", "Stremio", Path.Combine(local, folder, "stremio.exe"));
            FindRegistered("stremio", "Stremio", "stremio.exe");
            FindRegistered("spotify", "Spotify", "Spotify.exe");
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                Find("vlc", "VLC", Path.Combine(Environment.GetFolderPath(folder), "VideoLAN", "VLC", "vlc.exe"));
            refreshed = DateTime.UtcNow;
            return available.Values.Select(value => value.App).ToArray();
        }
    }
    private void Add(string id, string name, string target, string kind = "app") => available[id] = (new(id, name, kind), target);
    private void Find(string id, string name, string path)
    {
        if (!available.ContainsKey(id) && File.Exists(path)) Add(id, name, Path.GetFullPath(path));
    }
    private void FindRegistered(string id, string name, string executable)
    {
        if (available.ContainsKey(id)) return;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + executable);
            if (key?.GetValue(null) is not string path) continue;
            path = path.Trim('"');
            if (Path.IsPathFullyQualified(path) && Path.GetFileName(path).Equals(executable, StringComparison.OrdinalIgnoreCase)) Find(id, name, path);
        }
    }
    private void ReadStoreApps()
    {
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) return;
            shell = Activator.CreateInstance(type);
            dynamic folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            if (folder == null) return;
            foreach (dynamic item in folder.Items())
            {
                string id = item.Path;
                if (id == "4DF9E0F8.Netflix_mcm4njqhnhss8!Netflix") Add("netflix", "Netflix", "shell:AppsFolder\\" + id);
                if (id == "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify") Add("spotify", "Spotify", "shell:AppsFolder\\" + id);
            }
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidOperationException) { }
        finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
    }
    public void Launch(string id)
    {
        _ = List();
        string target;
        lock (sync) target = available.TryGetValue(id, out var value) ? value.Target : throw new InvalidOperationException("This app is not available on the connected computer.");
        var process = target.StartsWith("shell:AppsFolder\\", StringComparison.Ordinal)
            ? new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), target) { UseShellExecute = true }
            : new ProcessStartInfo(target) { UseShellExecute = true };
        if (start != null) start(process);
        else Process.Start(process);
    }
}
