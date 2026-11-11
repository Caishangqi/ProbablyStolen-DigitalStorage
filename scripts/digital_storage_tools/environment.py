"""Resolve local game and output paths for repository scripts."""

from __future__ import annotations

import argparse
import json
import os
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Mapping, Sequence


GAME_ENV = "DIGITAL_STORAGE_GAME_DIR"
STEAM_ENV = "DIGITAL_STORAGE_STEAM_LIBRARY_DIR"
PACKAGE_ENV = "DIGITAL_STORAGE_PACKAGE_DIR"
DEPLOY_ENV = "DIGITAL_STORAGE_DEPLOY_DIR"
REQUIRED_GAME_FILES = (
    Path("Probably Stolen.exe"),
    Path("Probably Stolen_Data/Managed/Assembly-CSharp.dll"),
    Path("Probably Stolen_Data/Managed/UnityEngine.dll"),
    Path("Probably Stolen_Data/Managed/UnityEngine.CoreModule.dll"),
)


class EnvironmentResolutionError(ValueError):
    """Raised when configured paths cannot be resolved safely."""


@dataclass(frozen=True)
class ResolvedEnvironment:
    repository_root: Path
    game_directory: Path | None
    steam_library_directory: Path | None
    output_directory: Path
    package_directory: Path
    deploy_directory: Path | None
    sources: Mapping[str, str]

    def to_dict(self) -> dict[str, object]:
        """Return paths and their configuration sources as JSON compatible values."""
        return {
            "repository_root": str(self.repository_root),
            "game_directory": str(self.game_directory) if self.game_directory else None,
            "steam_library_directory": str(self.steam_library_directory) if self.steam_library_directory else None,
            "output_directory": str(self.output_directory),
            "package_directory": str(self.package_directory),
            "deploy_directory": str(self.deploy_directory) if self.deploy_directory else None,
            "sources": dict(self.sources),
        }


def parse_arguments(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", choices=("core", "game", "package", "deploy"), default="core")
    parser.add_argument("--game-dir")
    parser.add_argument("--steam-library-dir")
    parser.add_argument("--artifacts-path")
    parser.add_argument("--package-dir")
    parser.add_argument("--deploy-dir")
    parser.add_argument("--json", action="store_true", help="Emit the resolved configuration as JSON.")
    return parser.parse_args(argv)


def resolve_environment(
    arguments: argparse.Namespace,
    environ: Mapping[str, str] | None = None,
    repository_root: Path | None = None,
) -> ResolvedEnvironment:
    environment = os.environ if environ is None else environ
    root = (Path(__file__).resolve().parents[2] if repository_root is None else repository_root).resolve()
    sources: dict[str, str] = {}

    output_value, output_source = _select(arguments.artifacts_path, next((value for key, value in environment.items() if key.casefold() == "artifactspath"), None), "artifacts")
    _reject_linked_output(output_value, root, "artifacts path")
    output_directory = _resolve_path(output_value, root, "artifacts path", restrict_relative=True)
    sources["output_directory"] = output_source

    package_value, package_source = _select(arguments.package_dir, environment.get(PACKAGE_ENV), None)
    if package_value is None:
        package_directory = output_directory / "package" / "DigitalStorage"
        package_source = "derived"
    else:
        _reject_linked_output(package_value, root, "package directory")
        package_directory = _resolve_path(package_value, root, "package directory", restrict_relative=True)
    sources["package_directory"] = package_source

    game_requested = (
        arguments.target in {"game", "package", "deploy"}
        or bool(arguments.game_dir)
        or bool(arguments.steam_library_dir)
    )
    game_directory: Path | None = None
    steam_library_directory: Path | None = None
    if game_requested:
        game_value, game_source = _select(arguments.game_dir, environment.get(GAME_ENV), None)
        steam_value, steam_source = _select(arguments.steam_library_dir, environment.get(STEAM_ENV), None)
        game_directory, steam_library_directory, resolved_game_source = _resolve_game(
            game_value, game_source, steam_value, root
        )
        sources["game_directory"] = resolved_game_source
        if steam_library_directory is not None:
            sources["steam_library_directory"] = steam_source if steam_value else "derived"
    else:
        sources["game_directory"] = "unused"
        sources["steam_library_directory"] = "unused"

    deploy_value, deploy_source = _select(arguments.deploy_dir, environment.get(DEPLOY_ENV), None)
    if deploy_value is not None:
        deploy_directory = _resolve_path(deploy_value, root, "deployment directory", restrict_relative=False)
        sources["deploy_directory"] = deploy_source
    elif game_directory is not None:
        deploy_directory = game_directory / "Mods" / "DigitalStorage"
        sources["deploy_directory"] = "derived"
    else:
        deploy_directory = None
        sources["deploy_directory"] = "unset"

    protected_paths = [root / ".reference"]
    configured_game = environment.get(GAME_ENV)
    configured_steam = environment.get(STEAM_ENV)
    if configured_game:
        protected_paths.append(_resolve_path(configured_game, root, "game directory", restrict_relative=False))
    if configured_steam:
        protected_paths.append(_resolve_path(configured_steam, root, "Steam library directory", restrict_relative=False) / "steamapps" / "common" / "Probably Stolen")
    if game_directory is not None:
        protected_paths.append(game_directory)
    for label, candidate in (("output directory", output_directory), ("package directory", package_directory)):
        _reject_protected_path(candidate, protected_paths, label)

    return ResolvedEnvironment(
        repository_root=root,
        game_directory=game_directory,
        steam_library_directory=steam_library_directory,
        output_directory=output_directory,
        package_directory=package_directory,
        deploy_directory=deploy_directory,
        sources=sources,
    )


def _select(cli_value: str | None, environment_value: str | None, default: str | None) -> tuple[str | None, str]:
    if cli_value:
        return cli_value, "cli"
    if environment_value:
        return environment_value, "environment"
    if default is not None:
        return default, "default"
    return None, "unset"


def _reject_linked_output(value: str, root: Path, label: str) -> None:
    expanded = Path(os.path.expandvars(value)).expanduser()
    candidate = Path(os.path.abspath(str(expanded if expanded.is_absolute() else root / expanded)))
    _reject_protected_path_lexical(candidate, root / '.reference', label)
    for path in (candidate, *candidate.parents):
        try:
            status = path.lstat()
        except FileNotFoundError:
            continue
        if path.is_symlink() or getattr(status, 'st_file_attributes', 0) & 0x400:
            raise EnvironmentResolutionError(f"The {label} cannot contain a link or reparse point: {path}")


def _reject_protected_path_lexical(candidate: Path, protected: Path, label: str) -> None:
    candidate_text = os.path.normcase(os.path.abspath(str(candidate)))
    protected_text = os.path.normcase(os.path.abspath(str(protected)))
    if candidate_text == protected_text or candidate_text.startswith(protected_text.rstrip(os.sep) + os.sep):
        raise EnvironmentResolutionError(f"The {label} cannot be inside a protected directory: {candidate}")


def _resolve_path(value: str, root: Path, label: str, restrict_relative: bool) -> Path:
    expanded = os.path.expandvars(value)
    candidate = Path(expanded).expanduser()
    was_relative = not candidate.is_absolute()
    resolved = (root / candidate if was_relative else candidate).resolve(strict=False)
    if was_relative and restrict_relative and not resolved.is_relative_to(root):
        raise EnvironmentResolutionError(f"The relative {label} must remain inside the repository: {value}")
    return resolved


def _resolve_game(
    game_value: str | None,
    game_source: str,
    steam_value: str | None,
    root: Path,
) -> tuple[Path, Path | None, str]:
    game_directory = _resolve_path(game_value, root, "game directory", restrict_relative=False) if game_value else None
    steam_library = _resolve_path(steam_value, root, "Steam library directory", restrict_relative=False) if steam_value else None

    derived_game = None
    if steam_library is not None:
        if not steam_library.is_dir():
            raise EnvironmentResolutionError(f"Steam library directory does not exist: {steam_library}")
        derived_game = steam_library / "steamapps" / "common" / "Probably Stolen"

    if game_directory is None and derived_game is None:
        raise EnvironmentResolutionError(
            f"A game build requires {GAME_ENV} or {STEAM_ENV}, or pass --game-dir or --steam-library-dir."
        )
    if game_directory is not None and derived_game is not None and _normalized(game_directory) != _normalized(derived_game):
        raise EnvironmentResolutionError(
            f"The game directory conflicts with the Steam library derived path: {game_directory} != {derived_game}"
        )

    resolved_game = game_directory if game_directory is not None else derived_game
    if resolved_game is None:
        raise EnvironmentResolutionError("The game installation path could not be resolved.")
    resolved_game = resolved_game.resolve(strict=False)
    if not resolved_game.is_dir():
        raise EnvironmentResolutionError(f"Probably Stolen installation directory does not exist: {resolved_game}")

    missing = [str(resolved_game / relative) for relative in REQUIRED_GAME_FILES if not (resolved_game / relative).is_file()]
    if missing:
        raise EnvironmentResolutionError("Required game files are missing: " + ", ".join(missing))

    resolved_game_source = game_source if game_directory is not None else "derived"
    return resolved_game, steam_library, resolved_game_source


def _normalized(path: Path) -> str:
    return os.path.normcase(str(path.resolve(strict=False)))


def _reject_protected_path(candidate: Path, protected_paths: Sequence[Path], label: str) -> None:
    normalized_candidate = _normalized(candidate)
    for protected in protected_paths:
        normalized_protected = _normalized(protected)
        if normalized_candidate == normalized_protected or normalized_candidate.startswith(
            normalized_protected.rstrip(os.sep) + os.sep
        ):
            raise EnvironmentResolutionError(f"The {label} cannot be inside a protected directory: {candidate}")


def main(argv: Sequence[str] | None = None) -> int:
    arguments = parse_arguments(argv)
    try:
        result = resolve_environment(arguments)
    except EnvironmentResolutionError as error:
        print(f"Environment error: {error}", file=sys.stderr)
        return 2

    if arguments.json:
        print(json.dumps(result.to_dict(), indent=2, ensure_ascii=False))
    else:
        for key, value in result.to_dict().items():
            print(f"{key}={value}")
    return 0


