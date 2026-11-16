using Microsoft.Extensions.Logging;
using Raccoonwatch.Infrastructure;
using Raccoonwatch.Models;

namespace Raccoonwatch.Services;

/// <summary>Application settings, validation and persistence orchestration.</summary>
public sealed class SettingsService(JsonSettingsStore store, ILogger<SettingsService> logger) : ISettingsService
{
    private readonly object _gate = new();
    private AppSettings _settings = new();

    public AppSettings Settings => _settings;

    public event EventHandler? Changed;

    public void Load()
    {
        var loaded = store.Load();
        Normalize(loaded);

        lock (_gate)
        {
            _settings = loaded;
        }

        logger.LogInformation(
            "Loaded {Count} process definitions from {Path}.",
            loaded.Processes.Count,
            store.FilePath);
    }

    public void Save()
    {
        lock (_gate)
        {
            store.Save(_settings);
        }
    }

    public IReadOnlyList<string> Validate(ProcessDefinition definition, Guid? ignoreId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.DisplayName))
        {
            errors.Add("Display name is required.");
        }
        else if (definition.DisplayName.Length > 80)
        {
            errors.Add("Display name must be 80 characters or fewer.");
        }
        else if (_settings.Processes.Any(p =>
                     p.Id != ignoreId &&
                     string.Equals(p.DisplayName, definition.DisplayName, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Another process already uses this display name.");
        }

        if (string.IsNullOrWhiteSpace(definition.CommandPath))
        {
            errors.Add("Command path is required.");
        }
        else
        {
            if (!File.Exists(definition.CommandPath))
            {
                errors.Add("Command file does not exist.");
            }

            var extension = Path.GetExtension(definition.CommandPath);
            var validExtension = definition.Kind switch
            {
                ProcessKind.Executable => extension.Equals(".exe", StringComparison.OrdinalIgnoreCase),
                ProcessKind.Cmd => extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                                   extension.Equals(".bat", StringComparison.OrdinalIgnoreCase),
                ProcessKind.PowerShell => extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase),
                _ => false
            };

            if (!validExtension)
            {
                errors.Add($"Command path must have the correct extension for {definition.Kind}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(definition.WorkingDirectory) &&
            !Directory.Exists(definition.WorkingDirectory))
        {
            errors.Add("Working directory does not exist.");
        }

        return errors;
    }

    public void AddOrUpdate(ProcessDefinition definition)
    {
        lock (_gate)
        {
            var index = _settings.Processes.FindIndex(p => p.Id == definition.Id);
            if (index >= 0)
            {
                _settings.Processes[index] = definition;
            }
            else
            {
                _settings.Processes.Add(definition);
            }

            store.Save(_settings);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(Guid id)
    {
        lock (_gate)
        {
            _settings.Processes.RemoveAll(p => p.Id == id);
            store.Save(_settings);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Normalize(AppSettings settings)
    {
        settings.SchemaVersion = 1;
        settings.Processes ??= [];
        settings.PollIntervalMs = Math.Clamp(settings.PollIntervalMs, 250, 60_000);
        settings.OutputHistoryLines = Math.Clamp(settings.OutputHistoryLines, 100, 100_000);

        if (!Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(
                settings.LogLevel,
                ignoreCase: true,
                out var logLevel))
        {
            logLevel = Microsoft.Extensions.Logging.LogLevel.Information;
        }

        settings.LogLevel = logLevel.ToString();
    }
}
