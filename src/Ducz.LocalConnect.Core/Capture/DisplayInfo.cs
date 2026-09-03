using System.Drawing;
using System.Runtime.InteropServices;

namespace Ducz.LocalConnect.Core.Capture;

public sealed record DisplayInfo(string DeviceName, Rectangle Bounds, bool IsPrimary)
{
    private const uint MonitorInfoPrimary = 0x1;

    public string BuildDisplayName(int index)
    {
        var primarySuffix = IsPrimary ? " (Primary)" : string.Empty;
        return $"Monitor {index + 1}: {Bounds.Width}x{Bounds.Height} at {Bounds.X},{Bounds.Y}{primarySuffix}";
    }

    public static IReadOnlyList<DisplayInfo> GetAll()
    {
        var displays = new List<DisplayInfo>();

        bool Callback(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data)
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var bounds = Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
                displays.Add(new DisplayInfo(info.Device, bounds, (info.Flags & MonitorInfoPrimary) != 0));
            }

            return true;
        }

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero) || displays.Count == 0)
        {
            throw new InvalidOperationException("No display was found.");
        }

        return displays.OrderByDescending(display => display.IsPrimary).ToArray();
    }

    /// <summary>Finds a display by device name, falling back to the primary one.</summary>
    public static DisplayInfo Resolve(string? deviceName)
    {
        var displays = GetAll();
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var match = displays.FirstOrDefault(display => string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return displays.First(display => display.IsPrimary);
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }
}
