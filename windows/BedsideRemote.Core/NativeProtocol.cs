using System.Text.Json;

namespace BedsideRemote.Core;

public static class NativeProtocol
{
    public static JsonElement Parse(ReadOnlyMemory<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.GetProperty("version").GetInt32() != 1) throw new FormatException("Unsupported native protocol.");
        var type = Protocol.Text(root, "type");
        if (type == "browser.result")
        {
            Protocol.Fields(root, "version", "type", "id", "ok", "error");
            if (!Guid.TryParse(Protocol.Text(root, "id"), out _)) throw new FormatException("Invalid result ID.");
            root.GetProperty("ok").GetBoolean();
            if (root.TryGetProperty("error", out _)) Protocol.Text(root, "error", 1024);
        }
        else if (type == "browser.state")
        {
            Protocol.Fields(root, "version", "type", "activeTabId", "tabs");
            if (root.TryGetProperty("activeTabId", out _)) Protocol.Integer(root, "activeTabId", 0, int.MaxValue);
            var tabs = root.GetProperty("tabs");
            if (tabs.ValueKind != JsonValueKind.Array || tabs.GetArrayLength() > 32) throw new FormatException("Invalid tab list (32 players maximum).");
            var ids = new HashSet<int>();
            foreach (var tab in tabs.EnumerateArray())
            {
                Protocol.Fields(tab, "platform", "tabId", "videoId", "title", "channel", "thumbnail", "paused", "position", "duration", "ended", "capabilities", "error");
                Protocol.OneOf(Protocol.Text(tab, "platform"), ["youtube", "netflix"]);
                if (!ids.Add(Protocol.Integer(tab, "tabId", 0, int.MaxValue))) throw new FormatException("Duplicate tab.");
                var title = tab.GetProperty("title");
                if (title.ValueKind != JsonValueKind.String || title.GetString()!.Length > 512) throw new FormatException("Invalid title.");
                foreach (var key in new[] { "videoId", "channel", "thumbnail", "error" })
                    if (tab.TryGetProperty(key, out var item) && (item.ValueKind != JsonValueKind.String || item.GetString()!.Length > 2048)) throw new FormatException("Invalid metadata.");
                if (tab.TryGetProperty("videoId", out var video)) Protocol.YoutubeId("https://youtu.be/" + video.GetString());
                tab.GetProperty("paused").GetBoolean(); tab.GetProperty("ended").GetBoolean();
                Protocol.Number(tab, "position", 0, double.MaxValue); Protocol.Number(tab, "duration", 0, double.MaxValue);
                var caps = tab.GetProperty("capabilities");
                if (caps.ValueKind != JsonValueKind.Array || caps.GetArrayLength() > 8) throw new FormatException("Invalid capabilities.");
                foreach (var capability in caps.EnumerateArray()) Protocol.OneOf(capability.GetString()!, Protocol.BrowserCommands);
            }
        }
        else throw new FormatException("Unknown native message.");
        return root.Clone();
    }
}
