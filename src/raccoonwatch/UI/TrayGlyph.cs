using Raccoonwatch.Models;

namespace Raccoonwatch.UI;

/// <summary>Text glyphs used to distinguish statuses without relying on color alone.</summary>
public static class TrayGlyph
{
    public static string For(ProcessStatus status) => status switch
    {
        ProcessStatus.Running => "●",
        ProcessStatus.Stopped => "○",
        ProcessStatus.Starting or ProcessStatus.Stopping => "▲",
        ProcessStatus.Exited or ProcessStatus.Failed => "✖",
        _ => "?"
    };
}
