"""Static regression for the retired quantity-ledger, snapshot codec, journal, and 9x3 page backend.

These checks read source text only. They cannot prove native behavior; they exist so a removed
storage backend cannot be reintroduced alongside the unavailable result.
"""

import unittest
from pathlib import Path

REPOSITORY = Path(__file__).resolve().parents[3]
PLATFORM = REPOSITORY / "src" / "ProbablyStolen.DigitalStorage.Platform"
NATIVE = PLATFORM / "NativeContent"
NETWORK = PLATFORM / "Network"
ENERGY = PLATFORM / "Energy"
CONTENT = PLATFORM / "Content"

RETIRED_PATHS = (
    "src/ProbablyStolen.DigitalStorage.Common/appeng/api/stacks/AEKey.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/api/stacks/AEItemKey.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/api/stacks/KeyCounter.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/api/storage/Actionable.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/api/storage/MEStorage.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/api/storage/cells/CellState.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/api/storage/cells/StorageCell.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/api/networking/security/IActionSource.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/me/storage/GridStorage.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/me/storage/GridStorageStatus.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/me/storage/StorageTransactionException.cs",
    "src/ProbablyStolen.DigitalStorage.Common/blockentity/inventory/AppEngCellInventory.cs",
    "src/ProbablyStolen.DigitalStorage.Common/items/materials/StorageComponentItem.cs",
    "src/ProbablyStolen.DigitalStorage.Common/recipes/game/StorageCellDisassemblyRecipe.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/me/cells/BasicCellInventory.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/api/storage/cells/StorageCellSnapshot.cs",
    "src/ProbablyStolen.DigitalStorage.Common/Ports/IStorageCellPersistencePort.cs",
    "src/ProbablyStolen.DigitalStorage.Common/appeng/api/storage/StorageHelper.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeStorageTransferJournal.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeStorageItemCodec.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/INativeItemStorageCodec.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeInventoryGraphCodec.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeStorageContainerCodec.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeItemFeatureCodec.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeInventoryCallbackCodec.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeFeatureSnapshot.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeItemCreationScope.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeStorageFailure.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeStorageCellPersistence.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeComponentInventoryPage.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeStorageCellFactory.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeItemFactory.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeFactoryInspector.cs",
    "src/ProbablyStolen.DigitalStorage.Platform/NativeContent/NativeFactoryResourceScope.cs",
)

RETIRED_SYMBOLS = (
    "AEItemKey",
    "AEKey",
    "KeyCounter",
    "Actionable",
    "IActionSource",
    "StorageTransactionException",
    "AppEngCellInventory",
    "StorageComponentItem",
    "ProbablyStolen.DigitalStorage.Common.appeng",
    "BasicCellInventory",
    "StorageCellSnapshot",
    "IStorageCellPersistencePort",
    "StorageHelper",
    "PoweredExtractionStatus",
    "NativeStorageTransferJournal",
    "NativeStorageItemCodec",
    "INativeItemStorageCodec",
    "NativeInventoryGraphCodec",
    "NativeStorageContainerCodec",
    "NativeItemFeatureCodec",
    "NativeInventoryCallbackCodec",
    "NativeFeatureSnapshot",
    "NativeItemCreationScope",
    "NativeStorageFailure",
    "NativeStorageCellPersistence",
    "NativeStorageCellCodecSettings",
    "NativeComponentInventoryPage",
    "NativeStorageCellFactory",
    "NativeItemFactory",
    "NativeFactoryInspector",
    "NativeFactoryResourceScope",
    "HasStoredContents",
)


def source_files(*roots):
    for root in roots:
        for path in sorted(root.rglob("*.cs")):
            relative = path.relative_to(REPOSITORY).as_posix()
            if "/obj/" in relative or "/bin/" in relative:
                continue
            yield path, relative


class RetirementContractTests(unittest.TestCase):
    def test_retired_backend_sources_are_absent(self):
        present = [relative for relative in RETIRED_PATHS if (REPOSITORY / relative).exists()]
        self.assertEqual(present, [], "Retired storage backend sources must stay deleted.")

    def test_no_product_or_test_source_references_retired_symbols(self):
        hits = []
        for path, relative in source_files(REPOSITORY / "src", REPOSITORY / "tests"):
            text = path.read_text(encoding="utf-8")
            hits.extend(f"{relative}: {symbol}" for symbol in RETIRED_SYMBOLS if symbol in text)
        self.assertEqual(hits, [], "The retired storage backend must have no remaining reference.")

    def test_mounted_cells_are_native_items_without_a_parallel_ledger(self):
        # Stored items live on the cell's native pages; the resolver only finds the mounted cells.
        resolver = (NETWORK / "NativeCellResolver.cs").read_text(encoding="utf-8")
        self.assertIn("internal static IReadOnlyList<GameItem> GetCellItems(GameItem drive)", resolver)
        for forbidden in ("CellMetadataStore", "JsonConvert", "TransferContext", "GetAvailableStacks"):
            self.assertNotIn(forbidden, resolver)

    def test_terminal_storage_goes_through_network_storage_only(self):
        coordinator = (NETWORK / "GridNetworkPowerCoordinator.cs").read_text(encoding="utf-8")
        for required in ("StoreSelected(GameItem terminal",
                         "StoreSingle(GameItem terminal", "CanStore(GameItem terminal",
                         "ExtractToPlayer(GameItem terminal", "_storage.Store(", "_storage.Extract("):
            self.assertIn(required, coordinator)
        for forbidden in ("storage.insert(", "storage.extract(", "PoweredExtraction", "Journal", "Codec"):
            self.assertNotIn(forbidden, coordinator)

    def test_terminal_rejections_do_not_call_storage_operations(self):
        storage = (PLATFORM / "Storage" / "NetworkStorage.cs").read_text(encoding="utf-8")
        self.assertIn("terminal_storage_unavailable", storage)
        coordinator = (NETWORK / "GridNetworkPowerCoordinator.cs").read_text(encoding="utf-8")
        for forbidden in ("storage.insert(", "storage.extract(", "PoweredExtraction", "Journal", "Codec"):
            self.assertNotIn(forbidden, coordinator)

    def test_fixed_terminal_geometry_and_cell_bays_are_unchanged(self):
        metrics = (REPOSITORY / "src" / "ProbablyStolen.DigitalStorage.Client"
                   / "Terminal" / "Presentation" / "TerminalWindowMetrics.cs")
        self.assertTrue(metrics.is_file(), "TerminalWindowMetrics defines the fixed 7x4 slot layout.")
        drive = (CONTENT / "DriveItem.cs").read_text(encoding="utf-8")
        self.assertIn("new GridPixelElement(7, 2)", drive)
        self.assertIn("new DriveCellInventory(recipe)", (PLATFORM / "Composition" / "PlatformComposition.cs")
                      .read_text(encoding="utf-8"))
        bays = (REPOSITORY / "src" / "ProbablyStolen.DigitalStorage.Common" / "Domain" / "Machines"
                / "DriveCellInventory.cs").read_text(encoding="utf-8")
        self.assertIn("public int Size() => 14;", bays)

    def test_grid_storage_keeps_only_the_read_only_query(self):
        # The grid view must stay a query over mounted cells; the retired quantity planner, commit, and rollback
        # algorithm cannot reappear behind it, and it must not implement the storage mutation contract.
        query = (REPOSITORY / "src" / "ProbablyStolen.DigitalStorage.Common" / "Application" / "Queries"
                 / "GridStorage.cs").read_text(encoding="utf-8")
        for required in ("GetStatus(", "IsAvailable("):
            self.assertIn(required, query)
        for forbidden in ("IStorageInventory", "BuildPlan", "CommitPlan", "Rollback", "ValidateOperation",
                          "StorageOperation", "StorageTransactionException", "public long Insert(",
                          "public long Extract("):
            self.assertNotIn(forbidden, query)


if __name__ == "__main__":
    unittest.main()
