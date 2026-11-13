import sys
import tempfile
import unittest
from types import SimpleNamespace
from unittest.mock import patch
from pathlib import Path

REPOSITORY = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPOSITORY / "scripts"))

from digital_storage_tools.environment import (  # noqa: E402
    EnvironmentResolutionError,
    REQUIRED_GAME_FILES,
    parse_arguments,
    resolve_environment,
)


class ResolveEnvironmentTests(unittest.TestCase):
    def setUp(self):
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name).resolve() / "Digital Storage Repo"
        self.root.mkdir()
        self.config = self.root / "mise.toml"
        self.config.write_text("[env]\nArtifactsPath = { default = 'artifacts' }\n", encoding="utf-8")

    def create_game(self, path: Path, missing: Path | None = None) -> Path:
        path.mkdir(parents=True)
        for relative in REQUIRED_GAME_FILES:
            if relative == missing:
                continue
            target = path / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(b"test input")
        return path

    def resolve(self, arguments: list[str], environment: dict[str, str] | None = None):
        return resolve_environment(
            parse_arguments(arguments),
            environ={} if environment is None else environment,
            repository_root=self.root,
        )

    def test_core_target_ignores_missing_local_game_configuration(self):
        result = self.resolve([], {"DIGITAL_STORAGE_GAME_DIR": r"Z:\missing\Probably Stolen"})

        self.assertIsNone(result.game_directory)
        self.assertEqual(result.output_directory, self.root / "artifacts")
        self.assertEqual(result.package_directory, self.root / "artifacts" / "package" / "DigitalStorage")
        self.assertEqual(result.sources["game_directory"], "unused")

    def test_command_line_game_path_takes_precedence_over_environment(self):
        game = self.create_game(self.root / "Install With Spaces" / "Probably Stolen")
        result = self.resolve(
            ["--target", "game", "--game-dir", str(game)],
            {"DIGITAL_STORAGE_GAME_DIR": r"Z:\missing\Probably Stolen"},
        )

        self.assertEqual(result.game_directory, game.resolve())
        self.assertEqual(result.sources["game_directory"], "cli")

    def test_default_does_not_require_mise_config(self):
        self.config.unlink()
        self.assertEqual(self.resolve([]).output_directory, self.root / "artifacts")

    def test_empty_values_fall_back(self):
        for args, env in ((["--artifacts-path", ""], {}), ([], {"ArtifactsPath": ""}),
                          (["--artifacts-path", ""], {"ArtifactsPath": "env"})):
            with self.subTest(args=args, env=env):
                result = self.resolve(args, env)
                self.assertEqual(result.output_directory, self.root / ("env" if env.get("ArtifactsPath") else "artifacts"))
                self.assertEqual(result.sources["output_directory"], "environment" if env.get("ArtifactsPath") else "default")

    def test_environment_output_path_takes_precedence_over_default(self):
        result = self.resolve([], {"ArtifactsPath": "out folder"})

        self.assertEqual(result.output_directory, (self.root / "out folder").resolve())
        self.assertEqual(result.sources["output_directory"], "environment")

    def test_command_line_output_and_package_override_environment(self):
        result = self.resolve(
            ["--artifacts-path", "cli output", "--package-dir", "cli package"],
            {
                "ArtifactsPath": "environment output",
                "DIGITAL_STORAGE_PACKAGE_DIR": "environment package",
            },
        )

        self.assertEqual(result.output_directory, (self.root / "cli output").resolve())
        self.assertEqual(result.package_directory, (self.root / "cli package").resolve())
        self.assertEqual(result.sources["output_directory"], "cli")
        self.assertEqual(result.sources["package_directory"], "cli")

    def test_steam_library_derives_game_install_with_spaces(self):
        library = self.root / "Steam Library"
        game = self.create_game(library / "steamapps" / "common" / "Probably Stolen")
        result = self.resolve(["--target", "game", "--steam-library-dir", str(library)])

        self.assertEqual(result.game_directory, game.resolve())
        self.assertEqual(result.steam_library_directory, library.resolve())
        self.assertEqual(result.sources["game_directory"], "derived")

    def test_matching_game_and_steam_paths_are_accepted(self):
        library = self.root / "Steam Library"
        game = self.create_game(library / "steamapps" / "common" / "Probably Stolen")
        result = self.resolve(
            ["--target", "game", "--game-dir", str(game), "--steam-library-dir", str(library)]
        )

        self.assertEqual(result.game_directory, game.resolve())

    def test_conflicting_game_and_steam_paths_are_rejected(self):
        library = self.root / "Steam Library"
        self.create_game(library / "steamapps" / "common" / "Probably Stolen")
        other_game = self.create_game(self.root / "Other Install" / "Probably Stolen")

        with self.assertRaisesRegex(EnvironmentResolutionError, "conflicts"):
            self.resolve(
                ["--target", "game", "--game-dir", str(other_game), "--steam-library-dir", str(library)]
            )

    def test_game_target_requires_a_configured_install(self):
        with self.assertRaisesRegex(EnvironmentResolutionError, "requires DIGITAL_STORAGE_GAME_DIR"):
            self.resolve(["--target", "game"])

    def test_missing_managed_assembly_is_reported(self):
        game = self.create_game(
            self.root / "Incomplete Install" / "Probably Stolen",
            missing=Path("Probably Stolen_Data/Managed/UnityEngine.CoreModule.dll"),
        )

        with self.assertRaisesRegex(EnvironmentResolutionError, "UnityEngine.CoreModule.dll"):
            self.resolve(["--game-dir", str(game)])

    def test_game_and_package_resolve_without_optional_console_sdk(self):
        for target in ("game", "package"):
            with self.subTest(target=target):
                game = self.create_game(self.root / f"Native Spawner {target}")
                for name in ("QFSW.QC.dll", "QFSW.QC.Suggestors.dll", "QFSW.QC.ScanRules.dll"):
                    self.assertFalse((game / "Probably Stolen_Data/Managed" / name).exists())
                before = sorted(p.relative_to(game) for p in game.rglob("*"))
                result = self.resolve(["--target", target, "--game-dir", str(game)])
                self.assertEqual(result.game_directory, game.resolve())
                self.assertEqual(before, sorted(p.relative_to(game) for p in game.rglob("*")))

    def test_artifacts_rejects_reparse_ancestor_before_resolution(self):
        linked = self.root / 'linked'
        original = Path.lstat
        def lstat(path):
            if path == linked:
                return SimpleNamespace(st_file_attributes=0x400, st_mode=0o040755)
            return original(path)
        with patch.object(Path, 'lstat', lstat):
            with self.assertRaisesRegex(EnvironmentResolutionError, 'reparse'):
                self.resolve(['--artifacts-path', str(linked / 'output')])

    def test_relative_output_cannot_escape_repository(self):
        with self.assertRaisesRegex(EnvironmentResolutionError, "remain inside the repository"):
            self.resolve(["--artifacts-path", "../outside"])

    def test_output_cannot_target_the_game_install_or_reference_tree(self):
        game = self.create_game(self.root / "Install" / "Probably Stolen")
        with self.assertRaisesRegex(EnvironmentResolutionError, "protected directory"):
            self.resolve(["--target", "game", "--game-dir", str(game), "--artifacts-path", str(game / "Mods")])
        with self.assertRaisesRegex(EnvironmentResolutionError, "protected directory"):
            self.resolve(["--artifacts-path", str(self.root / ".reference" / "output")])

    def test_outputs_cannot_target_manual_workshop_files(self):
        workshop = self.root / "workshop"
        changelog = workshop / "changelog" / "zh-CN.txt"
        changelog.parent.mkdir(parents=True)
        changelog.write_text("manual release notes", encoding="utf-8")
        for arguments, environment in (
            (["--artifacts-path", str(workshop / "artifacts")], {}),
            (["--package-dir", str(workshop / "package")], {}),
            ([], {"DIGITAL_STORAGE_DEPLOY_DIR": str(workshop / "Mods" / "DigitalStorage")}),
        ):
            with self.subTest(arguments=arguments, environment=environment):
                with self.assertRaisesRegex(EnvironmentResolutionError, "protected directory|manually managed Workshop"):
                    self.resolve(arguments, environment)
            self.assertEqual(changelog.read_text(encoding="utf-8"), "manual release notes")

    def test_repository_parent_output_can_contain_managed_workshop_directory(self):
        result = self.resolve(["--artifacts-path", str(self.root)])
        self.assertEqual(result.output_directory, self.root)
        self.assertEqual(result.package_directory, self.root / "package" / "DigitalStorage")

    def test_resolution_does_not_write_files(self):
        before = sorted(path.relative_to(self.root) for path in self.root.rglob("*"))
        self.resolve([])
        after = sorted(path.relative_to(self.root) for path in self.root.rglob("*"))

        self.assertEqual(after, before)


if __name__ == "__main__":
    unittest.main()
