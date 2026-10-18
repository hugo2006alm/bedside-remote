using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BedsideRemote.Agent;
using BedsideRemote.Core;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
var root = Path.Combine(Path.GetTempPath(), "BedsideRemote-tests-" + Guid.NewGuid().ToString("N"));
var injectedInputs = 0;
var storage = new Storage(root); var engine = new Engine(storage, _ => Interlocked.Increment(ref injectedInputs)); var server = new RemoteServer(storage, engine);
using var certificate = storage.Certificate();
using (var reloaded = storage.Certificate()) Check(certificate.Thumbprint == reloaded.Thumbprint, "TLS identity must survive restart.");
var pin = SHA256.HashData(certificate.RawData);
using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45)); var token = lifetime.Token;
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
var nativeTask = new NativeBridge(engine).ListenAsync(token);
try
{
    await server.StartAsync(port, certificate, token);
    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert != null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.RawData), pin) };
    using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
    var health = await http.GetFromJsonAsync<JsonElement>("/health", token);
    Check(health.GetProperty("version").GetInt32() == 1 && health.EnumerateObject().Count() == 2, "Minimal health response.");
    using var wrongPinHandler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => false };
    using var wrongPin = new HttpClient(wrongPinHandler);
    try { await wrongPin.GetAsync(http.BaseAddress, token); throw new Exception("Wrong pin accepted."); } catch (HttpRequestException) { checks++; }
    var code = server.Pairing.Open(); server.Approve = (_, _) => Task.FromResult(false);
    using (var denied = await http.PostAsJsonAsync("/pair", new { version = 1, deviceName = "Test phone", code }, token)) Check(denied.StatusCode == HttpStatusCode.Forbidden && server.Devices.Length == 0, "Local denial blocks pairing.");
    server.Approve = (_, _) => Task.FromResult(true);
    using (var wrong = await http.PostAsJsonAsync("/pair", new { version = 1, deviceName = "Test phone", code = "abcdef" }, token)) Check(wrong.StatusCode == HttpStatusCode.Forbidden, "Wrong code blocked.");
    using (var invalid = await http.PostAsJsonAsync("/pair", new { version = 1, deviceName = "Test phone", code, extra = "not allowed" }, token)) Check(invalid.StatusCode == HttpStatusCode.BadRequest, "Unknown pairing fields blocked.");
    using var pairResponse = await http.PostAsJsonAsync("/pair", new { version = 1, deviceName = "Test phone", code }, token);
    Check(pairResponse.IsSuccessStatusCode, "Approved pairing succeeds.");
    var paired = await pairResponse.Content.ReadFromJsonAsync<JsonElement>(token);
    var credential = paired.GetProperty("token").GetString()!; var deviceId = paired.GetProperty("deviceId").GetString()!;
    Check(credential.Length == 44 && server.Devices.Length == 1, "Per-device credentials issued.");
    Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "devices.dpapi"))).Contains(credential, StringComparison.Ordinal), "Credentials protected on disk.");
    await ExpectSocketRejection(null, null); await ExpectSocketRejection(credential, "https://evil.example");
    using var socket = await ConnectSocket(credential); var initial = await Read(socket);
    Check(initial.GetProperty("type").GetString() == "state" && !initial.GetProperty("extensionConnected").GetBoolean(), "Initial universal state without extension.");
    using var native = new NamedPipeClientStream(".", NativeBridge.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await native.ConnectAsync(5000, token);
    await SendNativeState(native, "dQw4w9WgXcQ", false);
    var browserState = await ReadUntil(socket, x => x.GetProperty("type").GetString() == "state" && x.GetProperty("sessions").GetArrayLength() == 1);
    Check(browserState.GetProperty("extensionConnected").GetBoolean() && browserState.GetProperty("activeSessionId").GetString()!.EndsWith(":7", StringComparison.Ordinal), "Native session exposed with connection-qualified ID.");
    var nowId = Guid.NewGuid().ToString();
    await SendCommand(socket, nowId, 1, "youtube.enqueue", new { url = "https://youtu.be/dQw4w9WgXcQ?si=test", mode = "now" });
    var openNow = await ReadNative(native);
    Check(openNow.GetProperty("command").GetString() == "openYoutube" && openNow.GetProperty("url").GetString() == "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "Canonical YouTube open dispatch.");
    var responsiveId = Guid.NewGuid().ToString(); var responsiveStart = System.Diagnostics.Stopwatch.StartNew();
    await SendCommand(socket, responsiveId, 2, "input.key", new { key = "up" });
    Check((await Ack(socket, responsiveId)).GetProperty("ok").GetBoolean() && responsiveStart.Elapsed < TimeSpan.FromSeconds(3) && injectedInputs == 1, "Universal command acknowledged promptly while browser open is pending; injected test delegate never touches desktop.");
    await NativeFrames.WriteAsync(native, JsonSerializer.SerializeToUtf8Bytes(new { version = 1, type = "browser.state", tabs = Array.Empty<object>() }, Protocol.Json), token);
    await SendNativeState(native, "ZZZZZZZZZZZ", false, 8);
    var intermediate = await ReadUntil(socket, x => x.GetProperty("type").GetString() == "state" && x.GetProperty("sessions").GetArrayLength() == 1 && x.GetProperty("sessions")[0].GetProperty("tabId").GetInt32() == 8);
    Check(intermediate.GetProperty("currentEntryId").GetString() != null && intermediate.GetProperty("activeSessionId").ValueKind == JsonValueKind.Null, "Pending queue selection survives target-tab removal and another active tab.");
    await SendNativeState(native, "dQw4w9WgXcQ", false);
    var acknowledgementHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseAcknowledgement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    server.AfterAcknowledgementSent = async (id, cancellation) =>
    {
        if (id.ToString() != nowId) return;
        acknowledgementHeld.TrySetResult();
        await releaseAcknowledgement.Task.WaitAsync(cancellation);
    };
    try
    {
        await SendResult(native, openNow); Check((await Ack(socket, nowId)).GetProperty("ok").GetBoolean(), "Browser acknowledgement correlated.");
        await acknowledgementHeld.Task.WaitAsync(token);
        await SendNativeState(native, "dQw4w9WgXcQ", false);
        var nextId = Guid.NewGuid().ToString(); await SendCommand(socket, nextId, 3, "youtube.enqueue", new { url = "https://youtu.be/abcdefghijk", mode = "last" });
        Check((await Ack(socket, nextId)).GetProperty("ok").GetBoolean(), "Queue append is admitted while the previous observable ACK's send completion is deterministically held.");
    }
    finally { releaseAcknowledgement.TrySetResult(); server.AfterAcknowledgementSent = null; }
    await SendNativeState(native, "dQw4w9WgXcQ", true);
    var autoNext = await ReadNative(native);
    Check(autoNext.GetProperty("url").GetString() == "https://www.youtube.com/watch?v=abcdefghijk", "Natural ended transition opens next video.");
    await SendResult(native, autoNext); await SendNativeState(native, "abcdefghijk", false);
    var nextState = await ReadUntil(socket, x => x.GetProperty("type").GetString() == "state" && x.GetProperty("playback").ValueKind == JsonValueKind.Object && x.GetProperty("playback").GetProperty("videoId").GetString() == "abcdefghijk");
    Check(nextState.GetProperty("queue").GetArrayLength() == 2 && nextState.GetProperty("currentEntryId").GetString() != null, "Current queue entry retained and synchronized.");
    Check(storage.Read<QueueSnapshot>("queue.json")!.Queue.Count == 2, "Queue persisted to disk.");
    using (var disconnecting = await ConnectSocket(credential))
    {
        await Read(disconnecting);
        await SendCommand(disconnecting, Guid.NewGuid().ToString(), 1, "browser.command", new { command = "playPause" });
        var delayed = await ReadNative(native);
        var busyId = Guid.NewGuid().ToString(); var busyStart = System.Diagnostics.Stopwatch.StartNew();
        await SendCommand(disconnecting, busyId, 2, "queue.clear", new { });
        var busy = await Ack(disconnecting, busyId);
        Check(!busy.GetProperty("ok").GetBoolean() && busy.GetProperty("error").GetString()!.Contains("busy", StringComparison.Ordinal) && busyStart.Elapsed < TimeSpan.FromSeconds(3), "Further browser/queue operations receive immediate busy rejection and are never queued.");
        disconnecting.Abort();
        for (var attempt = 0; attempt < 40 && server.ActiveConnections != 1; attempt++) await Task.Delay(25, token);
        Check(server.ActiveConnections == 1, "Disconnect cancels active browser dispatch and releases its admission slot.");
        await SendResult(native, delayed);
        Check(JsonSerializer.SerializeToElement(engine.Snapshot(), Protocol.Json).GetProperty("queue").GetArrayLength() == 2, "Rejected operations cannot execute after disconnect or a late native result.");
    }
    var replayId = Guid.NewGuid().ToString(); await SendCommand(socket, replayId, 4, "queue.clear", new { });
    Check((await Ack(socket, replayId)).GetProperty("ok").GetBoolean(), "Allowlisted non-input command accepted.");
    using var reconnected = await ConnectSocket(credential); await Read(reconnected);
    await SendCommand(reconnected, replayId, 1, "queue.clear", new { }); Check(!(await Ack(reconnected, replayId)).GetProperty("ok").GetBoolean(), "Command ID replay across reconnect rejected.");
    var lowSequence = Guid.NewGuid().ToString(); await SendCommand(socket, lowSequence, 2, "queue.clear", new { });
    Check(!(await Ack(socket, lowSequence)).GetProperty("ok").GetBoolean(), "Out-of-order sequence rejected.");
    using (var consecutive = await ConnectSocket(credential))
    {
        await Read(consecutive);
        for (var sequence = 1; sequence <= 100; sequence++)
        {
            var id = Guid.NewGuid().ToString();
            var shouldSucceed = sequence % 2 == 1;
            await SendCommand(consecutive, id, sequence, shouldSucceed ? "queue.clear" : "queue.remove", shouldSucceed ? new { } : (object)new { entryId = "missing-entry" });
            var completion = await Ack(consecutive, id);
            if (completion.GetProperty("ok").GetBoolean() != shouldSucceed || !shouldSucceed && completion.GetProperty("error").GetString() != "Queue entry was removed.")
                throw new Exception($"Consecutive command {sequence} returned an unexpected acknowledgement: {completion}");
        }
        Check(true, "100 consecutive ACK-followed browser/queue operations alternate success and expected errors without sleeps, retries or spurious busy rejection.");
        for (var index = 0; index < 50; index++) { await Read(socket); await Read(reconnected); }
    }
    Check(await NativeBridge.RunHostAsync(["--native", "wrong-extension@example"]) == 2, "Native host rejects unapproved extension.");
    var boundedStorage = new Storage(Path.Combine(root, "bounds"));
    var longTitle = new string('語', 512);
    boundedStorage.Save("queue.json", new QueueSnapshot(Enumerable.Range(0, 50).Select(_ => new QueueEntry(Guid.NewGuid().ToString(), "dQw4w9WgXcQ", "https://www.youtube.com/watch?v=dQw4w9WgXcQ", longTitle)).ToList(), null));
    var boundedEngine = new Engine(boundedStorage);
    using (var boundedWire = new MemoryStream())
    using (var boundedNative = new NativeConnection("size-test", boundedWire))
    {
        boundedEngine.AddNative(boundedNative);
        JsonElement SelectionState(int activeTab) => JsonSerializer.SerializeToElement(new { version = 1, type = "browser.state", activeTabId = activeTab, tabs = new[] { 1, 2 }.Select(i => new { platform = "youtube", tabId = i, title = "Tab " + i, videoId = "dQw4w9WgXcQ", paused = false, ended = false, position = 0, duration = 0, capabilities = Array.Empty<string>() }).ToArray() }, Protocol.Json);
        string? ActiveTarget() => JsonSerializer.SerializeToElement(boundedEngine.Snapshot(), Protocol.Json).GetProperty("activeSessionId").GetString();
        await boundedEngine.UpdateNativeAsync(boundedNative, SelectionState(1), token); Check(ActiveTarget() == "size-test:1", "Initial active browser target selected.");
        await boundedEngine.UpdateNativeAsync(boundedNative, SelectionState(2), token); Check(ActiveTarget() == "size-test:2", "Changed popup/browser active tab updates remote target.");
        using (var selectPayload = JsonDocument.Parse("{\"sessionId\":\"size-test:1\"}"))
        {
            var selectTask = boundedEngine.ExecuteAsync(new Command(Guid.NewGuid(), 1, "browser.select", selectPayload.RootElement.Clone()), token);
            boundedWire.Position = 0; var nativeSelect = await ReadNative(boundedWire);
            Check(nativeSelect.GetProperty("command").GetString() == "selectTab" && nativeSelect.GetProperty("tabId").GetInt32() == 1, "Android selection synchronizes the extension target through native selectTab.");
            boundedNative.Complete(JsonSerializer.SerializeToElement(new { version = 1, type = "browser.result", id = nativeSelect.GetProperty("id").GetString(), ok = true }, Protocol.Json));
            await selectTask;
        }
        await boundedEngine.UpdateNativeAsync(boundedNative, SelectionState(2), token); Check(ActiveTarget() == "size-test:1", "Explicit Android target survives unchanged native progress state.");
        await boundedEngine.UpdateNativeAsync(boundedNative, SelectionState(1), token);
        await boundedEngine.UpdateNativeAsync(boundedNative, SelectionState(2), token); Check(ActiveTarget() == "size-test:2", "Popup can select its former target after Android selection synchronizes the native pointer.");
        var stress = JsonSerializer.SerializeToElement(new { version = 1, type = "browser.state", activeTabId = 0, tabs = Enumerable.Range(0, 32).Select(i => new { platform = "youtube", tabId = i, title = longTitle, videoId = "dQw4w9WgXcQ", channel = new string('語', 2048), thumbnail = new string('語', 2048), error = new string('語', 2048), paused = true, ended = false, position = 0, duration = 0, capabilities = Array.Empty<string>() }).ToArray() }, Protocol.Json);
        await boundedEngine.UpdateNativeAsync(boundedNative, stress, token);
        Check(JsonSerializer.SerializeToUtf8Bytes(boundedEngine.Snapshot(), Protocol.Json).Length <= Protocol.MaxBytes, "Maximum queue/session snapshot remains within 64 KiB even with escaped Unicode metadata.");
    }
    var nativeExeArgument = args.FirstOrDefault(x => x.StartsWith("--native-exe=", StringComparison.Ordinal));
    if (nativeExeArgument != null)
    {
        var executable = Path.GetFullPath(nativeExeArgument["--native-exe=".Length..]);
        var start = new System.Diagnostics.ProcessStartInfo(executable) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(root, "native-host.json")); start.ArgumentList.Add(NativeBridge.ExtensionId);
        using var host = System.Diagnostics.Process.Start(start)!;
        try
        {
            await SendNativeState(host.StandardInput.BaseStream, "ZZZZZZZZZZZ", false);
            var multiple = await ReadUntil(socket, x => x.GetProperty("type").GetString() == "state" && x.GetProperty("sessions").GetArrayLength() == 2);
            var secondSession = multiple.GetProperty("sessions").EnumerateArray().Single(x => x.GetProperty("sessionId").GetString() != multiple.GetProperty("activeSessionId").GetString()).GetProperty("sessionId").GetString()!;
            var selectId = Guid.NewGuid().ToString(); await SendCommand(socket, selectId, 5, "browser.select", new { sessionId = secondSession });
            var select = await ReadNative(host.StandardOutput.BaseStream);
            Check(select.GetProperty("command").GetString() == "selectTab" && select.GetProperty("tabId").GetInt32() == 7, "Published WinExe native host forwards framed commands over stdout to the correct browser peer.");
            await SendResult(host.StandardInput.BaseStream, select); Check((await Ack(socket, selectId)).GetProperty("ok").GetBoolean(), "Native stdio response bridges back through current-user pipe.");
            host.StandardInput.Close(); await host.WaitForExitAsync(token);
            Check(host.ExitCode == 0 && string.IsNullOrEmpty(await host.StandardError.ReadToEndAsync(token)), "Native host exits cleanly on browser EOF without diagnostics on the messaging stream.");
        }
        finally { if (!host.HasExited) host.Kill(); }
    }
    server.Revoke(deviceId);
    Check(server.Devices.Length == 0, "Device removed on revocation.");
    try { await Read(socket); throw new Exception("Revoked socket remained open."); } catch (Exception ex) when (ex is WebSocketException or EndOfStreamException) { checks++; }
    await ExpectSocketRejection(credential, null);
    var previewArgument = args.FirstOrDefault(x => x.StartsWith("--preview=", StringComparison.Ordinal));
    if (previewArgument != null)
    {
        Exception? renderError = null;
        var previewPath = Path.GetFullPath(previewArgument["--preview=".Length..]);
        var render = new Thread(() =>
        {
            try
            {
                var application = new System.Windows.Application();
                var window = new MainWindow(storage, new Settings(port), server, Convert.ToHexString(pin));
                window.SetStatus("Ready · encrypted local control", false);
                window.BeginAnimation(System.Windows.UIElement.OpacityProperty, null); window.Opacity = 1;
                var visual = (System.Windows.Controls.ScrollViewer)window.Content; visual.Background = window.Background;
                visual.Measure(new System.Windows.Size(620, 790)); visual.Arrange(new System.Windows.Rect(0, 0, 620, 790)); visual.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(620, 790, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(visual);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!); using var output = File.Create(previewPath); png.Save(output);
                application.Shutdown();
            }
            catch (Exception ex) { renderError = ex; }
        });
        render.SetApartmentState(ApartmentState.STA); render.Start(); render.Join();
        if (renderError != null) throw renderError;
        Check(File.Exists(previewPath), "Actual WPF layout rendered offscreen.");
    }
    Console.WriteLine($"PASS: {checks} Windows integration checks. Real pinned TLS/HTTPS/WSS, DPAPI, named pipe, pairing, browser/queue dispatch and revocation. No desktop input injected.");

    async Task<ClientWebSocket> ConnectSocket(string bearer)
    {
        var ws = new ClientWebSocket(); ws.Options.SetRequestHeader("Authorization", "Bearer " + bearer);
        ws.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => cert != null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.GetRawCertData()), pin);
        await ws.ConnectAsync(new Uri($"wss://127.0.0.1:{port}/ws"), token); return ws;
    }
    async Task ExpectSocketRejection(string? bearer, string? origin)
    {
        using var ws = new ClientWebSocket();
        ws.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => cert != null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.GetRawCertData()), pin);
        if (bearer != null) ws.Options.SetRequestHeader("Authorization", "Bearer " + bearer);
        if (origin != null) ws.Options.SetRequestHeader("Origin", origin);
        try { await ws.ConnectAsync(new Uri($"wss://127.0.0.1:{port}/ws"), token); throw new Exception("Unauthorized WSS accepted."); } catch (WebSocketException) { checks++; }
    }
    async Task<JsonElement> Read(ClientWebSocket ws)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var buffer = new byte[65536]; var used = 0; WebSocketReceiveResult frame;
        do { frame = await ws.ReceiveAsync(new ArraySegment<byte>(buffer, used, buffer.Length - used), timeout.Token); if (frame.MessageType == WebSocketMessageType.Close) throw new EndOfStreamException(); used += frame.Count; } while (!frame.EndOfMessage);
        using var doc = JsonDocument.Parse(buffer.AsMemory(0, used)); return doc.RootElement.Clone();
    }
    async Task<JsonElement> ReadUntil(ClientWebSocket ws, Func<JsonElement, bool> predicate)
    { for (var i = 0; i < 30; i++) { var message = await Read(ws); if (predicate(message)) return message; } throw new Exception("Expected message missing."); }
    Task<JsonElement> Ack(ClientWebSocket ws, string id) => ReadUntil(ws, x => x.GetProperty("type").GetString() == "ack" && x.GetProperty("id").GetString() == id);
    async Task SendCommand(ClientWebSocket ws, string id, long sequence, string action, object payload) => await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { version = 1, type = "command", id, sequence, action, payload }, Protocol.Json), WebSocketMessageType.Text, true, token);
    async Task<JsonElement> ReadNative(Stream stream) { using var doc = JsonDocument.Parse((await NativeFrames.ReadAsync(stream, token))!); return doc.RootElement.Clone(); }
    Task SendResult(Stream stream, JsonElement command) => NativeFrames.WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(new { version = 1, type = "browser.result", id = command.GetProperty("id").GetString(), ok = true }, Protocol.Json), token);
    Task SendNativeState(Stream stream, string videoId, bool ended, int tabId = 7) => NativeFrames.WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(new { version = 1, type = "browser.state", activeTabId = tabId, tabs = new[] { new { platform = "youtube", tabId, videoId, title = "Integration video", paused = false, position = ended ? 60 : 0, duration = 60, ended, capabilities = new[] { "playPause", "seek", "fullscreen" } } } }, Protocol.Json), token);
}
finally
{
    lifetime.Cancel(); await server.StopAsync();
    try { await nativeTask; } catch (OperationCanceledException) { }
    Directory.Delete(root, true);
}
