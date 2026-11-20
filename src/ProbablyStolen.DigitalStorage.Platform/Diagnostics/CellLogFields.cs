using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Diagnostics;
using ProbablyStolen.DigitalStorage.Platform.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Diagnostics;

internal static class CellLogFields
{
    internal static IReadOnlyList<LogField> For(GameItem cell, params LogField[] details)
    {
        var health = CellHealth.Get(cell);
        var fields = new List<LogField>(details.Length + 5)
        {
            LogField.Of("cell_item_id", cell.identifier),
            LogField.Of("cell_uid", cell.uniqueId),
            LogField.Of("schema", CellTags.ReadSchema(cell)),
            LogField.Of("health", health.Status.ToString()),
            LogField.Of("pages", CellPages.Tabs(cell)?.count ?? 0)
        };
        fields.AddRange(details);
        return fields;
    }
}
