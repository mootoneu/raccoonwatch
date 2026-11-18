using System.Diagnostics;
using Raccoonwatch.Models;

namespace Raccoonwatch.Infrastructure;

/// <summary>Builds and launches the one-off update command configured for a process.</summary>
public static class UpdateCommandLauncher
{
    /// <summary>Infers how to run an updater from its file extension.</summary>
    public static ProcessKind InferKind(string commandPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandPath);

        var extension = Path.GetExtension(commandPath);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessKind.Cmd;
        }

        return extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
            ? ProcessKind.PowerShell
            : ProcessKind.Executable;
    }

    /// <summary>Builds a visible, one-off start info for the given updater.</summary>
    public static ProcessStartInfo BuildStartInfo(string commandPath, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandPath);

        var startInfo = ProcessLauncher.BuildStartInfo(new ProcessDefinition
        {
            CommandPath = commandPath,
            Arguments = arguments,
            Kind = InferKind(commandPath)
        });

        // Update commands are ordinary, visible processes rather than tracked output
        // streams, so they must not inherit the redirect/no-window settings.
        startInfo.RedirectStandardOutput = false;
        startInfo.RedirectStandardError = false;
        startInfo.StandardOutputEncoding = null;
        startInfo.StandardErrorEncoding = null;
        startInfo.CreateNoWindow = false;
        startInfo.UseShellExecute = true;
        return startInfo;
    }

    /// <summary>Starts the updater and returns the launched process.</summary>
    public static Process Launch(string commandPath, string arguments) =>
        Process.Start(BuildStartInfo(commandPath, arguments))
        ?? throw new InvalidOperationException("The update command could not be started.");
}
