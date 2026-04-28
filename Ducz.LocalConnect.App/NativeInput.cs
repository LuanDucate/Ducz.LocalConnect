using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ducz.LocalConnect.App;

internal static class NativeInput
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventWheel = 0x0800;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;

    public static void Apply(RemoteInputMessage message)
    {
        switch (message.Type)
        {
            case PacketType.MouseMove:
                MoveCursor(message.X, message.Y);
                break;
            case PacketType.MouseDown:
                MoveCursor(message.X, message.Y);
                SendMouseButton(message.Button, isDown: true);
                break;
            case PacketType.MouseUp:
                MoveCursor(message.X, message.Y);
                SendMouseButton(message.Button, isDown: false);
                break;
            case PacketType.MouseWheel:
                MoveCursor(message.X, message.Y);
                SendMouseWheel(message.Delta);
                break;
            case PacketType.KeyDown:
                SendKeyboard((ushort)message.KeyCode, isDown: true);
                break;
            case PacketType.KeyUp:
                SendKeyboard((ushort)message.KeyCode, isDown: false);
                break;
        }
    }

    private static void SendMouseButton(RemoteMouseButton button, bool isDown)
    {
        var flags = button switch
        {
            RemoteMouseButton.Left => isDown ? MouseEventLeftDown : MouseEventLeftUp,
            RemoteMouseButton.Right => isDown ? MouseEventRightDown : MouseEventRightUp,
            RemoteMouseButton.Middle => isDown ? MouseEventMiddleDown : MouseEventMiddleUp,
            _ => 0u
        };

        SendMouse(flags, 0);
    }

    private static void SendMouseWheel(int delta)
    {
        SendMouse(MouseEventWheel, delta);
    }

    private static void SendMouse(uint flags, int mouseData)
    {
        var input = new INPUT
        {
            type = InputMouse,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flags,
                    mouseData = mouseData
                }
            }
        };

        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the remote mouse event.");
        }
    }

    private static void SendKeyboard(ushort keyCode, bool isDown)
    {
        var scanCode = (ushort)MapVirtualKey(keyCode, 0);
        var input = new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wScan = scanCode,
                    dwFlags = KeyEventScanCode | (isDown ? 0u : KeyEventKeyUp)
                }
            }
        };

        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the remote keyboard event.");
        }
    }

    private static void MoveCursor(int x, int y)
    {
        if (!SetCursorPos(x, y))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the remote cursor movement.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInputStructure);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }
}