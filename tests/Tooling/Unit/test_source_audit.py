import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


REPOSITORY = Path(__file__).resolve().parents[3]
CHECKER = REPOSITORY / "scripts/check_quality.py"
SPEC = importlib.util.spec_from_file_location("project_quality_checker", CHECKER)
assert SPEC is not None and SPEC.loader is not None
quality = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(quality)


def write_files(root: Path, files: dict[str, str]) -> None:
    for name, content in files.items():
        target = root / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")


class SourceDiscoveryTests(unittest.TestCase):
    def test_git_discovery_audits_only_files_the_ignore_rules_admit(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            subprocess.run(["git", "init", "-q"], cwd=root, check=True)
            write_files(root, {
                ".gitignore": "/*\n!/.gitignore\n!/src/\n**/obj/\n",
                "src/real.cs": "class Real {}",
                "src/obj/generated.cs": "invalid generated content",
                "private/notes.json": "invalid local content",
                "local.py": "invalid local content",
            })

            self.assertEqual(quality.all_source_paths(root), {"src/real.cs"})

    def test_walk_discovery_prunes_generated_and_reference_directories(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            write_files(root, {
                "src/real.cs": "class Real {}",
                ".run/shared.xml": "<component />",
                "artifacts/engineering/baseline/old.py": "invalid generated content",
                ".venv/package/cached.py": "invalid environment content",
                ".reference/nested/upstream.cs": "invalid reference content",
            })
            visited = []
            original_walk = quality.os.walk

            def capture(path, *args, **kwargs):
                for directory, children, names in original_walk(path, *args, **kwargs):
                    visited.append(Path(directory).relative_to(root).as_posix())
                    yield directory, children, names

            with patch.object(quality.os, "walk", side_effect=capture):
                found = quality.walked_source_paths(root)

            self.assertEqual(found, {"src/real.cs", ".run/shared.xml"})
            self.assertFalse(any(name == "artifacts" or name.startswith("artifacts/") for name in visited))
            self.assertFalse(any(name == ".reference" or name.startswith(".reference/") for name in visited))

    def test_walk_discovery_reports_directory_errors(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)

            def broken_walk(path, **kwargs):
                kwargs["onerror"](PermissionError("denied"))
                yield from ()

            with patch.object(quality.os, "walk", side_effect=broken_walk):
                with self.assertRaisesRegex(RuntimeError, "Could not enumerate source directory"):
                    quality.walked_source_paths(root)


if __name__ == "__main__":
    unittest.main()
