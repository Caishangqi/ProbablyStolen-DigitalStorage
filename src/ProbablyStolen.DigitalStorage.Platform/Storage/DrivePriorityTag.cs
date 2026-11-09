using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Reads and writes a drive's storage priority. The tag is part of the native <c>itemState</c>, so the save graph
/// keeps it with the drive. A drive without the tag, including every drive from an older save, has priority 0.
/// </summary>
internal static class DrivePriorityTag
{
    internal const string Tag = "digital_storage.priority";

    /// <summary>Gets a drive's priority, clamped to the allowed range; reading never rewrites the tag.</summary>
    internal static int Read(GameItem? drive) =>
        drive != null && drive.IsTag(Tag) ? DrivePriority.Clamp(drive.GetTagReadonly(Tag).GetInt()) : DrivePriority.Default;

    /// <summary>Gets the priority of the drive a mounted cell sits in; a loose cell has priority 0.</summary>
    internal static int ReadForCell(GameItem cell) => Read(cell.parentInventory?.GetParentItem());

    /// <summary>Writes a clamped priority; the default removes the tag.</summary>
    internal static void Write(GameItem drive, int priority)
    {
        var value = DrivePriority.Clamp(priority);
        if (value == DrivePriority.Default)
        {
            drive.DisableTag(Tag);
            return;
        }

        drive.ModifyTag(Tag, state => state.Enable().SetInt(value));
    }
}
