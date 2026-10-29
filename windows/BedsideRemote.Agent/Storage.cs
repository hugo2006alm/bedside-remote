using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace BedsideRemote.Agent;

public sealed class Storage
{
    public string Root { get; }
    public Storage(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BedsideRemote");
        Directory.CreateDirectory(Root);
    }
    public T? Read<T>(string file, bool secret = false)
    {
        var path = Path.Combine(Root, file);
        if (!File.Exists(path)) return default;
        var bytes = File.ReadAllBytes(path);
        return JsonSerializer.Deserialize<T>(secret ? Protect(bytes, false) : bytes, Core.Protocol.Json);
    }
    public void Save<T>(string file, T value, bool secret = false)
    {
        var path = Path.Combine(Root, file);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Core.Protocol.Json);
        File.WriteAllBytes(path + ".tmp", secret ? Protect(bytes, true) : bytes);
        File.Move(path + ".tmp", path, true);
    }
    public X509Certificate2 Certificate()
    {
        var path = Path.Combine(Root, "identity.dpapi");
        // Schannel needs a current-user key container; the durable PFX remains DPAPI-protected.
        if (File.Exists(path)) return X509CertificateLoader.LoadPkcs12(Protect(File.ReadAllBytes(path), false), null, X509KeyStorageFlags.UserKeySet);
        using var key = RSA.Create(3072);
        var request = new CertificateRequest("CN=Bedside Remote", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var usages = new OidCollection { new("1.3.6.1.5.5.7.3.1") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        var pfx = certificate.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, Protect(pfx, true));
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob data, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob data, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            Blob output;
            var ok = encrypt ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
}

public sealed record Settings(int Port = 8765, bool Startup = false, bool StartMinimized = false);
