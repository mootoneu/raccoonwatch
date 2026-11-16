using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Raccoonwatch.Models;

namespace Raccoonwatch.Infrastructure;

/// <summary>Loads and atomically saves the JSON settings document.</summary>
public sealed class JsonSettingsStore(ILogger<JsonSettingsStore>? logger = null, string? filePath = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        WriteIndented = true
    };

    private readonly ILogger<JsonSettingsStore> _logger = logger ?? NullLogger<JsonSettingsStore>.Instance;
    private readonly string _filePath = filePath ?? AppPaths.SettingsFilePath;
    private readonly string _temporaryPath = (filePath ?? AppPaths.SettingsFilePath) + ".tmp";

    public string FilePath => _filePath;

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions)
                           ?? new AppSettings();

            if (settings.SchemaVersion != 1)
            {
                throw new InvalidDataException($"Unsupported schema version {settings.SchemaVersion}.");
            }

            settings.Processes ??= [];
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings file at {Path} is corrupt; recreating defaults.", _filePath);
            QuarantineCorruptFile();
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        settings.SchemaVersion = 1;
        var json = JsonSerializer.Serialize(settings, SerializerOptions);

        try
        {
            File.WriteAllText(_temporaryPath, json);
            File.Move(_temporaryPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}.", _filePath);
            throw;
        }
    }

    private void QuarantineCorruptFile()
    {
        try
        {
            File.Move(_filePath, _filePath + ".bad", overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not quarantine corrupt settings file {Path}.", _filePath);
        }
    }
}
