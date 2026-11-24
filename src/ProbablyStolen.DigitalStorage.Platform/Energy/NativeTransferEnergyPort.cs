using System;
using System.Diagnostics.CodeAnalysis;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Platform.Energy;

internal sealed class NativeTransferEnergyPort : ITransferEnergyPort
{
    private readonly long _idlePower;
    private readonly Func<IFeeSource?> _findSource;

    // The coordinator binds the run after the native load or new-game hook initializes the station.
    internal NativeTransferEnergyPort(GameItem controller, long idlePower, PlayerStore store, StoreStation station)
    {
        if (controller is null) throw new ArgumentNullException(nameof(controller));
        if (idlePower < 0) throw new ArgumentOutOfRangeException(nameof(idlePower));
        _idlePower = idlePower;
        if (store is null) throw new ArgumentNullException(nameof(store));
        if (station is null) throw new ArgumentNullException(nameof(station));
        _findSource = () => FindSource(controller, store, station);
    }

    internal NativeTransferEnergyPort(long idlePower, Func<IFeeSource?> findSource)
    {
        if (idlePower < 0) throw new ArgumentOutOfRangeException(nameof(idlePower));
        _idlePower = idlePower;
        _findSource = findSource ?? throw new ArgumentNullException(nameof(findSource));
    }

    internal IoPortRuntimeStatus? GetFeeSourceBlock(int amount) => Quote(amount, out _);

    public bool TryReserve(int amount, [NotNullWhen(true)] out ITransferEnergyReservation? reservation)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        reservation = null;
        if (Quote(amount, out var quote) != null) return false;

        var pending = new NativeTransferEnergyReservation(() => Refund(quote));
        var drawn = quote.Source.Draw(amount);
        var expected = drawn ? quote.Before - quote.Debit : quote.Before;
        if (quote.Source.ReadBalance(quote.Cash) != expected)
            throw new InvalidOperationException("The native operation fee debit could not be verified.");
        if (!drawn) return false;
        reservation = pending;
        return true;
    }

    private IoPortRuntimeStatus? Quote(int amount, out FeeQuote quote)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        quote = default;
        if (_findSource() is not { } source) return IoPortRuntimeStatus.Unpowered;

        var converter = source.IsConverter;
        if (!converter && source.IsHypertech) return IoPortRuntimeStatus.UnsupportedEnergySource;
        if (_idlePower > int.MaxValue - amount) return IoPortRuntimeStatus.Unpowered;
        var demand = (int)Math.Max(1L, _idlePower + amount);

        if (converter)
        {
            if (source.ConverterIsNight() is not { } night) return IoPortRuntimeStatus.Unpowered;
            if (night)
                return source.CanSupplyAtNight() ? IoPortRuntimeStatus.OperationFeeUnavailable : IoPortRuntimeStatus.Unpowered;

            var rate = source.GetRate();
            var idleUnits = amount == 0 ? demand : (int)_idlePower;
            if (!TryQuoteCredits(source, idleUnits, rate, out var idleCredits) ||
                !TryQuoteCredits(source, amount, rate, out var feeCredits)) return IoPortRuntimeStatus.OperationFeeUnavailable;
            // The converter rounds the periodic load and each operation debit separately.
            if ((long)idleCredits + feeCredits > source.ReadCash() || !source.CanSupply(amount))
                return IoPortRuntimeStatus.Unpowered;
            quote = new FeeQuote(source, true, source.ReadCash(), feeCredits);
            return null;
        }

        if (source.ReadEnergy() is not { } energy || energy < demand) return IoPortRuntimeStatus.Unpowered;
        quote = new FeeQuote(source, false, energy, amount);
        return null;
    }

    private static bool TryQuoteCredits(IFeeSource source, int units, float rate, out int credits)
    {
        credits = 0;
        if (units == 0) return true;
        var price = units * rate;
        if (!(price > 0 && price < 2147483648f)) return false;
        credits = source.ToCredits(units, rate);
        return credits > 0;
    }

    private static bool Refund(FeeQuote quote)
    {
        if (quote.Source.ReadBalance(quote.Cash) is not { } current || current > int.MaxValue - quote.Debit) return false;
        var restored = current + quote.Debit;
        var verifySourceState = quote.Source.WriteBalance(quote.Cash, restored);
        return quote.Source.ReadBalance(quote.Cash) == restored && (verifySourceState?.Invoke() ?? true);
    }

    private static IFeeSource? FindSource(GameItem controller, PlayerStore store, StoreStation station)
    {
        if (!IsCurrentRun(store)) return null;
        var source = MachineHelper.GetBatterySlot(controller)?.childItem;
        return IsLivePowerSource(source) ? new NativeFeeSource(source, store, station) : null;
    }

    private static bool IsCurrentRun(PlayerStore store) => !SaveManager.IsDecoding && EmporiumEntry.Instance != null &&
        PlayerStore.HasInstance && ReferenceEquals(PlayerStore.Instance, store);

    private static bool IsLivePowerSource([NotNullWhen(true)] GameItem? source) =>
        source != null && source.parentInventory != null &&
        (source is not GameItemElement element || !element.IsDestroyed()) && source.IsTag(PowerHelper.POWER_SOURCE_ITEM_TAG);

    private readonly struct FeeQuote
    {
        internal FeeQuote(IFeeSource source, bool cash, int before, int debit) =>
            (Source, Cash, Before, Debit) = (source, cash, before, debit);

        internal IFeeSource Source { get; }
        internal bool Cash { get; }
        internal int Before { get; }
        internal int Debit { get; }
    }

    internal interface IFeeSource
    {
        bool IsConverter { get; }
        bool IsHypertech { get; }
        bool? ConverterIsNight();
        bool CanSupplyAtNight();
        float GetRate();
        int ToCredits(int units, float rate);
        bool CanSupply(int amount);
        int ReadCash();
        int? ReadEnergy();
        bool Draw(int amount);
        int? ReadBalance(bool cash);
        // The optional check runs only after the balance is verified.
        Func<bool>? WriteBalance(bool cash, int restored);
    }

    private sealed class NativeFeeSource : IFeeSource
    {
        private const string EnergyTag = PowerHelper.POWER_SOURCE_ITEM_ENERGY;
        private readonly GameItem _source;
        private readonly PlayerStore _store;
        private readonly StoreStation _station;

        internal NativeFeeSource(GameItem source, PlayerStore store, StoreStation station) =>
            (_source, _store, _station) = (source, store, station);

        public bool IsConverter => CurrentConverterHelper.IsCurrentConverter(_source);
        public bool IsHypertech => HypertechBatteryHelper.IsHypertechBattery(_source);

        public bool? ConverterIsNight()
        {
            // Native GetTagReadonly creates missing entries. Only quote a converter with its initialized tags.
            var tags = _source.modifiedState?.dict;
            if (_station.storeEventManager == null || tags == null ||
                !tags.TryGetValue(CurrentConverterHelper.CURRENT_CONVERTER_ON_BOOL, out var on) || on == null ||
                !tags.TryGetValue(CurrentConverterHelper.CURRENT_CONVERTER_RATE_FLOAT, out var rateTag) || rateTag == null)
                return null;
            return CurrentConverterHelper.isNightCycle;
        }

        public bool CanSupplyAtNight() => _store.storeService != null && CurrentConverterHelper.CanSupply(_source, 0);
        public float GetRate() => CurrentConverterHelper.GetRate(_source);
        public int ToCredits(int units, float rate) => CurrentConverterHelper.ToCredits(units, rate);
        public bool CanSupply(int amount) => CurrentConverterHelper.CanSupply(_source, amount);
        public int ReadCash() => _store.playerCash;
        public bool Draw(int amount) => PowerHelper.DrawPowerSource(_source, amount);

        public int? ReadEnergy()
        {
            var tags = _source.state?.dict;
            var effectiveTags = _source.modifiedState?.dict;
            if (tags == null || effectiveTags == null || !tags.TryGetValue(EnergyTag, out var stored) || stored == null ||
                !effectiveTags.TryGetValue(EnergyTag, out var effective) || effective == null) return null;
            var balance = stored.GetInt();
            return balance >= 0 && effective.GetInt() == balance ? balance : null;
        }

        public int? ReadBalance(bool cash)
        {
            if (!IsCurrentRun(_store)) return null;
            if (cash) return _store.playerCash;
            return IsLivePowerSource(_source) ? ReadEnergy() : null;
        }

        public Func<bool>? WriteBalance(bool cash, int restored)
        {
            if (cash)
            {
                _store.playerCash = restored;
                return null;
            }

            var tags = _source.state.dict;
            var enabled = tags[EnergyTag].IsEnabled();
            _source.ModifyTag(EnergyTag, tag => tag.SetInt(restored));
            PowerHelper.UpdateEnergyCreditSprite(_source);
            return () => _source.state.dict[EnergyTag].IsEnabled() == enabled;
        }
    }
}
