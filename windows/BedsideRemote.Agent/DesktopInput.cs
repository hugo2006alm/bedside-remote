using System.Runtime.InteropServices;
using System.Text;
using BedsideRemote.Core;

namespace BedsideRemote.Agent;

public static class DesktopInput
{
    private static readonly PointerDrag Drag = new((flags, x, y) => MouseEvent(flags, x, y), GetForegroundWindow);
    private static readonly Dictionary<string, ushort> Keys = new()
    {
        ["up"] = 0x26,
        ["down"] = 0x28,
        ["left"] = 0x25,
        ["right"] = 0x27,
        ["enter"] = 0x0D,
        ["escape"] = 0x1B,
        ["home"] = 0x5B,
        ["backspace"] = 8,
        ["tab"] = 9,
        ["space"] = 0x20,
        ["playPause"] = 0xB3,
        ["next"] = 0xB0,
        ["previous"] = 0xB1,
        ["volumeUp"] = 0xAF,
        ["volumeDown"] = 0xAE,
        ["mute"] = 0xAD
    };
    public static async Task ExecuteAsync(Command command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (command.Action == "input.drag")
        {
            var payload = command.Payload;
            Drag.Handle(payload.GetProperty("phase").GetString()!, (int)Math.Round(payload.GetProperty("dx").GetDouble()), (int)Math.Round(payload.GetProperty("dy").GetDouble()), token);
            return;
        }
        if (Drag.Active) throw new InvalidOperationException("Finish the current drag before sending another desktop action.");
        if (command.Action == "input.navigate")
        {
            DesktopNavigation.Execute(command.Payload.GetProperty("key").GetString()!, token);
            return;
        }
        if (command.Action == "input.text")
        {
            var target = GetForegroundWindow();
            if (target == IntPtr.Zero) throw new InvalidOperationException("Focus a text field before sending text.");
            if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0))
                throw new InvalidOperationException("Release modifier keys before sending text.");
            var layout = GetKeyboardLayout(GetWindowThreadProcessId(target, out _));
            await TypeTextAsync(command.Payload.GetProperty("text").GetString()!, packet =>
            {
                if (GetForegroundWindow() != target)
                    throw new InvalidOperationException("Text input stopped because the focused window changed. Some text may already have been entered.");
                Send(packet);
            }, Task.Delay, token, character => VkKeyScanEx(character, layout), (GetKeyState(0x14) & 1) != 0);
            return;
        }
        Execute(command);
    }
    internal static async Task TypeTextAsync(string text, Action<Input[]> emit, Func<int, CancellationToken, Task> pause, CancellationToken token, Func<char, short>? keyMapping = null, bool capsLock = false)
    {
        // Prefer layout keystrokes: composition-aware editors can mishandle rapid VK_PACKET input.
        // For Unicode fallback, let each UTF-16 unit reach the editor before the next packet.
        foreach (var scalar in text.EnumerateRunes())
        {
            token.ThrowIfCancellationRequested();
            var mapped = scalar.IsBmp && !Rune.IsControl(scalar) ? keyMapping?.Invoke((char)scalar.Value) ?? -1 : -1;
            var shiftState = (mapped >> 8) & 255;
            if (mapped != -1 && shiftState is 0 or 1)
            {
                var key = (ushort)(mapped & 255);
                var shift = shiftState == 1;
                if (key is >= 0x41 and <= 0x5A && capsLock) shift = !shift;
                var events = new List<Input>();
                if (shift) events.Add(KeyInput(0x10, false));
                events.Add(KeyInput(key, false)); events.Add(KeyInput(key, true));
                if (shift) events.Add(KeyInput(0x10, true));
                emit(events.ToArray());
                await pause(10, token);
                continue;
            }
            var units = scalar.ToString();
            for (var index = 0; index < units.Length; index++)
            {
                emit([
                    new Input { Type = 1, Data = new Union { Keyboard = new Keyboard { Scan = units[index], Flags = 4 } } },
                    new Input { Type = 1, Data = new Union { Keyboard = new Keyboard { Scan = units[index], Flags = 6 } } }
                ]);
                // Complete a started surrogate pair before honoring cancellation.
                await pause(50, index < units.Length - 1 ? CancellationToken.None : token);
            }
        }
    }
    private static Input KeyInput(ushort key, bool released) => new() { Type = 1, Data = new Union { Keyboard = new Keyboard { VirtualKey = key, Flags = released ? 2u : 0 } } };
    private static void Execute(Command command)
    {
        var p = command.Payload;
        switch (command.Action)
        {
            case "input.key": Key(Keys[p.GetProperty("key").GetString()!]); break;
            case "input.pointer": MouseEvent(1, (int)Math.Round(p.GetProperty("dx").GetDouble()), (int)Math.Round(p.GetProperty("dy").GetDouble())); break;
            case "input.click":
                var left = p.GetProperty("button").GetString() == "left";
                Send(new Input { Data = new Union { Mouse = new Mouse { Flags = left ? 2u : 8u } } }, new Input { Data = new Union { Mouse = new Mouse { Flags = left ? 4u : 16u } } });
                break;
            case "input.scroll": MouseEvent(0x800, data: unchecked((uint)p.GetProperty("delta").GetInt32())); break;
            case "media.command":
                var action = p.GetProperty("command").GetString()!;
                if (action is "seekForward" or "seekBackward")
                    throw new InvalidOperationException("Seeking requires an active browser player; select its seek controls.");
                Key(Keys[action]); break;
        }
    }
    private static void Key(ushort key) => Send(new Input { Type = 1, Data = new Union { Keyboard = new Keyboard { VirtualKey = key } } }, new Input { Type = 1, Data = new Union { Keyboard = new Keyboard { VirtualKey = key, Flags = 2 } } });
    internal static void NavigationKey(string key) => Key(Keys[key]);
    private static void MouseEvent(uint flags, int x = 0, int y = 0, uint data = 0) => Send(new Input { Data = new Union { Mouse = new Mouse { X = x, Y = y, Data = data, Flags = flags } } });
    private static void Send(params Input[] inputs) => SendBatch(inputs, batch => SendInput((uint)batch.Length, batch, Marshal.SizeOf<Input>()));
    internal static void SendBatch(Input[] inputs, Func<Input[], uint> emit)
    {
        var accepted = emit(inputs);
        if (accepted != inputs.Length)
        {
            // Release only our own accepted presses that have no accepted matching release.
            var keys = new Dictionary<(ushort Key, ushort Scan, uint Flags), Input>();
            var mouse = new HashSet<uint>();
            foreach (var input in inputs.Take((int)Math.Min(accepted, (uint)inputs.Length)))
            {
                if (input.Type == 1)
                {
                    var key = input.Data.Keyboard;
                    var identity = (key.VirtualKey, key.Scan, key.Flags & ~2u);
                    if ((key.Flags & 2) != 0) keys.Remove(identity);
                    else { var released = input; released.Data.Keyboard.Flags |= 2; keys[identity] = released; }
                }
                else
                {
                    foreach (var (down, up) in new[] { (2u, 4u), (8u, 16u), (32u, 64u) })
                    {
                        if ((input.Data.Mouse.Flags & down) != 0) mouse.Add(up);
                        if ((input.Data.Mouse.Flags & up) != 0) mouse.Remove(up);
                    }
                }
            }
            var cleanup = keys.Values.Reverse().Concat(mouse.Select(flags => new Input { Data = new Union { Mouse = new Mouse { Flags = flags } } })).ToArray();
            if (cleanup.Length != 0) _ = emit(cleanup);
            throw new InvalidOperationException("Windows could not deliver input. Elevated apps and the secure desktop cannot be controlled.");
        }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScanEx(char character, IntPtr layout);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public Union Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct Union { [FieldOffset(0)] public Mouse Mouse; [FieldOffset(0)] public Keyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)] internal struct Mouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Keyboard { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr Extra; }
}
