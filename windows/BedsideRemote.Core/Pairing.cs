using System.Security.Cryptography;
using System.Text;

namespace BedsideRemote.Core;

public sealed record PairedDevice(string DeviceId, string DeviceName, string TokenHash, DateTimeOffset PairedAt);

public sealed class PairingWindow(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly object sync = new();
    private string? code;
    private DateTimeOffset expires;
    private int attempts;
    public string Open()
    {
        lock (sync)
        {
            code = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            expires = clock.GetUtcNow().AddMinutes(5); attempts = 0; return code;
        }
    }
    public void Close() { lock (sync) code = null; }
    public bool TryCode(string candidate)
    {
        lock (sync)
        {
            if (code == null || clock.GetUtcNow() >= expires || ++attempts > 10) return false;
            return candidate.Length == 6 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(candidate));
        }
    }
    public static (PairedDevice Device, string Token) CreateDevice(string name)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return (new(Guid.NewGuid().ToString(), name, Hash(token), DateTimeOffset.UtcNow), token);
    }
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static bool Matches(PairedDevice device, string token)
    {
        if (token.Length != 44) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(device.TokenHash), Convert.FromHexString(Hash(token)));
    }
}
