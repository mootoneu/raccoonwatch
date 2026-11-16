using System.Drawing;
using System.Text;

namespace Raccoonwatch.UI;

public sealed record AnsiSegment(string Text, Color? Foreground);

/// <summary>
/// Minimal ANSI SGR parser: interprets foreground colors and reset codes and strips
/// other escape sequences so raw control characters never reach the output view.
/// </summary>
public static class AnsiTextParser
{
    private static readonly Color[] StandardColors =
    [
        Color.FromArgb(0, 0, 0),
        Color.FromArgb(205, 49, 49),
        Color.FromArgb(13, 188, 121),
        Color.FromArgb(229, 229, 16),
        Color.FromArgb(13, 110, 253),
        Color.FromArgb(188, 63, 188),
        Color.FromArgb(17, 168, 205),
        Color.FromArgb(229, 229, 229)
    ];

    private static readonly Color[] BrightColors =
    [
        Color.FromArgb(124, 124, 124),
        Color.FromArgb(241, 76, 76),
        Color.FromArgb(80, 250, 123),
        Color.FromArgb(255, 255, 0),
        Color.FromArgb(100, 150, 255),
        Color.FromArgb(255, 117, 255),
        Color.FromArgb(100, 235, 255),
        Color.FromArgb(255, 255, 255)
    ];

    public static IReadOnlyList<AnsiSegment> Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var segments = new List<AnsiSegment>();
        var buffer = new StringBuilder();
        Color? foreground = null;
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == '\x1b' && index + 1 < text.Length && text[index + 1] == '[')
            {
                Flush();

                var end = index + 2;
                while (end < text.Length && !char.IsLetter(text[end]))
                {
                    end++;
                }

                if (end < text.Length && text[end] == 'm')
                {
                    var codes = text[(index + 2)..end]
                        .Split(';', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var code in codes)
                    {
                        if (!int.TryParse(code, out var value))
                        {
                            continue;
                        }

                        switch (value)
                        {
                            case 0:
                            case 39:
                                foreground = null;
                                break;
                            case >= 30 and <= 37:
                                foreground = StandardColors[value - 30];
                                break;
                            case >= 90 and <= 97:
                                foreground = BrightColors[value - 90];
                                break;
                        }
                    }

                    index = end + 1;
                    continue;
                }

                // Non-SGR CSI sequence (cursor movement, erase, etc.): strip it.
                index = end < text.Length ? end + 1 : text.Length;
                continue;
            }

            buffer.Append(text[index]);
            index++;
        }

        Flush();
        return segments;

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            segments.Add(new AnsiSegment(buffer.ToString(), foreground));
            buffer.Clear();
        }
    }
}
