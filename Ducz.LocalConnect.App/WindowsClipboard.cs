using System.Threading;

namespace Ducz.LocalConnect.App;

internal static class WindowsClipboard
{
    public static string GetText()
    {
        return RunInStaThread(() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty);
    }

    public static void SetText(string text)
    {
        RunInStaThread(() =>
        {
            if (string.IsNullOrEmpty(text))
            {
                Clipboard.Clear();
            }
            else
            {
                Clipboard.SetText(text);
            }

            return 0;
        });
    }

    private static T RunInStaThread<T>(Func<T> action)
    {
        T? result = default;
        Exception? exception = null;
        using var completed = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                completed.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        completed.Wait();

        if (exception is not null)
        {
            throw exception;
        }

        return result!;
    }
}