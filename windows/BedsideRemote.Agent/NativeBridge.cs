using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using BedsideRemote.Core;

namespace BedsideRemote.Agent;

public sealed class NativeConnection(string id, Stream stream) : IDisposable
{
    public string Id { get; } = id;
    public JsonElement[] Tabs { get; set; } = [];
    public int? ActiveTabId { get; set; }
    public bool HasState { get; set; }
    private readonly SemaphoreSlim sendLock = new(1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
    public async Task<JsonElement> CommandAsync(Dictionary<string, object?> message, CancellationToken cancellationToken)
    {
        var commandId = Guid.NewGuid().ToString();
        message["version"] = 1; message["type"] = "browser.command"; message["id"] = commandId;
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[commandId] = completion;
        try
        {
            await sendLock.WaitAsync(cancellationToken);
            try { await NativeFrames.WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(message, Protocol.Json), cancellationToken); }
            finally { sendLock.Release(); }
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        finally { pending.TryRemove(commandId, out _); }
    }
    public void Complete(JsonElement result)
    {
        if (pending.TryGetValue(result.GetProperty("id").GetString()!, out var completion)) completion.TrySetResult(result);
    }
    public void Dispose()
    {
        foreach (var item in pending.Values) item.TrySetException(new IOException("Browser disconnected."));
        stream.Dispose();
    }
}

public sealed class NativeBridge(Engine engine)
{
    private readonly SemaphoreSlim connectionSlots = new(8);
    public const string ExtensionId = "bedside-remote@hugo2006alm";
    public static string PipeName => "BedsideRemote-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User!.Value)))[..24];
    public async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await connectionSlots.WaitAsync(cancellationToken);
            NamedPipeServerStream pipe;
            try { pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); }
            catch { connectionSlots.Release(); throw; }
            try { await pipe.WaitForConnectionAsync(cancellationToken); }
            catch { pipe.Dispose(); connectionSlots.Release(); throw; }
            _ = HandleAsync(pipe, cancellationToken);
        }
    }
    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var connection = new NativeConnection(Guid.NewGuid().ToString("N"), pipe);
        engine.AddNative(connection);
        try
        {
            while (await NativeFrames.ReadAsync(pipe, cancellationToken) is { } bytes)
            {
                var message = NativeProtocol.Parse(bytes);
                if (message.GetProperty("type").GetString() == "browser.result") connection.Complete(message);
                else await engine.UpdateNativeAsync(connection, message, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OperationCanceledException) { }
        finally { engine.RemoveNative(connection.Id); connection.Dispose(); connectionSlots.Release(); }
    }
    public static bool IsNativeInvocation(string[] args) => args.Contains("--native", StringComparer.Ordinal) || (args.Length >= 2 && args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    public static async Task<int> RunHostAsync(string[] args)
    {
        // Firefox passes the full manifest path followed by the add-on ID. --native is also supported for diagnostic launchers.
        if (args.Length != 2 || args[1] != ExtensionId || (args[0] != "--native" && !Path.IsPathFullyQualified(args[0]))) return 2;
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000);
            using var cancellation = new CancellationTokenSource();
            var input = Console.OpenStandardInput(); var output = Console.OpenStandardOutput();
            var toAgent = Pump(input, pipe, cancellation.Token); var toBrowser = Pump(pipe, output, cancellation.Token);
            await Task.WhenAny(toAgent, toBrowser);
            cancellation.Cancel();
            try { await Task.WhenAll(toAgent, toBrowser); } catch (OperationCanceledException) { }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException) { return 1; }
    }
    private static async Task Pump(Stream source, Stream target, CancellationToken token)
    {
        while (await NativeFrames.ReadAsync(source, token) is { } bytes) await NativeFrames.WriteAsync(target, bytes, token);
    }
}
