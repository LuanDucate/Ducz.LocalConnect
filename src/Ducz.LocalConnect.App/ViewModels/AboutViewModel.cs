using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ducz.LocalConnect.App.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public const string RepositoryUrl = "https://github.com/LuanDucate/Ducz.LocalConnect";
    public const string AuthorUrl = "https://github.com/LuanDucate";

    public string Version
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(informational))
            {
                return assembly.GetName().Version?.ToString(3) ?? "2.0.0";
            }

            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? informational[..plus] : informational;
        }
    }

    public string RuntimeText => $".NET {Environment.Version} · Windows {Environment.OSVersion.Version}";

    [RelayCommand]
    private void OpenRepository() => OpenUrl(RepositoryUrl);

    [RelayCommand]
    private void OpenAuthor() => OpenUrl(AuthorUrl);

    [RelayCommand]
    private void OpenIssues() => OpenUrl(RepositoryUrl + "/issues");

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
