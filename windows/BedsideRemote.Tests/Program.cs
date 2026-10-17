using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using BedsideRemote.Core;

var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }
void Reject(Action action, string message)
{
    try { action(); } catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or KeyNotFoundException or InvalidDataException) { passed++; return; }
    throw new Exception("Accepted invalid input: " + message);
}
Command Parse(string action, string payload, long sequence = 1, string? id = null) => Protocol.ParseCommand(Encoding.UTF8.GetBytes($$"""{"version":1,"type":"command","id":"{{id ?? Guid.NewGuid().ToString()}}","sequence":{{sequence}},"action":"{{action}}","payload":{{payload}}} """));
using (var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "youtube-url-cases.json"))))
    foreach (var sample in corpus.RootElement.EnumerateArray())
    {
        var url = sample.GetProperty("url").GetString()!;
        var expected = sample.GetProperty("videoId").GetString();
        if (expected == null) Reject(() => Protocol.YoutubeId(url), url);
        else Check(Protocol.YoutubeId(url) == expected, "YouTube canonical ID");
    }
foreach (var invalid in new[] { "https://youtube.com/watch?v=dQw4w9WgXcQ&v=dQw4w9WgXcQ", "https://youtu.be/dQw4w9WgXcQ/other", "https://youtube.com/shorts/dQw4w9WgXcQ/other" }) Reject(() => Protocol.YoutubeId(invalid), invalid);
foreach (var address in new[] { "192.168.1.7", "10.0.0.2", "172.16.0.1", "172.31.255.255", "127.0.0.1", "::1", "fe80::123", "fd00::1", "::ffff:192.168.0.3" }) Check(Protocol.IsLan(IPAddress.Parse(address)), "LAN must be accepted");
foreach (var address in new[] { "8.8.8.8", "172.15.0.1", "172.32.0.1", "0.0.0.0", "100.64.1.1", "2001:4860:4860::8888", "169.254.1.1" }) Check(!Protocol.IsLan(IPAddress.Parse(address)), "Public/unapproved address must be rejected");
var valid = new Dictionary<string, string>
{
    ["input.key"] = "{\"key\":\"home\"}",
    ["input.text"] = "{\"text\":\"Olá 🌙\"}",
    ["input.pointer"] = "{\"dx\":-1000,\"dy\":1000}",
    ["input.click"] = "{\"button\":\"right\"}",
    ["input.scroll"] = "{\"delta\":-1200}",
    ["media.command"] = "{\"command\":\"mute\"}",
    ["browser.command"] = "{\"command\":\"seek\",\"position\":1.25}",
    ["browser.select"] = "{\"sessionId\":\"test:1\"}",
    ["youtube.enqueue"] = "{\"url\":\"https://youtu.be/dQw4w9WgXcQ\",\"mode\":\"now\"}",
    ["queue.remove"] = "{\"entryId\":\"test\"}",
    ["queue.move"] = "{\"entryId\":\"test\",\"index\":0}",
    ["queue.clear"] = "{}",
    ["queue.next"] = "{}",
    ["queue.previous"] = "{}"
};
foreach (var item in valid) Check(Parse(item.Key, item.Value).Action == item.Key, "Valid action");
foreach (var sample in new[] { ("input.key", "{\"key\":\"powershell\"}"), ("input.text", "{\"text\":\"\"}"), ("input.pointer", "{\"dx\":1001,\"dy\":0}"), ("input.scroll", "{\"delta\":1.5}"), ("browser.command", "{\"command\":\"seek\"}"), ("queue.clear", "{\"unknown\":true}"), ("shell", "{}"), ("input.key", "{\"key\":\"up\",\"key\":\"down\"}") }) Reject(() => Parse(sample.Item1, sample.Item2), sample.ToString());
Reject(() => Parse("input.text", JsonSerializer.Serialize(new { text = new string('a', 4097) })), "Oversized text");
Reject(() => Parse("queue.clear", "{}", 0), "Zero sequence");
var replay = new ReplayGuard(); var first = Parse("queue.clear", "{}");
Check(replay.Accept(first), "First command"); Check(!replay.Accept(first), "Repeated command");
Check(!replay.Accept(Parse("queue.clear", "{}", 1)), "Repeated sequence"); Check(replay.Accept(Parse("queue.clear", "{}", 2)), "Increasing sequence");
for (var sequence = 3; sequence < 110000; sequence++)
    if (!replay.Accept(new Command(Guid.NewGuid(), sequence, "input.pointer", first.Payload))) throw new Exception("Long-lived connection rejected a valid sequence.");
Check(replay.Accept(new Command(Guid.NewGuid(), 110000, "queue.clear", first.Payload)), "Long-lived trackpad connection remains usable beyond 100,000 commands");
var clock = new TestClock(); var pairing = new PairingWindow(clock); var code = pairing.Open();
Check(code.Length == 6 && pairing.TryCode(code), "Pairing code"); clock.Advance(TimeSpan.FromMinutes(5)); Check(!pairing.TryCode(code), "Pairing expiry");
code = pairing.Open(); for (var attempt = 0; attempt < 10; attempt++) pairing.TryCode("badbad"); Check(!pairing.TryCode(code), "Pairing attempt budget");
code = pairing.Open(); pairing.Close(); Check(!pairing.TryCode(code), "Closed window");
var credential = PairingWindow.CreateDevice("Test phone"); Check(PairingWindow.Matches(credential.Device, credential.Token), "Token validation"); Check(!PairingWindow.Matches(credential.Device, new string('a', 44)), "Wrong token"); Check(!credential.Device.TokenHash.Contains(credential.Token, StringComparison.Ordinal), "Store hash only");
var queue = new PlaybackQueue(); var one = queue.Enqueue("https://youtu.be/dQw4w9WgXcQ", "now"); var two = queue.Enqueue("https://youtu.be/abcdefghijk", "last");
Check(queue.CurrentEntryId == one.EntryId && queue.Items.Count == 2, "Current remains in queue");
Check(queue.Observe("a:1", one.VideoId, false, "Title") == null, "Playing does not advance");
Check(queue.Observe("a:1", one.VideoId, true, "Title")?.EntryId == two.EntryId, "Natural ended advances");
Check(queue.Observe("a:1", one.VideoId, true, "Title") == null, "Duplicate ended does not advance");
queue.Observe("a:1", two.VideoId, false, "Second"); queue.Disconnect();
Check(queue.Observe("a:1", two.VideoId, true, "Second") == null, "Reconnect to ended does not advance");
queue.Observe("a:1", "ZZZZZZZZZZZ", false, "Manual"); Check(queue.CurrentEntryId == null, "Manual navigation detaches");
queue.Move(two.EntryId, 0); Check(queue.Items[0].EntryId == two.EntryId, "Reorder");
var restored = new PlaybackQueue(queue.Snapshot()); Check(restored.Items.Count == 2, "Persisted queue");
queue.Remove(two.EntryId); Check(queue.Items.Count == 1, "Removal"); queue.Clear(); Check(queue.Items.Count == 0 && queue.CurrentEntryId == null, "Clear");
queue.Enqueue("https://youtu.be/dQw4w9WgXcQ", "now"); queue.Enqueue("https://youtu.be/abcdefghijk", "last");
queue.Disconnect(); Check(queue.Step(-1) == null, "Previous boundary"); Check(queue.Step(1)?.VideoId == "abcdefghijk", "Explicit next");
using (var stream = new MemoryStream())
{
    var unicode = Encoding.UTF8.GetBytes("{\"text\":\"Olá 🌙\"}"); await NativeFrames.WriteAsync(stream, unicode); stream.Position = 0;
    Check((await NativeFrames.ReadAsync(stream))!.SequenceEqual(unicode), "UTF-8 framing counts bytes"); Check(await NativeFrames.ReadAsync(stream) == null, "Clean EOF");
}
var badHeader = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(badHeader, 65537);
try { await NativeFrames.ReadAsync(new MemoryStream(badHeader)); throw new Exception("Accepted oversized native frame"); } catch (InvalidDataException) { passed++; }
try { await NativeFrames.ReadAsync(new MemoryStream([4, 0, 0, 0, 123])); throw new Exception("Accepted truncated native frame"); } catch (EndOfStreamException) { passed++; }
var native = """{"version":1,"type":"browser.state","activeTabId":1,"tabs":[{"platform":"youtube","tabId":1,"videoId":"dQw4w9WgXcQ","title":"Test","paused":false,"position":10,"duration":60,"ended":false,"capabilities":["playPause","seek"]}]}""";
Check(NativeProtocol.Parse(Encoding.UTF8.GetBytes(native)).GetProperty("tabs").GetArrayLength() == 1, "Native player state");
Reject(() => NativeProtocol.Parse(Encoding.UTF8.GetBytes(native.Replace("\"seek\"", "\"shell\"", StringComparison.Ordinal))), "Native action allowlist");
Reject(() => NativeProtocol.Parse(Encoding.UTF8.GetBytes(native.Replace("\"duration\":60", "\"duration\":-1", StringComparison.Ordinal))), "Negative duration");
Console.WriteLine($"PASS: {passed} Windows core checks. No desktop input was injected.");

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.Parse("2026-01-01T00:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
