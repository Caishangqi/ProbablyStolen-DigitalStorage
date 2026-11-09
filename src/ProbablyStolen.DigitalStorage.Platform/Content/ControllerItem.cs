using ProbablyStolen.DigitalStorage.Platform.Client;
using ProbablyStolen.DigitalStorage.Platform.Localization;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed class ControllerItem
{
    internal const string ItemId = "digital_storage.storage_network_controller";
    internal const string SpriteKey = "controller";
    internal const int Width = 2;
    internal const int Height = 3;
    internal const string NameKey = "controller_name";
    internal const string DescriptionKey = "controller_desc";
    private readonly ModText _text;
    private readonly ItemAppearance _appearance;
    private readonly MachineContentWindow _windows;

    internal ControllerItem(ModText text, ItemAppearance appearance, MachineContentWindow windows)
    {
        _text = text;
        _appearance = appearance;
        _windows = windows;
    }

    internal GameItem Create()
    {
        _appearance.EnsureReady(SpriteKey);
        var item = ItemDirectory.CreateEmptyItem(ItemId);
        try
        {
            item.name = _text.Get(NameKey);
            item.shortDescription = _text.Get(DescriptionKey);
            _appearance.Apply(item, SpriteKey, Width, Height);
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
