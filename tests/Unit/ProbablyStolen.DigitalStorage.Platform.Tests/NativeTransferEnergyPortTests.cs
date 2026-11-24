using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Ports;
using ProbablyStolen.DigitalStorage.Platform.Energy;

namespace ProbablyStolen.DigitalStorage.Platform.Tests;

[TestFixture]
public sealed class NativeTransferEnergyPortTests
{
    [Test]
    public void GetFeeSourceBlock_OrdinaryEnergyCoversIdleAndFeeWithoutDebiting()
    {
        var source = new MemoryFeeSource(balance: 6);
        var port = new NativeTransferEnergyPort(5, () => source);

        Assert.That(port.GetFeeSourceBlock(0), Is.Null);
        Assert.That(port.GetFeeSourceBlock(1), Is.Null);
        Assert.That(port.GetFeeSourceBlock(2), Is.EqualTo(IoPortRuntimeStatus.Unpowered));
        Assert.That(source.Balance, Is.EqualTo(6));
        Assert.That(source.DrawCalls, Is.Zero);
        Assert.That(source.WriteCalls, Is.Zero);
    }

    [Test]
    public void GetFeeSourceBlock_DaytimeCashRoundsIdleAndFeeSeparately()
    {
        var source = new MemoryFeeSource(balance: 1) { IsConverter = true, Rate = 0.4f };
        var port = new NativeTransferEnergyPort(1, () => source);

        Assert.That(port.GetFeeSourceBlock(1), Is.EqualTo(IoPortRuntimeStatus.Unpowered));
        Assert.That(source.CreditQuotes, Is.EqualTo(new[] { 1, 1 }));
        Assert.That(source.SupplyChecks, Is.Empty);
        Assert.That(source.Balance, Is.EqualTo(1));
        Assert.That(source.DrawCalls, Is.Zero);
        Assert.That(source.WriteCalls, Is.Zero);
    }

    [Test]
    public void TryReserve_HypertechRejectsEvenWithBalance()
    {
        var source = new MemoryFeeSource(balance: 50) { IsHypertech = true };
        var port = new NativeTransferEnergyPort(5, () => source);

        Assert.That(port.GetFeeSourceBlock(0), Is.EqualTo(IoPortRuntimeStatus.UnsupportedEnergySource));
        Assert.That(port.GetFeeSourceBlock(1), Is.EqualTo(IoPortRuntimeStatus.UnsupportedEnergySource));
        Assert.That(port.TryReserve(1, out var reservation), Is.False);
        Assert.That(reservation, Is.Null);
        Assert.That(source.DrawCalls, Is.Zero);
        Assert.That(source.Balance, Is.EqualTo(50));
    }

    [Test]
    public void TryReserve_NightConverterWithActiveServiceRejectsOperationFees()
    {
        var source = new MemoryFeeSource(balance: 50) { IsConverter = true, IsNight = true, NightServiceActive = true };
        var port = new NativeTransferEnergyPort(5, () => source);

        Assert.That(port.GetFeeSourceBlock(0), Is.EqualTo(IoPortRuntimeStatus.OperationFeeUnavailable));
        Assert.That(port.GetFeeSourceBlock(1), Is.EqualTo(IoPortRuntimeStatus.OperationFeeUnavailable));
        Assert.That(port.TryReserve(1, out var reservation), Is.False);
        Assert.That(reservation, Is.Null);
        Assert.That(source.NightServiceChecks, Is.EqualTo(3));
        Assert.That(source.CreditQuotes, Is.Empty);
        Assert.That(source.DrawCalls, Is.Zero);
        Assert.That(source.Balance, Is.EqualTo(50));
    }

    [Test]
    public void TryReserve_OrdinaryEnergyAtNightPaysFeeAndKeepsIdlePower()
    {
        var source = new MemoryFeeSource(balance: 6) { IsNight = true };
        var port = new NativeTransferEnergyPort(5, () => source);

        Assert.That(port.TryReserve(1, out var reservation), Is.True);
        Assert.That(source.Balance, Is.EqualTo(5));
        Assert.That(source.DrawCalls, Is.EqualTo(1));
        Assert.That(source.ConverterTimeChecks, Is.Zero);
        reservation!.Commit();
    }

    [Test]
    public void Cancel_DaytimeCashRestoresOnlyTheQuotedDebitIntoTheCurrentBalance()
    {
        var source = new MemoryFeeSource(balance: 10)
        {
            IsConverter = true,
            Rate = 1.5f,
            DebitOverride = 2
        };
        var port = new NativeTransferEnergyPort(1, () => source);
        Assert.That(port.TryReserve(1, out var reservation), Is.True);
        Assert.That(source.Balance, Is.EqualTo(8));
        Assert.That(source.SupplyChecks, Is.EqualTo(new[] { 1 }));
        source.Balance -= 3;
        source.Rate = 4f;

        Assert.That(reservation!.Cancel(), Is.True);
        Assert.That(source.Balance, Is.EqualTo(7));
        Assert.That(source.WriteCalls, Is.EqualTo(1));
        Assert.That(source.CreditQuotes, Is.EqualTo(new[] { 1, 1 }));
    }

    [Test]
    public void Cancel_OrdinaryEnergyRestoresOnlyTheDebitAndCannotWriteTwice()
    {
        var source = new MemoryFeeSource(balance: 10);
        var reservation = ReserveOne(source);
        source.Balance += 4;

        Assert.That(reservation.Cancel(), Is.True);
        Assert.That(source.Balance, Is.EqualTo(14));
        Assert.That(reservation.Cancel(), Is.False);
        Assert.That(source.Balance, Is.EqualTo(14));
        Assert.That(source.WriteCalls, Is.EqualTo(1));
    }

    [Test]
    public void Commit_RepeatedConfirmationPreventsRefund()
    {
        var source = new MemoryFeeSource(balance: 10);
        var reservation = ReserveOne(source);

        reservation.Commit();
        reservation.Commit();
        Assert.That(reservation.Cancel(), Is.False);
        Assert.That(source.Balance, Is.EqualTo(9));
        Assert.That(source.WriteCalls, Is.Zero);
    }

    [Test]
    public void TryReserve_RefusedDrawWithUnchangedBalanceReturnsFalse()
    {
        var source = new MemoryFeeSource(balance: 10) { DrawAccepted = false, DebitOverride = 0 };
        var port = new NativeTransferEnergyPort(5, () => source);

        Assert.That(port.TryReserve(1, out var reservation), Is.False);
        Assert.That(reservation, Is.Null);
        Assert.That(source.Balance, Is.EqualTo(10));
        Assert.That(source.DrawCalls, Is.EqualTo(1));
        Assert.That(source.WriteCalls, Is.Zero);
    }

    [Test]
    public void TryReserve_InconsistentDrawAndBalanceThrowsWithoutCompensating()
    {
        foreach (var (accepted, debit) in new[] { (false, 2), (true, 2), (true, 0) })
        {
            var source = new MemoryFeeSource(balance: 10) { DrawAccepted = accepted, DebitOverride = debit };
            var port = new NativeTransferEnergyPort(5, () => source);

            Assert.Throws<InvalidOperationException>(new Action(() => port.TryReserve(1, out _)));
            Assert.That(source.Balance, Is.EqualTo(10 - debit));
            Assert.That(source.DrawCalls, Is.EqualTo(1));
            Assert.That(source.WriteCalls, Is.Zero);
        }
    }

    [Test]
    public void Cancel_UnverifiedRefundReturnsFalseAndIsNeverRepeated()
    {
        var source = new MemoryFeeSource(balance: 10) { PostWriteBalanceReadable = false };
        var reservation = ReserveOne(source);

        Assert.That(reservation.Cancel(), Is.False);
        Assert.That(source.Balance, Is.EqualTo(10), "The refund may have reached the source before verification failed.");
        Assert.That(reservation.Cancel(), Is.False);
        Assert.That(source.DrawCalls, Is.EqualTo(1));
        Assert.That(source.WriteCalls, Is.EqualTo(1));
    }

    [Test]
    public void Cancel_RestoredBalanceWithUnverifiedSourceStateIsNeverRefundedTwice()
    {
        var source = new MemoryFeeSource(balance: 10) { VerifySourceState = () => false };
        var reservation = ReserveOne(source);

        Assert.That(reservation.Cancel(), Is.False);
        Assert.That(source.Balance, Is.EqualTo(10));
        Assert.That(reservation.Cancel(), Is.False);
        Assert.That(source.WriteCalls, Is.EqualTo(1));
    }

    private static ITransferEnergyReservation ReserveOne(MemoryFeeSource source)
    {
        var port = new NativeTransferEnergyPort(5, () => source);
        Assert.That(port.TryReserve(1, out var reservation), Is.True);
        return reservation!;
    }

    private sealed class MemoryFeeSource : NativeTransferEnergyPort.IFeeSource
    {
        internal MemoryFeeSource(int balance) => Balance = balance;

        internal int Balance { get; set; }
        internal bool IsNight { get; init; }
        internal bool NightServiceActive { get; init; }
        internal float Rate { get; set; } = 1;
        internal bool DrawAccepted { get; init; } = true;
        internal int? DebitOverride { get; init; }
        internal bool PostWriteBalanceReadable { get; init; } = true;
        internal Func<bool>? VerifySourceState { get; init; }
        internal int DrawCalls { get; private set; }
        internal int WriteCalls { get; private set; }
        internal int NightServiceChecks { get; private set; }
        internal int ConverterTimeChecks { get; private set; }
        internal List<int> CreditQuotes { get; } = new();
        internal List<int> SupplyChecks { get; } = new();
        public bool IsConverter { get; init; }
        public bool IsHypertech { get; init; }

        public bool? ConverterIsNight()
        {
            ConverterTimeChecks++;
            return IsNight;
        }

        public bool CanSupplyAtNight()
        {
            NightServiceChecks++;
            return NightServiceActive;
        }

        public float GetRate() => Rate;

        public int ToCredits(int units, float rate)
        {
            CreditQuotes.Add(units);
            return (int)MathF.Ceiling(units * rate);
        }

        public bool CanSupply(int amount)
        {
            SupplyChecks.Add(amount);
            return true;
        }

        public int ReadCash() => IsConverter ? Balance : throw new InvalidOperationException();
        public int? ReadEnergy() => !IsConverter ? Balance : throw new InvalidOperationException();

        public bool Draw(int amount)
        {
            DrawCalls++;
            Balance -= DebitOverride ?? amount;
            return DrawAccepted;
        }

        public int? ReadBalance(bool cash)
        {
            Assert.That(cash, Is.EqualTo(IsConverter));
            return WriteCalls == 0 || PostWriteBalanceReadable ? Balance : null;
        }

        public Func<bool>? WriteBalance(bool cash, int restored)
        {
            Assert.That(cash, Is.EqualTo(IsConverter));
            WriteCalls++;
            Balance = restored;
            return VerifySourceState;
        }
    }
}
