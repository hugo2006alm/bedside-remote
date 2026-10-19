using System.Text.Json;
using BedsideRemote.Core;

namespace BedsideRemote.Agent;

public sealed class Engine
{
    private readonly object sync = new();
    private readonly Storage storage;
    public MediaApps Apps { get; } = new();
    private readonly Func<Command, CancellationToken, Task> desktopInput;
    private static readonly SemaphoreSlim DesktopSync = new(1);
    private readonly SemaphoreSlim browserCommands = new(1);
    private readonly PlaybackQueue queue;
    private string persistedQueue;
    private readonly Dictionary<string, NativeConnection> native = [];
    private string? activeSession;
    private string? browserError;
    private string? pendingVideoId;
    private string? pendingConnectionId;
    private int? pendingTabId;
    public event Action? Changed;
    public Engine(Storage storage, Action<Command>? desktopInput = null, Func<Command, CancellationToken, Task>? desktopInputAsync = null)
    {
        this.storage = storage;
        this.desktopInput = desktopInputAsync ?? (desktopInput == null ? DesktopInput.ExecuteAsync : (command, _) => { desktopInput(command); return Task.CompletedTask; });
        queue = new(storage.Read<QueueSnapshot>("queue.json"));
        persistedQueue = JsonSerializer.Serialize(queue.Snapshot(), Protocol.Json);
    }
    public object Snapshot()
    {
        lock (sync)
        {
            var sessions = native.Values.SelectMany(connection => connection.Tabs.Select(tab => new
            {
                sessionId = SessionId(connection, tab),
                tabId = tab.GetProperty("tabId").GetInt32(),
                platform = tab.GetProperty("platform").GetString(),
                title = Shorten(tab.GetProperty("title").GetString()!, 48)
            })).OrderByDescending(x => x.sessionId == activeSession).Take(32).ToArray();
            var selected = FindActive();
            object? playback = null;
            if (selected != null)
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(selected.Value.Tab)!;
                foreach (var field in new[] { "title", "channel", "thumbnail", "error" })
                    if (values.TryGetValue(field, out var value)) values[field] = JsonSerializer.SerializeToElement(Shorten(value.GetString()!, field == "thumbnail" ? 1024 : 128));
                if (browserError != null) values["error"] = JsonSerializer.SerializeToElement(Shorten(browserError, 256));
                playback = values;
            }
            return new
            {
                version = 1,
                type = "state",
                deviceName = Environment.MachineName,
                extensionConnected = native.Count > 0,
                activeSessionId = activeSession,
                sessions,
                playback,
                queue = queue.Items.Select(x => x with { Title = Shorten(x.Title, 64) }).ToArray(),
                currentEntryId = queue.CurrentEntryId
            };
        }
    }
    public void AddNative(NativeConnection connection) { lock (sync) native.Add(connection.Id, connection); Changed?.Invoke(); }
    public void RemoveNative(string id)
    {
        lock (sync)
        {
            native.Remove(id);
            if (pendingConnectionId == id) { ClearPending(); queue.Disconnect(); }
            if (activeSession?.StartsWith(id + ":", StringComparison.Ordinal) == true) { activeSession = null; queue.Disconnect(); }
        }
        Changed?.Invoke();
    }
    public Task UpdateNativeAsync(NativeConnection connection, JsonElement message, CancellationToken token)
    {
        QueueEntry? next = null;
        lock (sync)
        {
            var previousActive = connection.ActiveTabId;
            var hadState = connection.HasState;
            connection.Tabs = message.GetProperty("tabs").EnumerateArray().Select(x => x.Clone()).ToArray();
            connection.ActiveTabId = message.TryGetProperty("activeTabId", out var active) ? active.GetInt32() : null;
            connection.HasState = true;
            var waitingForTarget = pendingVideoId != null;
            if (waitingForTarget && connection.Id == pendingConnectionId)
            {
                var target = connection.Tabs.FirstOrDefault(t => t.GetProperty("platform").GetString() == "youtube" && t.TryGetProperty("videoId", out var video) && video.GetString() == pendingVideoId && (pendingTabId == null || t.GetProperty("tabId").GetInt32() == pendingTabId));
                if (target.ValueKind != JsonValueKind.Undefined) { activeSession = SessionId(connection, target); ClearPending(); waitingForTarget = false; }
            }
            if (!waitingForTarget && hadState && connection.ActiveTabId != previousActive)
            {
                var activated = connection.Tabs.FirstOrDefault(t => t.GetProperty("tabId").GetInt32() == connection.ActiveTabId);
                if (activated.ValueKind != JsonValueKind.Undefined) { activeSession = SessionId(connection, activated); queue.Disconnect(); }
            }
            if (!waitingForTarget && activeSession == null)
            {
                var candidate = connection.Tabs.FirstOrDefault(x => x.GetProperty("tabId").GetInt32() == connection.ActiveTabId);
                if (candidate.ValueKind == JsonValueKind.Undefined) candidate = connection.Tabs.FirstOrDefault();
                if (candidate.ValueKind != JsonValueKind.Undefined) activeSession = SessionId(connection, candidate);
            }
            var selected = FindActive();
            if (waitingForTarget) { if (selected == null) activeSession = null; }
            else if (selected == null) { activeSession = null; queue.Disconnect(); }
            else if (selected.Value.Connection.Id == connection.Id)
            {
                var tab = selected.Value.Tab;
                browserError = null;
                if (tab.GetProperty("platform").GetString() == "youtube")
                {
                    next = queue.Observe(activeSession!, tab.TryGetProperty("videoId", out var video) ? video.GetString() : null,
                        tab.GetProperty("ended").GetBoolean(), tab.GetProperty("title").GetString()!);
                    PersistQueue();
                }
                else queue.Disconnect();
            }
        }
        Changed?.Invoke();
        // Keep reading native frames while awaiting a browser result.
        if (next != null) _ = Task.Run(async () =>
        {
            try
            {
                await browserCommands.WaitAsync(token);
                try
                {
                    bool current;
                    lock (sync) current = queue.CurrentEntryId == next.EntryId;
                    if (current) await OpenAsync(next, token);
                }
                finally { browserCommands.Release(); }
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or OperationCanceledException) { SetError(ex.Message); }
        }, token);
        return Task.CompletedTask;
    }
    public static bool IsImmediate(Command command) => command.Action.StartsWith("input.", StringComparison.Ordinal) || command.Action == "media.command" && command.Payload.GetProperty("command").GetString() is not ("seekForward" or "seekBackward");
    public async Task ExecuteAsync(Command command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (command.Action == "app.launch") { Apps.Launch(command.Payload.GetProperty("appId").GetString()!); return; }
        if (IsImmediate(command))
        {
            if (!DesktopSync.Wait(0)) throw new InvalidOperationException("Desktop input is busy. Wait for it to finish; this action was not queued.");
            try { token.ThrowIfCancellationRequested(); await desktopInput(command, token); }
            finally { DesktopSync.Release(); }
            return;
        }
        await browserCommands.WaitAsync(token);
        try { await ExecuteBrowserAsync(command, token); }
        finally { browserCommands.Release(); }
    }
    private async Task ExecuteBrowserAsync(Command command, CancellationToken token)
    {
        if (command.Action == "browser.navigate")
        {
            var key = command.Payload.GetProperty("key").GetString()!;
            var action = key switch { "up" => "navigateUp", "down" => "navigateDown", "left" => "navigateLeft", "right" => "navigateRight", "enter" => "navigateEnter", "escape" => "navigateBack", _ => throw new FormatException("Unsupported navigation key.") };
            NativeConnection connection; int tabId;
            lock (sync)
            {
                var selected = FindActive() ?? throw new InvalidOperationException("Select a browser session before navigating.");
                connection = selected.Connection; tabId = selected.Tab.GetProperty("tabId").GetInt32();
            }
            CheckResult(await connection.CommandAsync(new() { ["command"] = action, ["tabId"] = tabId }, token));
            return;
        }
        if (command.Action == "media.command")
        {
            var value = command.Payload.GetProperty("command").GetString();
            if (value is "seekForward" or "seekBackward")
            {
                double position;
                lock (sync)
                {
                    var selected = FindActive() ?? throw new InvalidOperationException("No browser player is selected.");
                    position = Math.Max(0, selected.Tab.GetProperty("position").GetDouble() + (value == "seekForward" ? 10 : -10));
                }
                await BrowserCommandAsync("seek", position, token);
            }
            return;
        }
        var p = command.Payload;
        QueueEntry? open = null;
        lock (sync)
        {
            switch (command.Action)
            {
                case "browser.select":
                    var selectedId = p.GetProperty("sessionId").GetString()!;
                    if (!native.Values.Any(c => c.Tabs.Any(t => SessionId(c, t) == selectedId))) throw new InvalidOperationException("Browser session is unavailable.");
                    break;
                case "youtube.enqueue":
                    EnsureExtension();
                    var entry = queue.Enqueue(p.GetProperty("url").GetString()!, p.GetProperty("mode").GetString()!);
                    if (p.GetProperty("mode").GetString() == "now") open = entry;
                    break;
                case "queue.next": EnsureExtension(); open = queue.Step(1); break;
                case "queue.previous": EnsureExtension(); open = queue.Step(-1); break;
                case "queue.remove":
                    if (queue.CurrentEntryId == p.GetProperty("entryId").GetString()) ClearPending();
                    queue.Remove(p.GetProperty("entryId").GetString()!); break;
                case "queue.move": queue.Move(p.GetProperty("entryId").GetString()!, p.GetProperty("index").GetInt32()); break;
                case "queue.clear": ClearPending(); queue.Clear(); break;
            }
            PersistQueue();
        }
        Changed?.Invoke();
        if (command.Action == "browser.select") await BrowserSelectAsync(p.GetProperty("sessionId").GetString()!, token);
        if (command.Action == "browser.command") await BrowserCommandAsync(p.GetProperty("command").GetString()!, p.TryGetProperty("position", out var pos) ? pos.GetDouble() : null, token);
        if (open != null) await OpenAsync(open, token);
    }
    private async Task BrowserSelectAsync(string sessionId, CancellationToken token)
    {
        NativeConnection connection; int tabId;
        lock (sync)
        {
            var target = native.Values.SelectMany(c => c.Tabs.Select(t => (Connection: c, Tab: t))).FirstOrDefault(x => SessionId(x.Connection, x.Tab) == sessionId);
            connection = target.Connection ?? throw new InvalidOperationException("Browser session is unavailable.");
            tabId = target.Tab.GetProperty("tabId").GetInt32();
        }
        CheckResult(await connection.CommandAsync(new() { ["command"] = "selectTab", ["tabId"] = tabId }, token));
        lock (sync)
        {
            if (!native.ContainsKey(connection.Id) || !connection.Tabs.Any(t => t.GetProperty("tabId").GetInt32() == tabId)) throw new InvalidOperationException("Browser session closed while selecting it.");
            activeSession = sessionId; ClearPending(); queue.Disconnect();
        }
        Changed?.Invoke();
    }
    private async Task BrowserCommandAsync(string command, double? position, CancellationToken token)
    {
        NativeConnection connection; Dictionary<string, object?> message;
        lock (sync)
        {
            var selected = FindActive() ?? throw new InvalidOperationException("No browser player is selected.");
            if (!selected.Tab.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == command)) throw new InvalidOperationException("This action is unavailable for the selected player.");
            connection = selected.Connection;
            message = new() { ["command"] = command, ["tabId"] = selected.Tab.GetProperty("tabId").GetInt32() };
            if (position != null) message["position"] = position;
        }
        CheckResult(await connection.CommandAsync(message, token));
    }
    private async Task OpenAsync(QueueEntry entry, CancellationToken token)
    {
        NativeConnection connection; Dictionary<string, object?> message = new() { ["command"] = "openYoutube", ["url"] = entry.Url };
        lock (sync)
        {
            EnsureExtension();
            var selected = FindActive();
            var youtube = native.Values.SelectMany(c => c.Tabs.Where(t => t.GetProperty("platform").GetString() == "youtube").Select(t => (Connection: c, Tab: t))).FirstOrDefault();
            if (selected?.Tab.GetProperty("platform").GetString() == "youtube") youtube = selected.Value;
            if (youtube.Connection != null)
            {
                connection = youtube.Connection; message["tabId"] = youtube.Tab.GetProperty("tabId").GetInt32(); activeSession = SessionId(connection, youtube.Tab);
            }
            else { connection = native.Values.First(); activeSession = null; }
            pendingVideoId = entry.VideoId; pendingConnectionId = connection.Id;
            pendingTabId = message.TryGetValue("tabId", out var tabId) ? (int)tabId! : null;
        }
        try { CheckResult(await connection.CommandAsync(message, token)); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            lock (sync)
            {
                if (pendingVideoId == entry.VideoId) { ClearPending(); queue.CancelSelection(); PersistQueue(); }
            }
            SetError(ex.Message); throw;
        }
    }
    private void CheckResult(JsonElement result)
    {
        if (!result.GetProperty("ok").GetBoolean())
        {
            var error = result.TryGetProperty("error", out var text) ? text.GetString()! : "Browser rejected this action.";
            SetError(error); throw new InvalidOperationException(error);
        }
    }
    private void SetError(string error) { lock (sync) browserError = error; Changed?.Invoke(); }
    private void EnsureExtension() { if (native.Count == 0) throw new InvalidOperationException("Connect the browser companion: Zen/Firefox for YouTube, or Edge for the Netflix Store app."); }
    private void PersistQueue()
    {
        var snapshot = queue.Snapshot();
        var serialized = JsonSerializer.Serialize(snapshot, Protocol.Json);
        if (serialized == persistedQueue) return;
        storage.Save("queue.json", snapshot);
        persistedQueue = serialized;
    }
    private void ClearPending() { pendingVideoId = null; pendingConnectionId = null; pendingTabId = null; }
    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..length];
    private static string SessionId(NativeConnection connection, JsonElement tab) => $"{connection.Id}:{tab.GetProperty("tabId").GetInt32()}";
    private (NativeConnection Connection, JsonElement Tab)? FindActive()
    {
        foreach (var connection in native.Values)
            foreach (var tab in connection.Tabs)
                if (SessionId(connection, tab) == activeSession) return (connection, tab);
        return null;
    }
}
