using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using BedsideRemote.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BedsideRemote.Agent;

public sealed class RemoteServer
{
    private readonly Storage storage;
    private readonly Engine engine;
    private readonly object deviceLock = new();
    private readonly List<PairedDevice> devices;
    private readonly ConcurrentDictionary<string, Client> clients = new();
    private readonly SemaphoreSlim approvalLock = new(1);
    private readonly SemaphoreSlim browserAdmission = new(1);
    private readonly HashSet<Guid> recentIds = [];
    private readonly Queue<Guid> recentOrder = new();
    private WebApplication? app;
    private readonly CancellationTokenSource shutdown = new();
    private readonly object lifecycleLock = new();
    private Task? stopTask;
    private int stopping;
    public PairingWindow Pairing { get; } = new();
    public Func<string, CancellationToken, Task<bool>>? Approve { get; set; }
    public event Action? DevicesChanged;
    public int ActiveConnections => clients.Count;
    internal Func<Guid, CancellationToken, Task>? AfterAcknowledgementSent { get; set; }
    public RemoteServer(Storage storage, Engine engine)
    {
        this.storage = storage; this.engine = engine;
        devices = storage.Read<List<PairedDevice>>("devices.dpapi", true) ?? [];
        engine.Changed += Broadcast;
    }
    public PairedDevice[] Devices { get { lock (deviceLock) return devices.ToArray(); } }
    public async Task StartAsync(int port, X509Certificate2 certificate, CancellationToken token)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 4096;
            options.Limits.MaxConcurrentConnections = 32;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.ListenAnyIP(port, listen => listen.UseHttps(certificate));
        });
        app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (Volatile.Read(ref stopping) != 0) { context.Response.StatusCode = 503; return; }
            using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, shutdown.Token);
            context.RequestAborted = requestLifetime.Token;
            if (!Protocol.IsLan(context.Connection.RemoteIpAddress) || context.Request.Headers.ContainsKey("Origin")) { context.Response.StatusCode = 403; return; }
            context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20), KeepAliveTimeout = TimeSpan.FromSeconds(15) });
        app.MapGet("/health", () => new { version = 1, name = Environment.MachineName });
        app.MapGet("/apps", async context =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            lock (deviceLock)
                if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || !devices.Any(device => PairingWindow.Matches(device, authorization[7..])))
                { context.Response.StatusCode = 401; return; }
            await context.Response.WriteAsJsonAsync(new { version = 1, controls = new[] { "drag", "desktopNavigation" }, apps = engine.Apps.List() }, Protocol.Json, context.RequestAborted);
        });
        app.MapPost("/pair", PairAsync);
        app.MapGet("/ws", ConnectAsync);
        await app.StartAsync(token);
    }
    public Task StopAsync() { lock (lifecycleLock) return stopTask ??= StopCoreAsync(); }
    private async Task StopCoreAsync()
    {
        Interlocked.Exchange(ref stopping, 1);
        Pairing.Close();
        shutdown.Cancel();
        foreach (var client in clients.Values) client.Socket.Abort();
        if (app != null) { await app.StopAsync(); await app.DisposeAsync(); }
    }
    private async Task PairAsync(HttpContext context)
    {
        if (!await approvalLock.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 429; return; }
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, new JsonDocumentOptions { MaxDepth = 4 }, context.RequestAborted);
            var root = document.RootElement;
            Protocol.Fields(root, "version", "deviceName", "code");
            var name = Protocol.Text(root, "deviceName", 80);
            if (name.Any(char.IsControl) || root.GetProperty("version").GetInt32() != 1) throw new FormatException();
            var code = Protocol.Text(root, "code", 6);
            if (!Pairing.TryCode(code)) { context.Response.StatusCode = 403; return; }
            if (Approve == null || !await Approve(name, context.RequestAborted) || !Pairing.TryCode(code)) { context.Response.StatusCode = 403; return; }
            var created = PairingWindow.CreateDevice(name);
            lock (deviceLock)
            {
                if (devices.Count >= 20) { context.Response.StatusCode = 409; return; }
                devices.Add(created.Device); storage.Save("devices.dpapi", devices, true);
            }
            DevicesChanged?.Invoke();
            await context.Response.WriteAsJsonAsync(new { version = 1, deviceId = created.Device.DeviceId, token = created.Token, deviceName = Environment.MachineName }, Protocol.Json, context.RequestAborted);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or BadHttpRequestException) { context.Response.StatusCode = 400; }
        finally { approvalLock.Release(); }
    }
    private async Task ConnectAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        var authorization = context.Request.Headers.Authorization.ToString();
        PairedDevice? device = null;
        if (authorization.StartsWith("Bearer ", StringComparison.Ordinal))
            lock (deviceLock) device = devices.Find(x => PairingWindow.Matches(x, authorization[7..]));
        if (device == null) { context.Response.StatusCode = 401; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var client = new Client(device.DeviceId, socket);
        var connectionId = Guid.NewGuid().ToString(); clients[connectionId] = client;
        lock (deviceLock)
            if (Volatile.Read(ref stopping) != 0 || !devices.Any(d => d.DeviceId == device.DeviceId)) { clients.TryRemove(connectionId, out _); socket.Abort(); return; }
        using var connectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        Task pendingBrowser = Task.CompletedTask;
        Task pendingText = Task.CompletedTask;
        var closeStatus = WebSocketCloseStatus.NormalClosure;
        var closeReason = "Connection ended.";
        try
        {
            await client.SendAsync(engine.Snapshot(), context.RequestAborted);
            var replay = new ReplayGuard(); var buffer = new byte[Protocol.MaxBytes];
            var period = DateTimeOffset.UtcNow; var count = 0;
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var length = 0; WebSocketReceiveResult frame;
                do
                {
                    frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), context.RequestAborted);
                    if (frame.MessageType == WebSocketMessageType.Close) return;
                    if (frame.MessageType != WebSocketMessageType.Text || length + frame.Count >= buffer.Length && !frame.EndOfMessage)
                        throw new InvalidDataException("Invalid message type or size.");
                    length += frame.Count;
                } while (!frame.EndOfMessage);
                if (DateTimeOffset.UtcNow - period > TimeSpan.FromSeconds(1)) { period = DateTimeOffset.UtcNow; count = 0; }
                if (++count > 150) throw new CommandRateLimitException();
                Command command;
                try { command = Protocol.ParseCommand(buffer.AsMemory(0, length)); }
                catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException) { throw new InvalidDataException("Invalid command."); }
                try
                {
                    lock (deviceLock)
                    {
                        if (!devices.Any(d => d.DeviceId == device.DeviceId)) throw new InvalidOperationException("Device was revoked.");
                        if (!replay.Accept(command) || !recentIds.Add(command.Id)) throw new InvalidOperationException("Command replay was rejected.");
                        recentOrder.Enqueue(command.Id);
                        if (recentOrder.Count > 8192) recentIds.Remove(recentOrder.Dequeue());
                    }
                }
                catch (InvalidOperationException ex) { await AcknowledgeAsync(client, command.Id, false, ex.Message, context.RequestAborted); continue; }
                if (command.Action == "input.text")
                {
                    if (!pendingText.IsCompleted) await AcknowledgeAsync(client, command.Id, false, "Text input is busy; this action was not queued.", connectionLifetime.Token);
                    else pendingText = DispatchTextAsync(client, command, connectionLifetime.Token);
                }
                else if (Engine.IsImmediate(command)) await ExecuteAndAcknowledgeAsync(client, command, connectionLifetime.Token);
                else if (!browserAdmission.Wait(0)) await AcknowledgeAsync(client, command.Id, false, "Browser is busy; wait for the current action and try again.", connectionLifetime.Token);
                else pendingBrowser = DispatchBrowserAsync(client, command, connectionLifetime.Token);
            }
        }
        catch (CommandRateLimitException) { closeStatus = (WebSocketCloseStatus)1013; closeReason = "Command rate limit exceeded; reconnect and reduce input rate."; }
        catch (InvalidDataException) { closeStatus = WebSocketCloseStatus.ProtocolError; closeReason = "Invalid protocol message."; }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException) { }
        finally
        {
            connectionLifetime.Cancel();
            await pendingBrowser;
            await pendingText;
            clients.TryRemove(connectionId, out _);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await socket.CloseOutputAsync(closeStatus, closeReason, closeTimeout.Token); } catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
            }
        }
    }
    private async Task DispatchTextAsync(Client client, Command command, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(300));
        try
        {
            (bool Ok, string? Error) result;
            try { result = await ExecuteAsync(client, command, deadline.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { result = (false, "Text input timed out. Some text may already have been entered; check the document before retrying."); }
            await AcknowledgeAsync(client, command.Id, result.Ok, result.Error, token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException or IOException) { }
    }
    private async Task DispatchBrowserAsync(Client client, Command command, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            (bool Ok, string? Error) result;
            try
            {
                try { result = await ExecuteAsync(client, command, deadline.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { result = (false, "Browser operation timed out. Check playback before trying again."); }
            }
            finally
            {
                // An observable completion ACK must mean that the next operation can be admitted.
                browserAdmission.Release();
            }
            await AcknowledgeAsync(client, command.Id, result.Ok, result.Error, token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException or IOException) { }
    }
    private async Task ExecuteAndAcknowledgeAsync(Client client, Command command, CancellationToken token)
    {
        var result = await ExecuteAsync(client, command, token);
        await AcknowledgeAsync(client, command.Id, result.Ok, result.Error, token);
    }
    private async Task<(bool Ok, string? Error)> ExecuteAsync(Client client, Command command, CancellationToken token)
    {
        var ok = false; string? error = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref stopping) != 0) throw new OperationCanceledException(token);
            lock (deviceLock)
                if (!devices.Any(x => x.DeviceId == client.DeviceId)) throw new InvalidOperationException("Device was revoked.");
            await engine.ExecuteAsync(command, token); ok = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { error = ex.Message; }
        return (ok, error);
    }
    private async Task AcknowledgeAsync(Client client, Guid id, bool ok, string? error, CancellationToken token)
    {
        await client.SendAsync(new { version = 1, type = "ack", id = id.ToString(), ok, error }, token);
        if (AfterAcknowledgementSent is { } observeCompletion) await observeCompletion(id, token);
    }
    public void Revoke(string deviceId)
    {
        lock (deviceLock) { devices.RemoveAll(x => x.DeviceId == deviceId); storage.Save("devices.dpapi", devices, true); }
        foreach (var client in clients.Values.Where(x => x.DeviceId == deviceId)) client.Socket.Abort();
        DevicesChanged?.Invoke();
    }
    private void Broadcast()
    {
        var snapshot = engine.Snapshot();
        foreach (var client in clients.Values) _ = SendSnapshotAsync(client, snapshot);
    }
    private static async Task SendSnapshotAsync(Client client, object snapshot)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await client.SendAsync(snapshot, timeout.Token); }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { client.Socket.Abort(); }
    }
    private sealed class CommandRateLimitException : IOException;
    private sealed class Client(string deviceId, WebSocket socket)
    {
        public string DeviceId { get; } = deviceId;
        public WebSocket Socket { get; } = socket;
        private readonly SemaphoreSlim sendLock = new(1);
        public async Task SendAsync(object value, CancellationToken token)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Protocol.Json);
            if (bytes.Length > Protocol.MaxBytes) throw new InvalidDataException("Outbound message exceeds protocol limit.");
            await sendLock.WaitAsync(token);
            try { if (Socket.State == WebSocketState.Open) await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, token); }
            finally { sendLock.Release(); }
        }
    }
}
