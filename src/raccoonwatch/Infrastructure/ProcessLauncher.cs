using System.Diagnostics;
using System.Text;
using Raccoonwatch.Models;

namespace Raccoonwatch.Infrastructure;

public interface IProcessLauncher
{
    Process Start(ProcessDefinition definition);
}

/// <summary>Builds <see cref="ProcessStartInfo"/> objects and launches tracked processes.</summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    public Process Start(ProcessDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var process = new Process
        {
            StartInfo = BuildStartInfo(definition),
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException($"Failed to start '{definition.DisplayName}'.");
            }

            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public static ProcessStartInfo BuildStartInfo(ProcessDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var workingDirectory = string.IsNullOrWhiteSpace(definition.WorkingDirectory)
            ? Path.GetDirectoryName(definition.CommandPath)
            : definition.WorkingDirectory;

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = string.IsNullOrEmpty(workingDirectory)
                ? Environment.CurrentDirectory
                : workingDirectory
        };

        switch (definition.Kind)
        {
            case ProcessKind.Executable:
                startInfo.FileName = definition.CommandPath;
                startInfo.Arguments = definition.Arguments;
                break;

            case ProcessKind.Cmd:
                startInfo.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
                startInfo.Arguments = $"/c \"{BuildScriptCommand(definition)}\"";
                break;

            case ProcessKind.PowerShell:
                startInfo.FileName = ResolvePowerShellExecutable();
                startInfo.Arguments =
                    $"-NoProfile -ExecutionPolicy Bypass -File {Quote(definition.CommandPath)} {definition.Arguments}".TrimEnd();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(definition), definition.Kind, "Unknown process kind.");
        }

        return startInfo;
    }

    /// <summary>Quotes a path or argument for use on a Windows command line.</summary>
    public static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (value.StartsWith('"') && value.EndsWith('"'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static string BuildScriptCommand(ProcessDefinition definition) =>
        $"{Quote(definition.CommandPath)} {definition.Arguments}".TrimEnd();

    private static string ResolvePowerShellExecutable()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsPowerShell = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(windowsPowerShell) ? windowsPowerShell : "powershell.exe";
    }
}
