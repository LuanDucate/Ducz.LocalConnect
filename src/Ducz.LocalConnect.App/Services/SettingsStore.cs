using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ducz.LocalConnect.App.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();

    public SettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ducz LocalConnect", "settings.json"))
    {
    }

    public SettingsStore(string path)
    {
        _path = path;
        Current = Load();
    }

    public AppSettings Current { get; }

    public string FilePath => _path;

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                var json = JsonSerializer.Serialize(Current, JsonOptions);
                File.WriteAllText(_path, json);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Losing a preference is not worth an error dialog.
            }
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new AppSettings();
    }
}
