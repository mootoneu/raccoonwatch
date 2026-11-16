using System.Diagnostics;
using Raccoonwatch.Models;

namespace Raccoonwatch.Services;

public sealed class OutputBatchEventArgs(IReadOnlyList<OutputEntry> entries) : EventArgs
{
    public IReadOnlyList<OutputEntry> Entries { get; } = entries;
}

public interface IOutputStore : IDisposable
{
    event EventHandler<OutputBatchEventArgs>? OutputReceived;

    int HistoryLimit { get; set; }

    void Attach(Guid definitionId, Process process);

    IReadOnlyList<OutputEntry> GetSnapshot(Guid definitionId);

    void Append(Guid definitionId, OutputEntry entry);

    void AddSeparator(Guid definitionId, string text);

    void Clear(Guid definitionId);
}
