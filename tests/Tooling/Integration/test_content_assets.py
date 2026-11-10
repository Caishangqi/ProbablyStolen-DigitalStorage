"""Resource checks exercise the actual preparation functions with temporary assets."""

import hashlib
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

REPOSITORY = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPOSITORY / "scripts"))
from digital_storage_tools.assets import (  # noqa: E402
    ASSETS, ContentValidationError, check_assets, check_image, image_api,
    check_content, format_parameters, main, prepare_assets, read_localization, validate_write_root,
)


class ContentContractTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="digital-storage-contract-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.platform = self.root / "src" / "ProbablyStolen.DigitalStorage.Platform"
        original = REPOSITORY / "src" / self.platform.name
        for name in ("Content", "NativeContent"):
            shutil.copytree(original / name, self.platform / name)
        catalog = Path("src/ProbablyStolen.DigitalStorage.Common/Domain/Storage/StorageCellTier.cs")
        (self.root / catalog).parent.mkdir(parents=True)
        shutil.copy2(REPOSITORY / catalog, self.root / catalog)
        self.output = self.root / "resources"
        for name in ("Localization", "Search", "Licenses"):
            shutil.copytree(REPOSITORY / "packaging/resources" / name, self.output / name)

    def test_tier_vendor_value_and_resource_wiring(self):
        vendor = (self.platform / "Content/StorageVendors.cs").read_text(encoding="utf-8")
        self.assertIn("RNG.Roll(10)) PlayerStore.Instance.AddDirectSellingItemToTable(DrawTier().ComponentId)", vendor)
        self.assertIn("StorageCellTier.All.Select(tier => tier.ComponentId)", vendor)
        self.assertIn("roll <= 40 ? DrawTier().ComponentId", vendor)
        self.assertIn("roll <= 70 ? DrawTier().CellId", vendor)
        self.assertIn("RNG.GetRandomInt(1, 100)", vendor)
        definitions = (self.platform / "Content/ItemContentSynchronization.cs").read_text(encoding="utf-8")
        self.assertIn("tier.ComponentValue", definitions)
        self.assertIn('cellKey + "_desc", ItemPrices.CellHousing', definitions)
        prices = (self.platform / "Content/ItemPrices.cs").read_text(encoding="utf-8")
        self.assertRegex(prices, r"CellHousing\s*=\s*80")
        composition = (REPOSITORY / "src" / self.platform.name / "Composition/PlatformComposition.cs").read_text(encoding="utf-8")
        self.assertIn('_appearance.EnsureReady("item_storage_cell_" + tier.Name)', composition)
        self.assertIn('_appearance.EnsureReady("cell_component_" + tier.Name)', composition)

    def test_tier_catalog_drift_fails(self):
        path = self.root / "src/ProbablyStolen.DigitalStorage.Common/Domain/Storage/StorageCellTier.cs"
        path.write_text(path.read_text(encoding="utf-8").replace('"256k", 256, 20250, 2', '"256k", 256, 20251, 2'), encoding="utf-8")
        with self.assertRaisesRegex(ContentValidationError, "tier catalog"):
            check_content(self.root, self.output)

    def test_actual_contract_and_resources_pass(self):
        check_content(self.root, self.output)

    def test_missing_search_resources_are_rejected(self):
        for name in ("Search/pinyin-dictionary.txt", "Licenses/PininSharp-LICENSE.txt",
                     "Licenses/pinyin-data-LICENSE.txt", "Licenses/Rime-Terra-Pinyin-LICENSE.txt"):
            with self.subTest(resource=name):
                path = self.output / name
                content = path.read_bytes()
                path.unlink()
                try:
                    with self.assertRaisesRegex(ContentValidationError, "Required search resource"):
                        check_content(self.root, self.output)
                finally:
                    path.write_bytes(content)

    def test_pinyin_dictionary_read_is_deferred_until_own_mod_assets_are_loaded(self):
        original = REPOSITORY / "src" / self.platform.name / "Composition"
        source = (original / "TerminalSearchComposition.cs").read_text(encoding="utf-8")
        constructor = source[source.index("internal TerminalSearchComposition("):
                             source.index("internal TerminalSearchService Service")]
        self.assertNotIn("ReadModFileText", constructor)
        self.assertIn('registry.RegisterMatcher("pinyin", _pinyin.Matches)', constructor)
        load = source[source.index("internal void LoadDictionary()"):
                      source.index("internal void Update(")]
        self.assertIn('ModHelper.ReadModFileText(_modId, "Search/pinyin-dictionary.txt")', load)
        self.assertIn("_pinyin.LoadDictionary(dictionary)", load)
        self.assertIn("_service.UpdateConfiguration(_service.Configuration)", load)
        platform = (original / "PlatformComposition.cs").read_text(encoding="utf-8")
        callback = platform[platform.index("private void OnAssetsLoaded("):
                            platform.index("private void ValidateResources()")]
        self.assertIn("mod.Manifest.ID != _modId", callback)
        self.assertIn("_search.LoadDictionary()", callback)

    def test_assembled_cell_sprite_refreshes_live_view_without_move(self):
        appearance = (self.platform / "Content/ItemAppearance.cs").read_text(encoding="utf-8")
        self.assertRegex(
            appearance,
            r"item\.useSpriteFromMod = true;\s*item\.SetSpriteQuick\(_modId, spriteKey\);",
        )

    def test_drive_priority_panel_and_input_wiring(self):
        original = REPOSITORY / "src" / self.platform.name
        window = (original / "Client/MachineContentWindow.cs").read_text(encoding="utf-8")
        panel_order = ('power,', '"modules_panel"', '"cells_panel"', '"priority_configure"')
        positions = [window.index(part) for part in panel_order]
        self.assertEqual(positions, sorted(positions))
        self.assertIn('priority.Content, "priority"', window)
        self.assertIn("openCallback += priority.Refresh", window)
        self.assertNotIn("AddHeaderButton", window)

        priority = (original / "Client/DrivePriorityWindow.cs").read_text(encoding="utf-8")
        self.assertRegex(priority, r"Button\(100\), Button\(10\), Button\(1\), _fieldHost,\s*"
                                   r"Button\(-1\), Button\(-10\), Button\(-100\)")
        self.assertIn("TextAlignmentOptions.Center", priority)
        self.assertIn("_field.onValueChanged.AddListener(Typed)", priority)
        self.assertIn("_field.onEndEdit.AddListener(_ => ShowCurrent())", priority)
        self.assertRegex(priority, r"if \(!DrivePriority\.TryParse\(text\?\.Trim\(\), out var priority\)\) return;\s*"
                                   r"DrivePriorityTag\.Write\(_drive, priority\);")
        self.assertIn("DrivePriority.Clamp((long)DrivePriorityTag.Read(_drive) + delta)", priority)

    def test_native_content_callbacks_use_the_tested_production_coordinators(self):
        original = REPOSITORY / "src" / self.platform.name
        cell = (original / "Client/CellContentWindow.cs").read_text(encoding="utf-8")
        for callback in ("CanInsert", "CanRemove", "Removing", "Inserted", "Removed", "IsConsistent"):
            self.assertIn("_callbacks." + callback + "(", cell)
        self.assertIn("new CellAssemblyCallbacks(recipe, this)", cell)
        self.assertIn("ICellAssemblyPort.Read() => ContentSnapshots.Cell(_host)", cell)
        drive = (original / "Content/DriveItem.cs").read_text(encoding="utf-8")
        self.assertIn("new DriveBayBinding(_inventory, ports)", drive)
        self.assertIn("binding.CanInsert(", drive)
        self.assertIn("ports[index] = new NativeCellBay(bay)", drive)
        window = (original / "Client/MachineContentWindow.cs").read_text(encoding="utf-8")
        self.assertIn("new MachineContentLifecycle(new NativeMachineContents(machine))", window)
        self.assertIn("openCallback += () => lifecycle.RestoreIfReady(SaveManager.IsDecoding)", window)
        self.assertIn("onOneSecondItemFunc += (_, _, _) => lifecycle.RestoreIfReady(SaveManager.IsDecoding)", window)

    def test_network_power_config_and_native_controller_battery_wiring(self):
        original = REPOSITORY / "src" / self.platform.name
        config = (original / "Composition/DigitalStorageSettings.cs").read_text(encoding="utf-8")
        self.assertIn("ModHelper.GetConfig(modId)", config)
        for key, default in (("controller_power_usage", "3"), ("drive_power_usage", "2"), ("terminal_power_usage", "1")):
            self.assertIn(f'"{key}", {default}', config)
        for key in ("network_power_controller_name", "network_power_controller_desc",
                    "network_power_drive_name", "network_power_drive_desc",
                    "network_power_terminal_name", "network_power_terminal_desc"):
            self.assertIn(f'text.Get("{key}")', config)
        # Numbers are typed into text boxes: no slider range, every read is clamped instead.
        self.assertNotIn("min:", config)
        self.assertIn("internal const int MaximumPowerUsage = 1000", config)
        self.assertIn("}, 0, MaximumPowerUsage);", config)
        window = (original / "Client/MachineContentWindow.cs").read_text(encoding="utf-8")
        self.assertIn("if (acceptsBattery)", window)
        self.assertIn("MachineHelper.SetupBatterySlot(battery, machine)", window)
        self.assertIn("battery.maxSlotAddItemFunc = (_, _, _) => 0", window)
        self.assertIn("machine.onCycleEndLateSlotItemFunc += (_, _, _) => _networkPower.OnControllerCycleEnd(machine)", window)
        self.assertNotIn("onTurnEndSlotItemFunc", window)
        controller = (original / "Content/ControllerItem.cs").read_text(encoding="utf-8")
        self.assertIn("acceptsBattery: true, consumesNetworkPower: true", controller)
        drive = (original / "Content/DriveItem.cs").read_text(encoding="utf-8")
        terminal = (original / "Content/TerminalItem.cs").read_text(encoding="utf-8")
        self.assertIn("_windows.Attach(item, bays, drivePriority: true)", drive)
        self.assertNotIn("configurePartitions:", drive)
        self.assertNotIn("new CellPartitionSource(slots[index])", drive)
        self.assertIn("_windows.Attach(item)", terminal)
        scanner = (original / "Network/NativeNetworkScanner.cs").read_text(encoding="utf-8")
        self.assertIn("FindAdjacentType<GameItem>()", scanner)
        self.assertIn("ReferenceEquals(item.parentInventory, parentInventory)", scanner)
        self.assertNotIn("GetUniqueID()", scanner)
        self.assertIn("foreach (var item in parentInventory.childItems)", scanner)
        self.assertNotIn("GameMaster", scanner)
        self.assertNotIn("shipManager", scanner)
        controller = (original / "Energy/NativeEnergySource.cs").read_text(encoding="utf-8")
        self.assertIn("PowerHelper.CanDrawPowerSource(source, amount)", controller)
        self.assertIn("PowerHelper.DrawPowerSource(source, amount)", controller)
        coordinator = (original / "Network/GridNetworkPowerCoordinator.cs").read_text(encoding="utf-8")
        cycle_method = coordinator[coordinator.index("internal GridPowerResult OnControllerCycleEnd"):
                                  coordinator.index("    internal bool IsPowered", coordinator.index("internal GridPowerResult OnControllerCycleEnd"))]
        self.assertLess(cycle_method.index("if (grid.GetPathingService().GetControllerState() != ControllerState.Online)"),
                        cycle_method.index("snapshot.FindController(grid)"))
        presenter = (original / "Client/GridNetworkTooltipPresenter.cs").read_text(encoding="utf-8")
        for key in ("network_status_online", "network_status_unpowered",
                    "network_status_no_controller", "network_status_controller_conflict",
                    "network_tooltip_drive_slots", "network_module_stats_scope"):
            self.assertIn(key, presenter)
        self.assertNotIn("OnControllerCycleEnd", presenter)
        self.assertNotIn("TryDraw(", presenter)
        composition = (original / "Composition/PlatformComposition.cs").read_text(encoding="utf-8")
        self.assertIn("OnCreateTooltipLate += _networkTooltipPresenter.OnCreateTooltipLate", composition)
        self.assertIn("OnCreateTooltipLate -= _networkTooltipPresenter.OnCreateTooltipLate", composition)

    def test_unsupported_or_changed_constant_and_class_name_fail(self):
        path = self.platform / "Content/TerminalItem.cs"
        original = path.read_text(encoding="utf-8")
        for old, new in [('Width = 3;', 'Width = 1 + 2;'), ('Width = 3;', 'Width = 2;'),
                         ('class TerminalItem', 'class WrongClass'), ('terminal_name', 'drive_name')]:
            with self.subTest(new=new):
                path.write_text(original.replace(old, new), encoding="utf-8")
                with self.assertRaises(ContentValidationError):
                    check_content(self.root, self.output)

    def test_removed_command_source_and_language_keys_are_rejected(self):
        path = self.platform / "Diagnostics/ItemCommands.cs"
        path.parent.mkdir()
        path.write_text("// stale command source\n", encoding="utf-8")
        with self.assertRaisesRegex(ContentValidationError, "Removed custom command"):
            check_content(self.root, self.output)
        path.unlink()
        language = self.output / "Localization/en.csv"
        language.write_text(language.read_text(encoding="utf-8") + "command_spawn_help,obsolete\n", encoding="utf-8")
        with self.assertRaisesRegex(ContentValidationError, "required content keys"):
            check_content(self.root, self.output)

    def test_missing_empty_duplicate_bom_multiline_and_placeholder_drift_fail(self):
        path = self.output / "Localization/zh.csv"
        original = path.read_text(encoding="utf-8")
        first = original.splitlines()[0]
        key, value = first.split(",", 1)
        variants = [
            original.split("\n", 1)[1],
            original + key + ",duplicate\n",
            original.replace(first, key + ",", 1),
            "\ufeff" + original,
            original + "unexpected_key,extra\n",
            original.replace(first, first + " {0}", 1),
            original + 'bad,"real\nmultiline"\n',
            original.replace(first, first + " {name}", 1),
        ]
        for content in variants:
            with self.subTest(content=content[-50:]):
                self.assertNotEqual(content, original, "A negative fixture must actually change the source.")
                path.write_bytes(content.encode("utf-8"))
                with self.assertRaises(ContentValidationError):
                    check_content(self.root, self.output)

    def test_native_first_comma_quotes_and_literal_newline_decode(self):
        path = self.root / "quotes.csv"
        path.write_text('first,"a,b and ""quoted"" text\\nnext"\nsecond,unquoted,comma\n', encoding="utf-8")
        self.assertEqual(read_localization(path), {'first': 'a,b and "quoted" text\nnext', 'second': 'unquoted,comma'})
        for invalid in ('key,"unclosed', 'key,unescaped"quote', 'key,"single"quote"'):
            path.write_text(invalid, encoding="utf-8")
            with self.assertRaises(ContentValidationError):
                read_localization(path)
        self.assertEqual(format_parameters('{{literal}} {0} {0}'), [0, 0])


if __name__ == "__main__":
    unittest.main()
