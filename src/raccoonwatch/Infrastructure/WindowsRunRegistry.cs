using Microsoft.Win32;
using Raccoonwatch.Services;

namespace Raccoonwatch.Infrastructure;

/// <summary>HKCU Run-key implementation of <see cref="IAutostartService"/>.</summary>
public sealed class WindowsRunRegistry : IAutostartService
{
    public const string ValueName = "raccoonwatch";
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled() => GetConfiguredPath() is not null;

    public string? GetConfiguredPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        var value = key?.GetValue(ValueName) as string;
        return string.IsNullOrWhiteSpace(value) ? null : Unquote(value);
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        if (!enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        using var writableKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        writableKey.SetValue(ValueName, FormatValue(executablePath), RegistryValueKind.String);
    }

    public bool IsPathCurrent(string executablePath)
    {
        var configured = GetConfiguredPath();
        return configured is not null &&
               string.Equals(configured, executablePath, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatValue(string executablePath) => $"\"{executablePath}\"";

    private static string Unquote(string value) => value.Trim().Trim('"');
}
