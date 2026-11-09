"""Repository tooling command dispatcher."""

from __future__ import annotations

import argparse
import importlib.metadata
import shutil
import subprocess
import sys
from pathlib import Path


def check_environment() -> None:
    root = Path(__file__).resolve().parents[2]
    if Path(sys.prefix).resolve() != (root / ".venv").resolve():
        raise RuntimeError("Use the repository .venv; run mise run setup first.")
    if sys.version_info[:3] != (3, 13, 7):
        raise RuntimeError("Python 3.13.7 is required; run mise run setup.")
    try:
        version = importlib.metadata.version("Pillow")
    except importlib.metadata.PackageNotFoundError as error:
        raise RuntimeError("Pillow 12.0.0 is required; run mise run setup.") from error
    if version != "12.0.0":
        raise RuntimeError(f"Pillow 12.0.0 is required, found {version}; run mise run setup.")
    mise = shutil.which("mise")
    if mise is None:
        raise RuntimeError("mise is required to check the locked environment; install mise and run mise run setup.")
    result = subprocess.run(
        [mise, "exec", "--", "uv", "sync", "--check", "--locked", "--offline", "--no-python-downloads"],
        cwd=root, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, check=False,
    )
    if result.returncode:
        raise RuntimeError("The project environment or lock is out of date; run mise run setup.")


def _iter_tests(suite):
    import unittest
    for test in suite:
        if isinstance(test, unittest.TestSuite):
            yield from _iter_tests(test)
        else:
            yield test


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    from .commands import PUBLIC
    commands = ("environment", "assets", "internal-deployment", "internal-workshop-upload", "verify-layout", "inventory-tests", "verify-platform-review", "validate-trx") + PUBLIC
    subparsers = parser.add_subparsers(dest="command", required=True)
    for command in commands:
        subparsers.add_parser(command, add_help=command in ("verify-layout", "test-tools-unit", "test-tools-integration"))
    args, remaining = parser.parse_known_args(argv)
    try:
        check_environment()
    except RuntimeError as error:
        print(f"Tool environment error: {error}", file=sys.stderr)
        return 2
    if args.command == "validate-trx":
        from .trx import validate
        from .platform_review import validate as validate_review
        try:
            if len(remaining) not in (1, 2) or (len(remaining) == 2 and remaining[1] != '--platform'):
                raise ValueError('validate-trx requires a report and optional --platform')
            approved = validate_review()['tests'] if len(remaining) == 2 else None
            print(validate(Path(remaining[0]), approved))
            return 0
        except (OSError, ValueError, KeyError, __import__('xml.etree.ElementTree', fromlist=['ParseError']).ParseError) as error:
            print(f'Test report error: {error}', file=sys.stderr)
            return 1
    if args.command == "verify-platform-review":
        from .platform_review import validate
        try:
            if remaining not in ([], ["--tests-only"]):
                raise ValueError("verify-platform-review takes only an optional --tests-only")
            validate(tests_only=bool(remaining))
            return 0
        except (OSError, ValueError, KeyError) as error:
            print(f"Platform review error: {error}", file=sys.stderr)
            return 1
    if args.command == "inventory-tests":
        import os
        root = Path(__file__).resolve().parents[2]
        def error(error):
            raise error
        for directory, names, files in os.walk(root / "tests", onerror=error, followlinks=False):
            for name in list(names):
                path = Path(directory) / name
                status = path.lstat()
                if path.is_symlink() or getattr(status, "st_file_attributes", 0) & 0x400:
                    print("Linked test directory rejected", file=sys.stderr)
                    return 1
                if name.lower() in ("bin", "obj", "artifacts"):
                    names.remove(name)
            for name in files:
                if name.endswith(".csproj"):
                    path = Path(directory) / name
                    status = path.lstat()
                    if path.is_symlink() or getattr(status, "st_file_attributes", 0) & 0x400:
                        print("Linked test project rejected", file=sys.stderr)
                        return 1
                    print(path.relative_to(root).as_posix())
        return 0
    if args.command in ("test-tools-unit", "test-tools-integration"):
        if remaining:
            parser.error(f"{args.command} takes no arguments")
        import unittest
        root = Path(__file__).resolve().parents[2]
        test_dir = "Unit" if args.command == "test-tools-unit" else "Integration"
        suite = unittest.defaultTestLoader.discover(str(root / "tests/Tooling" / test_dir), pattern="test_*.py")
        expected = {path.resolve() for path in (root / "tests/Tooling" / test_dir).rglob("test_*.py")}
        discovered = {Path(sys.modules[test.__class__.__module__].__file__).resolve()
                      for group in suite for test in _iter_tests(group)
                      if getattr(sys.modules.get(test.__class__.__module__), "__file__", None)}
        if expected != discovered:
            print("Tooling test modules were not all discovered.", file=sys.stderr)
            return 1
        if suite.countTestCases() == 0:
            print("No tooling unit tests discovered.", file=sys.stderr)
            return 1
        result = unittest.TextTestRunner(stream=sys.stdout, verbosity=1).run(suite)
        return 0 if result.wasSuccessful() and not result.skipped else 1
    from . import assets, decompile, deployment, environment, extract_assets, package, workshop
    if args.command in ("check-assets", "prepare-assets"):
        remaining = ["--check" if args.command == "check-assets" else "--write", *remaining]
        return assets.main(remaining)
    if args.command in ("decompile", "extract-assets"):
        import os
        from .commands import local_environment
        try:
            os.environ.update(local_environment())
        except (ValueError, OSError) as error:
            print(f"Configuration error: {error}", file=sys.stderr)
            return 2
    commands = {"environment": environment.main, "assets": assets.main, "decompile": decompile.main,
                "extract-assets": extract_assets.main, "verify-package": package.main,
                "internal-deployment": deployment.main, "internal-workshop-upload": workshop.main}
    if args.command == "verify-layout":
        if remaining:
            parser.error("verify-layout takes no arguments")
        try:
            package.load_layouts()
        except (package.PackageValidationError, OSError, ValueError) as error:
            print(f"Package layout error: {error}", file=sys.stderr)
            return 2
        return 0
    if args.command in commands:
        return commands[args.command](remaining)
    from .commands import run
    try:
        return run(args.command, remaining)
    except (ValueError, OSError) as error:
        print(f"Command error: {error}", file=sys.stderr)
        return 2
