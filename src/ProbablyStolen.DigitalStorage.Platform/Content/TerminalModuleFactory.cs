using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

namespace ProbablyStolen.DigitalStorage.Platform.Content;

/// <summary>
/// Creates terminal modules: 2x2 modules of a type only terminal module bays accept, so they never fit a cell or a
/// vanilla machine.
/// </summary>
internal sealed class TerminalModuleFactory
{
    internal const string StackDisplaySprite = "module_type_stack_display";
    internal const int Width = 2;
    internal const int Height = 2;
    private readonly ItemAppearance _appearance;

    internal TerminalModuleFactory(ItemAppearance appearance) =>
        _appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));

    /// <summary>Creates a stack display module.</summary>
    internal GameItem CreateStackDisplay()
    {
        _appearance.EnsureReady(StackDisplaySprite);
        var item = ItemDirectory.CreateEmptyItem(StackingModule.ModuleId);
        try
        {
            _appearance.Apply(item, StackDisplaySprite, Width, Height);
            ModuleHelper.InitModuleItem(item, StackingModule.TerminalModuleType, isShipModule: false);
            item.mayStackItemFunc = (_, _) => false;
            item.maxSlotAmountItemFunc = (_, _, _) => 1;
            return item;
        }
        catch
        {
            item.Destroy();
            throw;
        }
    }
}
