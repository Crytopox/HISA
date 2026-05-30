using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface ILocalCharacterLocationFeed
{
    event EventHandler<LocalCharacterSystemChange>? SystemChanged;
    IReadOnlyDictionary<int, LocalCharacterSystemChange> Snapshot { get; }
}
