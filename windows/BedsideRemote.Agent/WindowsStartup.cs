using Microsoft.Win32;

namespace BedsideRemote.Agent;

public static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BedsideRemote";

    public static bool IsStartup(string[] args) => args is ["--startup"];
    public static bool IsValidInvocation(string[] args) => args.Length == 0 || IsStartup(args);
    public static bool ShouldStartHidden(string[] args, Settings settings) => IsStartup(args) && settings.StartMinimized;

    public static void Save(Storage storage, Settings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        Save(storage, settings, key, Environment.ProcessPath!);
    }

    // The supplied key lets integration tests exercise registration and rollback
    // without changing the user's real login entry.
    public static void Save(Storage storage, Settings settings, RegistryKey key, string executable)
    {
        var previous = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var previousKind = previous is null ? RegistryValueKind.String : key.GetValueKind(ValueName);
        try
        {
            if (settings.Startup) key.SetValue(ValueName, Command(executable), RegistryValueKind.String);
            else key.DeleteValue(ValueName, false);
            storage.Save("settings.json", settings);
        }
        catch
        {
            if (previous is null) key.DeleteValue(ValueName, false);
            else key.SetValue(ValueName, previous, previousKind);
            throw;
        }
    }

    public static void RefreshExistingRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is not null)
            key.SetValue(ValueName, Command(Environment.ProcessPath!), RegistryValueKind.String);
    }

    private static string Command(string executable) => "\"" + executable + "\" --startup";
}
