using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Client-side repository of the network inventory entries currently known to the terminal.</summary>
public interface IItemRepository
{
    /// <summary>Applies a full repository snapshot or a batch of serial-addressed changes.</summary>
    void HandleUpdate(bool fullUpdate, IReadOnlyList<InventoryEntryUpdate> entries);

    /// <summary>Returns the currently meaningful inventory entries.</summary>
    IReadOnlyCollection<InventoryEntryUpdate> GetAllEntries();
}
