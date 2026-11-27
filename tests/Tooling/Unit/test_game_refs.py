import json
import sys
import tempfile
import unittest
from pathlib import Path

REPOSITORY = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPOSITORY / "scripts"))

from digital_storage_tools import game_refs  # noqa: E402
from digital_storage_tools.decompile import WorkflowError  # noqa: E402


def appmanifest(build_id, mounted_beta=None, selected_beta=None):
    user = f'"UserConfig" {{ "language" "english" "BetaKey" "{selected_beta}" }}' if selected_beta is not None else ""
    mounted = f'"MountedConfig" {{ "language" "english" "BetaKey" "{mounted_beta}" }}' if mounted_beta is not None else ""
    return (f'"AppState" {{ "appid" "4348910" "buildid" "{build_id}" "installdir" "Probably Stolen" '
            f'"InstalledDepots" {{ "4348911" {{ "manifest" "1" }} }} {user} {mounted} }}')


class GameSnapshotTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.game = self.root / "steamapps" / "common" / "Probably Stolen"
        self.managed = self.game / "Probably Stolen_Data" / "Managed"
        self.managed.mkdir(parents=True)
        (self.managed / "Assembly-CSharp.dll").write_bytes(b"game")
        (self.managed / "UnityEngine.dll").write_bytes(b"engine")
        self.repo = self.root / "repo"
        self.repo.mkdir()
        self.write_manifest("12345", mounted_beta="open_beta", selected_beta="open_beta")

    def write_manifest(self, build_id, **branches):
        (self.root / "steamapps" / "appmanifest_4348910.acf").write_text(appmanifest(build_id, **branches), encoding="utf-8")

    def test_branch_comes_from_the_mounted_files_and_defaults_to_public(self):
        self.assertEqual(game_refs.installed_branch(self.game), "open_beta")
        self.write_manifest("12345", mounted_beta="", selected_beta="open_beta")
        self.assertEqual(game_refs.installed_branch(self.game), "public")
        self.write_manifest("12345")
        self.assertEqual(game_refs.installed_branch(self.game), "public")

    def test_snapshot_copies_managed_dlls_under_branch_and_build(self):
        target = game_refs.snapshot(self.game, self.repo)

        self.assertEqual(target, (self.repo / "game-refs" / "open_beta-12345").resolve())
        self.assertEqual((target / "Probably Stolen_Data" / "Managed" / "Assembly-CSharp.dll").read_bytes(), b"game")
        manifest = json.loads((target / "snapshot-manifest.json").read_text(encoding="utf-8"))
        self.assertEqual((manifest["steamBranch"], manifest["steamBuildId"]), ("open_beta", "12345"))
        self.assertEqual({entry["name"] for entry in manifest["assemblies"]}, {"Assembly-CSharp.dll", "UnityEngine.dll"})
        self.assertEqual(game_refs.snapshots(self.repo), [target])

    def test_snapshot_is_idempotent_and_refuses_different_files_for_the_same_build(self):
        first = game_refs.snapshot(self.game, self.repo)
        self.assertEqual(game_refs.snapshot(self.game, self.repo), first)
        (self.managed / "Assembly-CSharp.dll").write_bytes(b"patched")
        with self.assertRaisesRegex(WorkflowError, "different DLLs"):
            game_refs.snapshot(self.game, self.repo)

    def test_each_branch_keeps_its_own_snapshot(self):
        beta = game_refs.snapshot(self.game, self.repo)
        self.write_manifest("11111", mounted_beta="")
        public = game_refs.snapshot(self.game, self.repo)
        self.assertEqual(public.name, "public-11111")
        self.assertEqual(game_refs.snapshots(self.repo), sorted([beta, public]))

    def test_package_build_record_names_the_build_and_rejects_a_changed_package(self):
        package = self.root / "package"
        package.mkdir()
        (package / "Mod.dll").write_bytes(b"mod")
        output = self.root / "artifacts"
        game_refs.record_package_build(self.game, package, output, ["Mod.dll"])

        self.assertEqual(game_refs.read_package_build(output, package, ["Mod.dll"]), ("open_beta", "12345"))
        (package / "Mod.dll").write_bytes(b"rebuilt")
        with self.assertRaisesRegex(WorkflowError, "changed since they were built"):
            game_refs.read_package_build(output, package, ["Mod.dll"])


if __name__ == "__main__":
    unittest.main()
