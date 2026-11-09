using ProbablyStolen.DigitalStorage.Common.Domain.Network.Energy;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Platform.Energy;

internal sealed class NativeEnergySource : IEnergySourcePort
{
    private readonly GameItem _controller;

    internal NativeEnergySource(GameItem controller)
    {
        _controller = controller;
    }

    public bool HasSource => TryGetSource(out _);

    public bool CanDraw(int amount) =>
        TryGetSource(out var source) && PowerHelper.CanDrawPowerSource(source, amount);

    public bool TryDraw(int amount)
    {
        if (amount < 0 || !TryGetSource(out var source))
        {
            return false;
        }

        var ordinary = !CurrentConverterHelper.IsCurrentConverter(source) &&
            !HypertechBatteryHelper.IsHypertechBattery(source);
        var before = ordinary ? source.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_ENERGY).GetInt() : 0;
        try
        {
            return PowerHelper.DrawPowerSource(source, amount);
        }
        catch
        {
            if (ordinary)
            {
                source.ModifyTag(PowerHelper.POWER_SOURCE_ITEM_ENERGY,
                    tag => tag.SetInt(before), skipValidate: true);
            }

            throw;
        }
    }

    private bool TryGetSource(out GameItem source)
    {
        var battery = MachineHelper.GetBatterySlot(_controller)?.childItem;
        if (battery != null && battery.IsTag(PowerHelper.POWER_SOURCE_ITEM_TAG))
        {
            source = battery;
            return true;
        }

        source = null!;
        return false;
    }
}
