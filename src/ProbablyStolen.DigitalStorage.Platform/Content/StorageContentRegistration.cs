using System;
using System.Collections.Generic;
using System.Threading;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed class StorageContentRegistration : IDisposable
{
    private readonly ModLog _log;
    private readonly ItemContentLoadCallback _loadCallback;
    private readonly KeyValuePair<string, Func<GameItem>>[] _factories;
    private readonly ItemContentSynchronization _contentSynchronization;
    private ModItemDirectory? _directory;
    private bool _enabled;
    private bool _disposed;
    private int _threadId;

    internal StorageContentRegistration(TerminalItem terminal, DriveItem drive, StorageCellItem cell,
        ControllerItem controller, StorageComponentFactory component, MaterialItem housing,
        ItemContentSynchronization contentSynchronization, ModLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _contentSynchronization = contentSynchronization ?? throw new ArgumentNullException(nameof(contentSynchronization));
        _loadCallback = new ItemContentLoadCallback(_contentSynchronization, message => _log.Error(message));
        var factories = new List<KeyValuePair<string, Func<GameItem>>>
        {
            new(TerminalItem.ItemId, () => Create(terminal.Create)),
            new(DriveItem.ItemId, () => Create(drive.Create)),
            new(ControllerItem.ItemId, () => Create(controller.Create)),
            new(MaterialItem.ItemId, () => Create(housing.Create))
        };
        foreach (var tier in StorageCellTier.All)
        {
            Func<GameItem> createComponent = () => Create(() => component.Create(tier));
            factories.Add(new(tier.ComponentId, createComponent));
            factories.Add(new(tier.CellId, () => Create(() => cell.Create(tier, createComponent))));
        }
        _factories = factories.ToArray();
    }

    private GameItem Create(Func<GameItem> factory)
    {
        var item = factory();
        item.onLoaded += loaded =>
        {
            if (!_enabled || _disposed) return;
            _loadCallback.Invoke(new NativeItemContentState(loaded));
        };
        return item;
    }

    internal void Enable()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(StorageContentRegistration));
        }

        if (_enabled)
        {
            return;
        }

        _threadId = Thread.CurrentThread.ManagedThreadId;
        _enabled = true;
        ModHook.OnModItemDirectoryInit += Register;
    }

    internal void Register(ModItemDirectory directory)
    {
        if (!_enabled)
        {
            return;
        }

        if (Thread.CurrentThread.ManagedThreadId != _threadId)
        {
            _log.Error("Digital Storage: item registration rejected outside the enabling thread.");
            return;
        }

        if (directory == null || directory.factoryDictionary == null)
        {
            _log.Error("Digital Storage: item directory is unavailable.");
            return;
        }

        // Native Add can remove another directory's entry. Preflight the entire group first.
        foreach (var binding in _factories)
        {
            var ownedHere = ReferenceEquals(_directory, directory) &&
                directory.factoryDictionary.TryGetValue(binding.Key, out var local) && ReferenceEquals(local, binding.Value);
            if ((DirectoryMaster.Has<GameItem>(binding.Key) &&
                 !(ownedHere && ReferenceEquals(DirectoryMaster.GetFactory<GameItem>(binding.Key), binding.Value))) ||
                (directory.Has(binding.Key) && !ownedHere))
            {
                _log.Error($"Digital Storage: item identifier collision: {binding.Key}.");
                return;
            }
        }

        var added = new List<KeyValuePair<string, Func<GameItem>>>();
        try
        {
            foreach (var binding in _factories)
            {
                if (directory.factoryDictionary.TryGetValue(binding.Key, out var existing) && ReferenceEquals(existing, binding.Value))
                {
                    continue;
                }

                // Track the attempted entry as well: a virtual Add may insert and then throw.
                added.Add(binding);
                if (!directory.Add(binding.Key, binding.Value) ||
                    !directory.factoryDictionary.TryGetValue(binding.Key, out var actual) || !ReferenceEquals(actual, binding.Value))
                {
                    throw new InvalidOperationException($"Item registration failed: {binding.Key}.");
                }
            }

            _directory = directory;
            _log.Log("Digital Storage: fourteen content factories registered.");
        }
        catch (Exception error)
        {
            for (var index = added.Count - 1; index >= 0; index--)
            {
                var binding = added[index];
                if (directory.factoryDictionary.TryGetValue(binding.Key, out var actual) && ReferenceEquals(actual, binding.Value))
                {
                    directory.Remove(binding.Key);
                }
            }

            _log.Error($"Digital Storage: content group registration failed ({error.GetType().Name}).");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _enabled = false;
        _disposed = true;
        _loadCallback.Disable();
        ModHook.OnModItemDirectoryInit -= Register;
        // Native types may already be referenced by saved or placed items; keep them registered.
    }
}
