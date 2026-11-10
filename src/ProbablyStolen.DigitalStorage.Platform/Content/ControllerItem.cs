using ProbablyStolen.DigitalStorage.Platform.Client;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed class ControllerItem
{
    internal const string ItemId = "digital_storage.storage_network_controller";
    internal const string SpriteKey = "controller";
    internal const int Width = 2;
    internal const int Height = 3;
    internal const string NameKey = "controller_name";
    internal const string DescriptionKey = "controller_desc";
    private readonly ItemAppearance _appearance;
    private readonly MachineContentWindow _windows;
    private readonly ItemContentSynchronization _contentSynchronization;

    internal ControllerItem(ItemAppearance appearance, MachineContentWindow windows,
        ItemContentSynchronization contentSynchronization)
    {
        _contentSynchronization = contentSynchronization;
        _appearance = appearance;
        _windows = windows;
    }

    internal GameItem Create()
    {
        _appearance.EnsureReady(SpriteKey);
        var item = ItemDirectory.CreateEmptyItem(ItemId);
        try
        {
            _appearance.Apply(item, SpriteKey, Width, Height);
            NativeItemContentInitialization.Initialize(item, _contentSynchronization);
            _windows.Attach(item, acceptsBattery: true, consumesNetworkPower: true);
            return item;
        }
        catch
        {
            item.Destroy();
            throw;
        }
    }
}
