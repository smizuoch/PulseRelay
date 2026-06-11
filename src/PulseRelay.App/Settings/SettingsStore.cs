using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRelay.App.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON. A missing or unreadable file is never
/// fatal: <see cref="Load"/> falls back to defaults so the app always starts.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger _logger;

    public SettingsStore(string? directory = null, ILogger<SettingsStore>? logger = null)
    {
        _logger = logger ?? NullLogger<SettingsStore>.Instance;
        string dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PulseRelay");
        FilePath = Path.Combine(dir, "settings.json");
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                _logger.LogInformation("No settings file at {Path}, using defaults", FilePath);
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
            if (settings is null)
            {
                _logger.LogWarning("Settings file {Path} deserialized to null, using defaults", FilePath);
                return new AppSettings();
            }

            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to load settings from {Path}, using defaults", FilePath);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);

        // Write-then-move so a crash mid-write cannot truncate the existing file.
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
        _logger.LogDebug("Settings saved to {Path}", FilePath);
    }
}
