using NUnit.Framework;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Recipes;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.IntegrationTests;

[TestFixture]
public sealed class StorageCellTierIntegrationTests
{
    [Test]
    public void DriveAdmission_UsesExactTierPairForEveryCell()
    {
        var drive = new DriveCellInventory(new StorageCellDisassemblyRecipe(new StorageComponent()));
        foreach (var cell in StorageCellTier.All)
        {
            foreach (var component in StorageCellTier.All)
            {
                Assert.That(drive.IsItemValid(0, cell.CellId, 1, 1, 2, component.ComponentId, 1),
                    Is.EqualTo(cell == component));
            }
        }
    }
}
