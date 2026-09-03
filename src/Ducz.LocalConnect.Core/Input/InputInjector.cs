using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Ducz.LocalConnect.Core.Protocol;

namespace Ducz.LocalConnect.Core.Input;

public sealed class InputInjector : IDisposable
{
    private readonly Channel<RemoteInputMessage> _queue = Channel.CreateUnbounded<RemoteInputMessage>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = true,
    });

    private readonly Thread _thread;
    private readonly Func<System.Drawing.Point> _origin;

    public InputInjector(Func<System.Drawing.Point> originProvider)
    {
        _origin = originProvider;
        _thread = new Thread(ApplyLoop)
        {
            IsBackground = true,
            Name = "Ducz input injector",
        };
        _thread.Start();
    }

    public event Action<Exception>? Failed;

    public void Enqueue(RemoteInputMessage message) => _queue.Writer.TryWrite(message);

    public void Dispose() => _queue.Writer.TryComplete();

    private void ApplyLoop()
    {
        var reader = _queue.Reader;
        while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            if (!reader.TryRead(out var current))
            {
                continue;
            }

            while (reader.TryRead(out var next))
            {
                if (current.Type == PacketType.MouseMove && next.Type == PacketType.MouseMove)
                {
                    current = next;
                    continue;
                }

                Apply(current);
                current = next;
            }

            Apply(current);
        }
    }

    private void Apply(RemoteInputMessage message)
    {
        try
        {
            switch (message.Type)
            {
                case PacketType.MouseMove:
                    MoveCursor(message.X, message.Y);
                    break;
                case PacketType.MouseDown:
                case PacketType.MouseUp:
                    MoveCursor(message.X, message.Y);
                    SendMouse(ButtonFlags(message.Button, message.Type == PacketType.MouseDown), 0);
                    break;
                case PacketType.MouseWheel:
                    MoveCursor(message.X, message.Y);
                    SendMouse(MouseEventWheel, message.Delta);
                    break;
                case PacketType.KeyDown:
                case PacketType.KeyUp:
                    SendKey((ushort)message.KeyCode, message.Type == PacketType.KeyDown);
                    break;
            }
        }
        catch (Win32Exception exception)
        {
            Failed?.Invoke(exception);
        }
    }

    private static uint ButtonFlags(RemoteMouseButton button, bool isDown) => button switch
    {
        RemoteMouseButton.Left => isDown ? MouseEventLeftDown : MouseEventLeftUp,
        RemoteMouseButton.Right => isDown ? MouseEventRightDown : MouseEventRightUp,
        RemoteMouseButton.Middle => isDown ? MouseEventMiddleDown : MouseEventMiddleUp,
        _ => 0u,
    };

    private void MoveCursor(int x, int y)
    {
        var origin = _origin();
        if (!SetCursorPos(origin.X + x, origin.Y + y))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the remote cursor movement.");
        }
    }

    private static void SendMouse(uint flags, int mouseData)
    {
        var input = new NativeInput
        {
            Type = InputMouse,
            Union = new InputUnion
            {
                Mouse = new MouseInput { Flags = flags, MouseData = mouseData },
            },
        };
        Send(ref input, "Windows rejected the remote mouse event.");
    }

    private static void SendKey(ushort virtualKey, bool isDown)
    {
        var scanCode = (ushort)MapVirtualKey(virtualKey, MapVkToVsc);
        var flags = KeyEventScanCode;
        if (!isDown)
        {
            flags |= KeyEventKeyUp;
        }

        if (IsExtendedKey(virtualKey))
        {
            flags |= KeyEventExtendedKey;
        }

        var input = new NativeInput
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput { VirtualKey = virtualKey, ScanCode = scanCode, Flags = flags },
            },
        };
        Send(ref input, "Windows rejected the remote keyboard event.");
    }

    private static void Send(ref NativeInput input, string failureMessage)
    {
        if (SendInput(1, ref input, Marshal.SizeOf<NativeInput>()) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), failureMessage);
        }
    }

    private static bool IsExtendedKey(ushort virtualKey) => virtualKey switch
    {
        >= 0x21 and <= 0x28 => true, // PageUp, PageDown, End, Home, Left, Up, Right, Down
        0x2C or 0x2D or 0x2E => true, // PrintScreen, Insert, Delete
        0x5B or 0x5C or 0x5D => true, // LWin, RWin, Apps
        0x6F => true,                 // Numpad divide
        0x90 => true,                 // NumLock
        0xA3 or 0xA5 => true,         // RControl, RMenu
        _ => false,
    };

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MapVkToVsc = 0;

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventWheel = 0x0800;

    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, ref NativeInput input, int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public int MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
}
