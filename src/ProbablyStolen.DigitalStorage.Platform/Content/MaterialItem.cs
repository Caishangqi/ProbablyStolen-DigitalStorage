namespace ProbablyStolen.DigitalStorage.Platform.Content;

internal sealed class MaterialItem
{
    internal const string ItemId = "digital_storage.item_cell_housing";
    internal const string SpriteKey = "item_cell_housing";
    internal const int Width = 1;
    internal const int Height = 2;
    internal const string NameKey = "item_cell_housing_name";
    internal const string DescriptionKey = "item_cell_housing_desc";
    private readonly StorageCellItem _cell;

    internal MaterialItem(StorageCellItem cell)
    {
        _cell = cell;
    }

    internal GameItem Create() => _cell.CreateHost(false);
}
