using System.Runtime.InteropServices;

namespace Ducz.LocalConnect.Core.Clipboard;

public static class ClipboardService
{
    private const uint UnicodeTextFormat = 13; // CF_UNICODETEXT
    private const uint GlobalMoveable = 0x0002; // GMEM_MOVEABLE
    private const int OpenAttempts = 10;
    private const int RetryDelayMilliseconds = 15;

    public static string GetText()
    {
        return WithClipboard(() =>
        {
            if (!IsClipboardFormatAvailable(UnicodeTextFormat))
            {
                return string.Empty;
            }

            var handle = GetClipboardData(UnicodeTextFormat);
            if (handle == IntPtr.Zero)
            {
                return string.Empty;
            }

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                return Marshal.PtrToStringUni(pointer) ?? string.Empty;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        });
    }

    public static void SetText(string text)
    {
        WithClipboard(() =>
        {
            EmptyClipboard();
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var byteCount = (text.Length + 1) * sizeof(char);
            var handle = GlobalAlloc(GlobalMoveable, (nuint)byteCount);
            if (handle == IntPtr.Zero)
            {
                throw new OutOfMemoryException("Could not allocate clipboard memory.");
            }

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                GlobalFree(handle);
                throw new InvalidOperationException("Could not lock clipboard memory.");
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                Marshal.WriteInt16(pointer, text.Length * sizeof(char), 0);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            // On success the system owns the memory; only free it if the call failed.
            if (SetClipboardData(UnicodeTextFormat, handle) == IntPtr.Zero)
            {
                GlobalFree(handle);
                throw new InvalidOperationException("Windows refused the clipboard data.");
            }

            return 0;
        });
    }

    private static T WithClipboard<T>(Func<T> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    return action();
                }
                finally
                {
                    CloseClipboard();
                }
            }

            if (attempt >= OpenAttempts)
            {
                throw new InvalidOperationException("The clipboard is in use by another application.");
            }

            Thread.Sleep(RetryDelayMilliseconds);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);
}
