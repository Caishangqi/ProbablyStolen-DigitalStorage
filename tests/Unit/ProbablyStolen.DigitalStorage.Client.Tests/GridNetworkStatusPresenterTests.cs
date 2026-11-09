using System;
using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Client.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;

namespace ProbablyStolen.DigitalStorage.Client.Tests;

[TestFixture]
public sealed class GridNetworkStatusPresenterTests
{
    [TestCase(ControllerState.NoController, true, GridNetworkDisplayStatus.NoController)]
    [TestCase(ControllerState.NoController, false, GridNetworkDisplayStatus.NoController)]
    [TestCase(ControllerState.ControllerConflict, true, GridNetworkDisplayStatus.ControllerConflict)]
    [TestCase(ControllerState.ControllerConflict, false, GridNetworkDisplayStatus.ControllerConflict)]
    [TestCase(ControllerState.Online, false, GridNetworkDisplayStatus.Unpowered)]
    [TestCase(ControllerState.Online, true, GridNetworkDisplayStatus.Online)]
    public void Resolve_MapsTopologyAndPowerToPlayerFacingState(
        ControllerState controllerState, bool isPowered, GridNetworkDisplayStatus expected)
    {
        Assert.That(GridNetworkStatusPresenter.Resolve(controllerState, isPowered), Is.EqualTo(expected));
    }

    [Test]
    public void Resolve_UnknownTopology_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            GridNetworkStatusPresenter.Resolve((ControllerState)99, false)));
    }
}
