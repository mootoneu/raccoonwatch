using Raccoonwatch.Models;

namespace Raccoonwatch.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }

    event EventHandler? Changed;

    void Load();

    void Save();

    IReadOnlyList<string> Validate(ProcessDefinition definition, Guid? ignoreId = null);

    void AddOrUpdate(ProcessDefinition definition);

    void Delete(Guid id);
}
