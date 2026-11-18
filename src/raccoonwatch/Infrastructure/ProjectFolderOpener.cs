using System.Diagnostics;
using Raccoonwatch.Models;

namespace Raccoonwatch.Infrastructure;

/// <summary>Resolves a process definition's project folder and opens it in Windows Explorer.</summary>
public static class ProjectFolderOpener
{
    /// <summary>
    /// Returns the configured working directory, or the command file's directory when no
    /// working directory is configured. Returns <see langword="null"/> when neither yields a path.
    /// </summary>
    public static string? ResolveFolder(ProcessDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var folder = string.IsNullOrWhiteSpace(definition.WorkingDirectory)
            ? Path.GetDirectoryName(definition.CommandPath)
            : definition.WorkingDirectory;

        return string.IsNullOrWhiteSpace(folder) ? null : folder;
    }

    /// <summary>Builds the Explorer start info for <paramref name="folder"/>.</summary>
    public static ProcessStartInfo BuildStartInfo(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        return new ProcessStartInfo("explorer.exe")
        {
            Arguments = ProcessLauncher.Quote(folder),
            UseShellExecute = true
        };
    }

    /// <summary>Opens <paramref name="folder"/> in Windows Explorer.</summary>
    public static void Open(string folder)
    {
        using var process = Process.Start(BuildStartInfo(folder));
    }
}
