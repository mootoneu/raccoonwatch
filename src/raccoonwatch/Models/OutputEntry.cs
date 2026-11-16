namespace Raccoonwatch.Models;

/// <summary>A single captured stdout/stderr line (or synthetic separator).</summary>
public sealed record OutputEntry(
    DateTimeOffset Timestamp,
    Guid DefinitionId,
    bool IsStandardError,
    string Text);
