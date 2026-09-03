using System.Runtime.InteropServices;

namespace Ducz.LocalConnect.Core.Input;

public static class SecureAttention
{
    public const string PolicyHint =
        "Windows only accepts a software Ctrl+Alt+Del when the host's policy allows it: "
        + "run gpedit.msc → Computer Configuration → Administrative Templates → Windows Components → "
        + "Windows Logon Options → \"Disable or enable software Secure Attention Sequence\" → Enabled, "
        + "\"Services and Ease of Access applications\" (or set HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\\SoftwareSASGeneration = 3).";

    public static bool TrySend(out string? error)
    {
        try
        {
            SendSAS(asUser: false);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            error = "This Windows edition does not provide sas.dll.";
            return false;
        }
    }

    [DllImport("sas.dll")]
    private static extern void SendSAS([MarshalAs(UnmanagedType.Bool)] bool asUser);
}
