using System.Runtime.InteropServices;
using BedsideRemote.Core;

namespace BedsideRemote.Agent;

public static class DesktopInput
{
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
    public static void Execute(Command command)
    {
        var p = command.Payload;
        switch (command.Action)
        {
            case "input.key": Key(Keys[p.GetProperty("key").GetString()!]); break;
            case "input.text":
                var text = p.GetProperty("text").GetString()!;
                var events = new Input[text.Length * 2];
                for (var index = 0; index < text.Length; index++)
                {
                    events[index * 2] = new Input { Type = 1, Data = new Union { Keyboard = new Keyboard { Scan = text[index], Flags = 4 } } };
                    events[index * 2 + 1] = new Input { Type = 1, Data = new Union { Keyboard = new Keyboard { Scan = text[index], Flags = 6 } } };
                }
                Send(events);
                break;
            case "input.pointer": MouseEvent(1, (int)Math.Round(p.GetProperty("dx").GetDouble()), (int)Math.Round(p.GetProperty("dy").GetDouble())); break;
            case "input.click": var left = p.GetProperty("button").GetString() == "left"; MouseEvent(left ? 2u : 8u); MouseEvent(left ? 4u : 16u); break;
            case "input.scroll": MouseEvent(0x800, data: unchecked((uint)p.GetProperty("delta").GetInt32())); break;
            case "media.command":
                var action = p.GetProperty("command").GetString()!;
                if (action is "seekForward" or "seekBackward")
                    throw new InvalidOperationException("Seeking requires an active browser player; select its seek controls.");
                Key(Keys[action]); break;
        }
    }
    private static void Key(ushort key) => Send(new Input { Type = 1, Data = new Union { Keyboard = new Keyboard { VirtualKey = key } } }, new Input { Type = 1, Data = new Union { Keyboard = new Keyboard { VirtualKey = key, Flags = 2 } } });
    private static void MouseEvent(uint flags, int x = 0, int y = 0, uint data = 0) => Send(new Input { Data = new Union { Mouse = new Mouse { X = x, Y = y, Data = data, Flags = flags } } });
    private static void Send(params Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new InvalidOperationException("Windows could not deliver input. Elevated apps and the secure desktop cannot be controlled.");
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Union Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Union { [FieldOffset(0)] public Mouse Mouse; [FieldOffset(0)] public Keyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr Extra; }
}
