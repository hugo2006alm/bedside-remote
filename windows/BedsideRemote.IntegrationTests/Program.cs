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
var launchedApps = new List<System.Diagnostics.ProcessStartInfo>();
var mediaApps = new MediaApps(launchedApps.Add);
mediaApps.Launch("youtube");
Check(launchedApps.Single().FileName == "https://www.youtube.com/" && launchedApps.Single().UseShellExecute, "YouTube launch resolves only the fixed official destination through a simulated process launcher.");
mediaApps.Launch("netflix");
Check(launchedApps[1].FileName == "microsoft-edge:https://www.netflix.com/" || launchedApps[1].Arguments == @"shell:AppsFolder\4DF9E0F8.Netflix_mcm4njqhnhss8!Netflix", "Netflix resolves only its fixed official Edge destination or recognized Store identity.");
var focusBounds = new[] { new System.Windows.Rect(0, 0, 50, 50), new System.Windows.Rect(100, 0, 50, 50), new System.Windows.Rect(0, 100, 50, 50) };
Check(DesktopNavigation.Nearest(focusBounds, 0, "right") == 1 && DesktopNavigation.Nearest(focusBounds, 0, "down") == 2 && DesktopNavigation.Nearest(focusBounds, 1, "right") == 1, "TV navigation chooses visible neighbors by direction and preserves selection at an edge.");
var dragEvents = new List<uint>();
var drag = new PointerDrag((flags, _, _) => dragEvents.Add(flags), () => new IntPtr(1));
using (var dragLifetime = new CancellationTokenSource())
using (var otherLifetime = new CancellationTokenSource())
{
    drag.Handle("start", 0, 0, dragLifetime.Token);
    drag.Handle("move", 15, 5, dragLifetime.Token);
    try { drag.Handle("end", 0, 0, otherLifetime.Token); throw new Exception("Another connection released a drag."); }
    catch (InvalidOperationException) { Check(drag.Active, "A different connection cannot change an active drag."); }
    dragLifetime.Cancel();
    Check(!drag.Active && dragEvents.SequenceEqual(new uint[] { 2, 1, 4 }), "Disconnect cancellation releases the held button exactly once without real desktop input.");
}
var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var expiringDrag = new PointerDrag((flags, _, _) => { if (flags == 4) released.TrySetResult(); }, () => new IntPtr(1), 100);
using (var leaseOwner = new CancellationTokenSource())
{
    expiringDrag.Handle("start", 0, 0, leaseOwner.Token);
    await released.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Check(!expiringDrag.Active, "Missing drag heartbeats expire and release the button.");
}
Check(NativeBridge.IsAuthorizedInvocation(new[] { NativeBridge.EdgeOrigin, "--parent-window=0" }) && NativeBridge.IsAuthorizedInvocation(new[] { NativeBridge.EdgeOrigin }), "Edge native startup accepts only its fixed origin and optional numeric parent handle.");
Check(!NativeBridge.IsAuthorizedInvocation(new[] { "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/", "--parent-window=0" }) && !NativeBridge.IsAuthorizedInvocation(new[] { NativeBridge.EdgeOrigin, "--parent-window=bad" }) && !NativeBridge.IsAuthorizedInvocation(new[] { NativeBridge.EdgeOrigin, "--parent-window=0", "extra" }), "Edge native startup rejects foreign origins, invalid parent handles and extra arguments.");
Check(DiscoveryAddresses.Encode(new[] { "100.65.203.96", "192.168.1.28", "127.0.0.1", "8.8.8.8", "192.168.1.28", "fd12::1" }.Select(IPAddress.Parse)) == "192.168.1.28", "Discovery TXT hints contain distinct private IPv4 addresses only, excluding VPN CGNAT, public and loopback addresses.");
var discoveryHints = DiscoveryAddresses.Encode(Enumerable.Range(1, 100).Select(index => IPAddress.Parse($"192.168.100.{index}")));
Check(Encoding.UTF8.GetByteCount("addresses=" + discoveryHints) <= 255 && discoveryHints.Split(',').Length <= 16 && discoveryHints.Split(',').All(address => IPAddress.TryParse(address, out _)), "Discovery TXT hints are bounded without truncating an address.");
var unicodeText = "Bedside Remote - café, ç 😀";
var packets = new List<DesktopInput.Input[]>();
var pauses = 0;
await DesktopInput.TypeTextAsync(unicodeText, packets.Add, (_, _) => { pauses++; return Task.CompletedTask; }, CancellationToken.None);
var decodedPackets = new string(packets.SelectMany(packet => packet.Where((_, index) => index % 2 == 0).Select(input => (char)input.Data.Keyboard.Scan)).ToArray());
Check(decodedPackets == unicodeText && pauses == unicodeText.Length, "Paced Unicode packets preserve ASCII, accents and supplementary scalars.");
Check(packets.All(packet => packet.Length == 2 && packet.Select((input, index) => input.Type == 1 && input.Data.Keyboard.VirtualKey == 0 && input.Data.Keyboard.Flags == (index % 2 == 0 ? 4u : 6u)).All(valid => valid)), "Every UTF-16 unit has its own Unicode key-down/up packet.");
Check(System.Runtime.InteropServices.Marshal.SizeOf<DesktopInput.Input>() == (IntPtr.Size == 8 ? 40 : 28), "Native INPUT layout matches the operating-system ABI.");
using (var cancelledTyping = new CancellationTokenSource())
{
    var emitted = 0;
    try
    {
        await DesktopInput.TypeTextAsync("😀x", packet => { emitted += packet.Length; cancelledTyping.Cancel(); }, (_, cancellation) => cancellation.IsCancellationRequested ? Task.FromCanceled(cancellation) : Task.CompletedTask, cancelledTyping.Token);
        throw new Exception("Cancelled typing continued.");
    }
    catch (OperationCanceledException) { Check(emitted == 4, "Cancellation stops before the next scalar and cannot leave half an emoji queued."); }
}
var layoutPackets = new List<DesktopInput.Input[]>();
await DesktopInput.TypeTextAsync("aAé", layoutPackets.Add, (_, _) => Task.CompletedTask, CancellationToken.None, character => character switch { 'a' => 0x41, 'A' => 0x141, _ => -1 });
Check(layoutPackets[0].Length == 2 && layoutPackets[0][0].Data.Keyboard.VirtualKey == 0x41 && layoutPackets[1].Length == 4 && layoutPackets[1][0].Data.Keyboard.VirtualKey == 0x10 && layoutPackets[1][^1].Data.Keyboard.Flags == 2 && layoutPackets[2][0].Data.Keyboard.Scan == 'é', "Text uses layout keys and balanced Shift, falling back to Unicode for unmapped characters.");
layoutPackets.Clear();
await DesktopInput.TypeTextAsync("aA", layoutPackets.Add, (_, _) => Task.CompletedTask, CancellationToken.None, character => character == 'a' ? (short)0x41 : (short)0x141, capsLock: true);
Check(layoutPackets[0].Length == 4 && layoutPackets[1].Length == 2, "Layout text compensates for Caps Lock without toggling the user's setting.");
var partialPackets = new List<DesktopInput.Input[]>();
try
{
    DesktopInput.SendBatch(layoutPackets[0], packet => { partialPackets.Add(packet); return 1; });
    throw new Exception("Partial native input was accepted.");
}
catch (InvalidOperationException)
{
    Check(partialPackets.Count == 2 && partialPackets[1].Length == 1 && partialPackets[1][0].Data.Keyboard.VirtualKey == 0x10 && partialPackets[1][0].Data.Keyboard.Flags == 2, "Partial native delivery releases the Shift key introduced by the text packet and reports failure.");
}
var rejectedPackets = 0;
try { DesktopInput.SendBatch(layoutPackets[0], _ => { rejectedPackets++; return 0; }); throw new Exception("Rejected native input was accepted."); }
catch (InvalidOperationException) { Check(rejectedPackets == 1, "Zero accepted input events cannot trigger an unnecessary Shift release."); }
foreach (var input in new[] {
    new DesktopInput.Input { Type = 1, Data = new DesktopInput.Union { Keyboard = new DesktopInput.Keyboard { VirtualKey = 0x0D } } },
    new DesktopInput.Input { Type = 1, Data = new DesktopInput.Union { Keyboard = new DesktopInput.Keyboard { VirtualKey = 0x5B } } },
    new DesktopInput.Input { Type = 1, Data = new DesktopInput.Union { Keyboard = new DesktopInput.Keyboard { Scan = 'é', Flags = 4 } } },
    new DesktopInput.Input { Data = new DesktopInput.Union { Mouse = new DesktopInput.Mouse { Flags = 2 } } },
    new DesktopInput.Input { Data = new DesktopInput.Union { Mouse = new DesktopInput.Mouse { Flags = 8 } } }
})
{
    var release = input;
    if (input.Type == 1) release.Data.Keyboard.Flags |= 2;
    else release.Data.Mouse.Flags *= 2;
    var delivered = new List<DesktopInput.Input[]>();
    try { DesktopInput.SendBatch([input, release], batch => { delivered.Add(batch); return 1; }); throw new Exception("Partial delivery succeeded."); }
    catch (InvalidOperationException) { Check(delivered.Count == 2 && delivered[1].Length == 1 && (input.Type == 1 ? delivered[1][0].Data.Keyboard.Flags == release.Data.Keyboard.Flags && delivered[1][0].Data.Keyboard.Scan == input.Data.Keyboard.Scan && delivered[1][0].Data.Keyboard.VirtualKey == input.Data.Keyboard.VirtualKey : delivered[1][0].Data.Mouse.Flags == release.Data.Mouse.Flags), "Partial Enter, Windows, Unicode and mouse presses receive their matching release."); }
}
foreach (var work in new[] { new System.Windows.Rect(0, 0, 1280, 680), new System.Windows.Rect(-960, 0, 960, 520), new System.Windows.Rect(0, 0, 640, 440) })
{
    var fitted = MainWindow.FitBounds(work, 640, 700);
    Check(work.Contains(fitted) && fitted.Width > 0 && fitted.Height > 0, "Window fits small/high-DPI and secondary-monitor work areas.");
}
var root = Path.Combine(Path.GetTempPath(), "BedsideRemote-tests-" + Guid.NewGuid().ToString("N"));
var injectedInputs = 0;
var blockText = false;
var textStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var textStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var storage = new Storage(root);
var engine = new Engine(storage, desktopInputAsync: async (command, cancellation) =>
{
    if (blockText && command.Action == "input.text")
    {
        textStarted.TrySetResult();
        try { await Task.Delay(Timeout.Infinite, cancellation); }
        finally { textStopped.TrySetResult(); }
    }
    else Interlocked.Increment(ref injectedInputs);
});
var server = new RemoteServer(storage, engine);
using var certificate = storage.Certificate();
using (var reloaded = storage.Certificate()) Check(certificate.Thumbprint == reloaded.Thumbprint, "TLS identity must survive restart.");
var pin = SHA256.HashData(certificate.RawData);
using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45)); var token = lifetime.Token;
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
var isolatedNative = args.Contains("--isolate-native", StringComparer.Ordinal);
var nativePipeName = isolatedNative ? "BedsideRemote-tests-" + Guid.NewGuid().ToString("N") : NativeBridge.PipeName;
var nativeTask = new NativeBridge(engine, nativePipeName).ListenAsync(token);
try
{
    await server.StartAsync(port, certificate, token);
    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert != null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.RawData), pin) };
    using var http = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
    var health = await http.GetFromJsonAsync<JsonElement>("/health", token);
    using (var unpairedApps = await http.GetAsync("/apps", token)) Check(unpairedApps.StatusCode == HttpStatusCode.Unauthorized, "Installed media apps are not exposed before authentication.");
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
    using (var appRequest = new HttpRequestMessage(HttpMethod.Get, "/apps"))
    {
        appRequest.Headers.Authorization = new("Bearer", credential);
        using var appResponse = await http.SendAsync(appRequest, token);
        var catalog = await appResponse.Content.ReadFromJsonAsync<JsonElement>(token);
        Check(appResponse.IsSuccessStatusCode && catalog.GetProperty("controls").EnumerateArray().Any(control => control.GetString() == "drag") && catalog.GetProperty("apps").EnumerateArray().Any(item => item.GetProperty("appId").GetString() == "youtube"), "Authenticated app catalog exposes fixed identities and optional controls without changing state snapshots.");
        try { engine.Apps.Launch("powershell.exe"); throw new Exception("Arbitrary app was launched."); }
        catch (InvalidOperationException) { checks++; }
    }
    Check(credential.Length == 44 && server.Devices.Length == 1, "Per-device credentials issued.");
    Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "devices.dpapi"))).Contains(credential, StringComparison.Ordinal), "Credentials protected on disk.");
    await ExpectSocketRejection(null, null); await ExpectSocketRejection(credential, "https://evil.example");
    using var socket = await ConnectSocket(credential); var initial = await Read(socket);
    Check(initial.GetProperty("type").GetString() == "state" && !initial.GetProperty("extensionConnected").GetBoolean(), "Initial universal state without extension.");
    using var native = new NamedPipeClientStream(".", nativePipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
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
    using (var navigation = await ConnectSocket(credential))
    {
        await Read(navigation);
        var id = Guid.NewGuid().ToString();
        var beforeInput = injectedInputs;
        await SendCommand(navigation, id, 1, "browser.navigate", new { key = "right" });
        var focus = await ReadNative(native);
        Check(focus.GetProperty("command").GetString() == "navigateRight" && focus.GetProperty("tabId").GetInt32() == 7, "D-pad navigation targets the explicitly selected browser tab.");
        await SendResult(native, focus);
        Check((await Ack(navigation, id)).GetProperty("ok").GetBoolean() && injectedInputs == beforeInput, "Browser focus navigation does not inject Windows scrolling or keyboard input.");
    }
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
    using (var typing = await ConnectSocket(credential))
    {
        await Read(typing);
        blockText = true;
        await SendCommand(typing, Guid.NewGuid().ToString(), 1, "input.text", new { text = unicodeText });
        await textStarted.Task.WaitAsync(token);
        var keyId = Guid.NewGuid().ToString();
        await SendCommand(typing, keyId, 2, "input.key", new { key = "right" });
        var rejectedKey = await Ack(typing, keyId);
        Check(!rejectedKey.GetProperty("ok").GetBoolean() && rejectedKey.GetProperty("error").GetString()!.Contains("not queued", StringComparison.Ordinal), "Long text does not block socket reads; competing desktop actions are rejected instead of delayed.");
        var textId = Guid.NewGuid().ToString();
        await SendCommand(typing, textId, 3, "input.text", new { text = "must not run later" });
        Check(!(await Ack(typing, textId)).GetProperty("ok").GetBoolean(), "A second text submission is rejected without queueing.");
        var clearId = Guid.NewGuid().ToString();
        await SendCommand(typing, clearId, 4, "queue.clear", new { });
        Check((await Ack(typing, clearId)).GetProperty("ok").GetBoolean(), "Browser/queue operations remain available during long text delivery.");
        typing.Abort();
        await textStopped.Task.WaitAsync(token);
        blockText = false;
    }
    // Wait for connection cleanup, which awaits typing cancellation before releasing it.
    for (var attempt = 0; attempt < 40 && server.ActiveConnections != 2; attempt++) await Task.Delay(25, token);
    using (var afterTyping = await ConnectSocket(credential))
    {
        await Read(afterTyping);
        var keyId = Guid.NewGuid().ToString();
        await SendCommand(afterTyping, keyId, 1, "input.key", new { key = "right" });
        Check((await Ack(afterTyping, keyId)).GetProperty("ok").GetBoolean(), "Disconnect cancels text delivery and releases desktop input for the next connection.");
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
        var queuePath = Path.Combine(root, "bounds", "queue.json");
        var savedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(queuePath, savedAt);
        for (var index = 0; index < 20; index++) await boundedEngine.UpdateNativeAsync(boundedNative, SelectionState(2), token);
        Check(File.GetLastWriteTimeUtc(queuePath) == savedAt, "Repeated playback progress does not rewrite an unchanged persistent queue.");
        var stress = JsonSerializer.SerializeToElement(new { version = 1, type = "browser.state", activeTabId = 0, tabs = Enumerable.Range(0, 32).Select(i => new { platform = "youtube", tabId = i, title = longTitle, videoId = "dQw4w9WgXcQ", channel = new string('語', 2048), thumbnail = new string('語', 2048), error = new string('語', 2048), paused = true, ended = false, position = 0, duration = 0, capabilities = Array.Empty<string>() }).ToArray() }, Protocol.Json);
        await boundedEngine.UpdateNativeAsync(boundedNative, stress, token);
        Check(JsonSerializer.SerializeToUtf8Bytes(boundedEngine.Snapshot(), Protocol.Json).Length <= Protocol.MaxBytes, "Maximum queue/session snapshot remains within 64 KiB even with escaped Unicode metadata.");
    }
    var nativeExeArgument = args.FirstOrDefault(x => x.StartsWith("--native-exe=", StringComparison.Ordinal));
    if (nativeExeArgument != null)
    {
        if (isolatedNative) throw new ArgumentException("--native-exe requires the default pipe; stop the installed agent first or omit --native-exe.");
        var executable = Path.GetFullPath(nativeExeArgument["--native-exe=".Length..]);
        foreach (var edge in new[] { false, true })
        {
            var start = new System.Diagnostics.ProcessStartInfo(executable) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(edge ? NativeBridge.EdgeOrigin : Path.Combine(root, "native-host.json")); start.ArgumentList.Add(edge ? "--parent-window=0" : NativeBridge.ExtensionId);
            using var host = System.Diagnostics.Process.Start(start)!;
            try
            {
                var ready = await ReadNative(host.StandardOutput.BaseStream);
                Check(ready.GetProperty("version").GetInt32() == 1 && ready.GetProperty("type").GetString() == "native.ready", "Published native host confirms the agent pipe before the extension may report connected.");
                await SendNativeState(host.StandardInput.BaseStream, "ZZZZZZZZZZZ", false);
                var multiple = await ReadUntil(socket, x => x.GetProperty("type").GetString() == "state" && x.GetProperty("sessions").GetArrayLength() == 2);
                var secondSession = multiple.GetProperty("sessions").EnumerateArray().Single(x => x.GetProperty("sessionId").GetString() != browserState.GetProperty("activeSessionId").GetString()).GetProperty("sessionId").GetString()!;
                var selectId = Guid.NewGuid().ToString(); await SendCommand(socket, selectId, edge ? 6 : 5, "browser.select", new { sessionId = secondSession });
                var select = await ReadNative(host.StandardOutput.BaseStream);
                Check(select.GetProperty("command").GetString() == "selectTab" && select.GetProperty("tabId").GetInt32() == 7, "Published WinExe native host forwards framed commands over stdout to the correct browser peer.");
                await SendResult(host.StandardInput.BaseStream, select); Check((await Ack(socket, selectId)).GetProperty("ok").GetBoolean(), "Native stdio response bridges back through current-user pipe.");
                host.StandardInput.Close(); await host.WaitForExitAsync(token);
                Check(host.ExitCode == 0 && string.IsNullOrEmpty(await host.StandardError.ReadToEndAsync(token)), "Native host exits cleanly on browser EOF without diagnostics on the messaging stream.");
            }
            finally { if (!host.HasExited) host.Kill(); }
        }
    }
    using (var flood = await ConnectSocket(credential))
    {
        await Read(flood);
        for (var index = 1; index <= 151; index++) await SendCommand(flood, Guid.NewGuid().ToString(), index, "input.pointer", new { dx = 0, dy = 0 });
        var buffer = new byte[65536];
        WebSocketReceiveResult result;
        do { result = await flood.ReceiveAsync(buffer, token); } while (result.MessageType != WebSocketMessageType.Close);
        Check((int?)result.CloseStatus == 1013, "Rate limiting reports a retryable overload rather than authorization revocation.");
        await flood.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledged", token);
    }
    using (var afterRateLimit = await ConnectSocket(credential)) { await Read(afterRateLimit); Check(true, "Rate limiting preserves the paired credential."); }
    server.Revoke(deviceId);
    Check(server.Devices.Length == 0, "Device removed on revocation.");
    try { await Read(socket); throw new Exception("Revoked socket remained open."); } catch (Exception ex) when (ex is WebSocketException or EndOfStreamException) { checks++; }
    await ExpectSocketRejection(credential, null);
    code = server.Pairing.Open();
    using (var replacement = await http.PostAsJsonAsync("/pair", new { version = 1, deviceName = "Shutdown test", code }, token))
    {
        var identity = (await replacement.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("token").GetString()!;
        using var stoppingSocket = await ConnectSocket(identity); await Read(stoppingSocket);
        textStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        textStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); blockText = true;
        await SendCommand(stoppingSocket, Guid.NewGuid().ToString(), 1, "input.text", new { text = "cancel on exit" });
        await textStarted.Task.WaitAsync(token);
        await Task.WhenAll(server.StopAsync(), server.StopAsync()).WaitAsync(TimeSpan.FromSeconds(5), token);
        await textStopped.Task.WaitAsync(token);
        var before = injectedInputs;
        try { await SendCommand(stoppingSocket, Guid.NewGuid().ToString(), 2, "input.key", new { key = "enter" }); } catch (WebSocketException) { }
        Check(injectedInputs == before && server.ActiveConnections == 0, "Exit promptly cancels active text and closes existing authenticated sockets; repeated stop is safe.");
    }
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
                var visual = (System.Windows.FrameworkElement)window.Content;
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
