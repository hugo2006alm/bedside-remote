using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BedsideRemote.Core;

public sealed record Command(Guid Id, long Sequence, string Action, JsonElement Payload);

public static partial class Protocol
{
    public const int MaxBytes = 65536;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] BrowserCommands = ["playPause", "seek", "skipIntro", "skipRecap", "nextEpisode", "fullscreen", "subtitles", "audio"];
    public static void Fields(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new FormatException("Expected an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!allowed.Contains(field.Name, StringComparer.Ordinal) || !seen.Add(field.Name)) throw new FormatException("Unknown or duplicate field.");
    }
    public static string Text(JsonElement value, string key, int max = 256)
    {
        var field = value.GetProperty(key);
        if (field.ValueKind != JsonValueKind.String) throw new FormatException("Expected text.");
        var text = field.GetString()!;
        if (text.Length is 0 || text.Length > max) throw new FormatException("Text length is invalid.");
        return text;
    }
    public static double Number(JsonElement value, string key, double min, double max)
    {
        if (!value.GetProperty(key).TryGetDouble(out var number) || !double.IsFinite(number) || number < min || number > max)
            throw new FormatException("Number is outside its allowed range.");
        return number;
    }
    public static Command ParseCommand(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaxBytes) throw new FormatException("Message is too large.");
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = doc.RootElement;
        Fields(root, "version", "type", "id", "sequence", "action", "payload");
        if (root.GetProperty("version").GetInt32() != 1 || Text(root, "type") != "command") throw new FormatException("Unsupported protocol.");
        if (!Guid.TryParse(Text(root, "id"), out var id)) throw new FormatException("Invalid command ID.");
        var sequence = root.GetProperty("sequence").GetInt64();
        if (sequence <= 0) throw new FormatException("Invalid sequence.");
        var action = Text(root, "action");
        var p = root.GetProperty("payload");
        switch (action)
        {
            case "input.key": Fields(p, "key"); OneOf(Text(p, "key"), ["up", "down", "left", "right", "enter", "escape", "home", "backspace", "tab", "space"]); break;
            case "input.text": Fields(p, "text"); Text(p, "text", 4096); break;
            case "input.pointer": Fields(p, "dx", "dy"); Number(p, "dx", -1000, 1000); Number(p, "dy", -1000, 1000); break;
            case "input.click": Fields(p, "button"); OneOf(Text(p, "button"), ["left", "right"]); break;
            case "input.scroll": Fields(p, "delta"); Integer(p, "delta", -1200, 1200); break;
            case "media.command": Fields(p, "command"); OneOf(Text(p, "command"), ["playPause", "next", "previous", "volumeUp", "volumeDown", "mute", "seekForward", "seekBackward"]); break;
            case "browser.command":
                Fields(p, "command", "position"); var name = Text(p, "command"); OneOf(name, BrowserCommands);
                if (name == "seek") Number(p, "position", 0, double.MaxValue);
                else if (p.TryGetProperty("position", out _)) throw new FormatException("Position only applies to seek.");
                break;
            case "browser.select": Fields(p, "sessionId"); Text(p, "sessionId"); break;
            case "youtube.enqueue": Fields(p, "url", "mode"); YoutubeId(Text(p, "url", 2048)); OneOf(Text(p, "mode"), ["now", "next", "last"]); break;
            case "queue.remove": Fields(p, "entryId"); Text(p, "entryId"); break;
            case "queue.move": Fields(p, "entryId", "index"); Text(p, "entryId"); Integer(p, "index", 0, int.MaxValue); break;
            case "queue.clear": case "queue.next": case "queue.previous": Fields(p); break;
            default: throw new FormatException("Unsupported action.");
        }
        return new(id, sequence, action, p.Clone());
    }
    public static int Integer(JsonElement p, string key, int min, int max)
    {
        var result = p.GetProperty(key).GetInt32();
        if (result < min || result > max) throw new FormatException("Integer outside its allowed range.");
        return result;
    }
    public static void OneOf(string text, string[] allowed)
    {
        if (!allowed.Contains(text, StringComparer.Ordinal)) throw new FormatException("Unsupported value.");
    }
    public static string YoutubeId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new FormatException("Use an official HTTPS YouTube video link.");
        OneOf(uri.Host, ["youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be"]);
        string? id = null;
        if (uri.Host == "youtu.be") id = uri.AbsolutePath.TrimStart('/');
        else if (uri.AbsolutePath == "/watch")
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&'))
            {
                var pieces = pair.Split('=', 2);
                if (pieces[0] != "v" || pieces.Length != 2) continue;
                if (id != null) throw new FormatException("Duplicate video ID.");
                id = Uri.UnescapeDataString(pieces[1]);
            }
        }
        else if (uri.AbsolutePath.StartsWith("/shorts/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/live/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/embed/", StringComparison.Ordinal))
            id = uri.AbsolutePath.Split('/')[2];
        if (id == null || !VideoIdPattern().IsMatch(id) || (uri.AbsolutePath != "/watch" && uri.AbsolutePath.Trim('/').Split('/').Length != (uri.Host == "youtu.be" ? 1 : 2)))
            throw new FormatException("Invalid YouTube video ID.");
        return id;
    }
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdPattern();
    public static bool IsLan(IPAddress? address)
    {
        if (address == null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        var b = address.GetAddressBytes();
        return b.Length == 4 ? b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31 : address.IsIPv6LinkLocal || (b[0] & 0xfe) == 0xfc;
    }
}

public sealed class ReplayGuard
{
    private long lastSequence;
    private readonly HashSet<Guid> ids = [];
    private readonly Queue<Guid> order = new();
    public bool Accept(Command command)
    {
        if (command.Sequence <= lastSequence) return false;
        lastSequence = command.Sequence;
        if (!ids.Add(command.Id)) return false;
        order.Enqueue(command.Id);
        if (order.Count > 8192) ids.Remove(order.Dequeue());
        return true;
    }
}
