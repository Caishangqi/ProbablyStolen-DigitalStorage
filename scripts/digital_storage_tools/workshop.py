"""Prepare and publish a validated package to the Probably Stolen Workshop."""

from __future__ import annotations

import argparse
import ctypes
import os
import re
import stat
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Mapping, Sequence

from .package import PackageValidationError, validate_package_directory

ROOT = Path(__file__).resolve().parents[2]
WORKSHOP_APP_ID = "4348910"
DEFAULT_VDF_PATH = Path(".secrets") / "workshop.vdf"
MAX_PREVIEW_BYTES = 1_048_576
_VALID_ID = re.compile(r"(?:0|[1-9][0-9]*)\Z")
_VALID_VISIBILITY = {"0", "1", "2"}
_ENV_STEAMCMD_DIR = "DIGITAL_STORAGE_STEAMCMD_DIR"
_ENV_STEAM_USERNAME = "DIGITAL_STORAGE_STEAM_USERNAME"
_ENV_WORKSHOP_VDF = "DIGITAL_STORAGE_WORKSHOP_VDF"
_ENV_WORKSHOP_PREVIEW = "DIGITAL_STORAGE_WORKSHOP_PREVIEW"
_ENV_WORKSHOP_VISIBILITY = "DIGITAL_STORAGE_WORKSHOP_VISIBILITY"
_ENV_WORKSHOP_CHANGE_NOTE = "DIGITAL_STORAGE_WORKSHOP_CHANGE_NOTE"
_ENV_WORKSHOP_LOCALE = "DIGITAL_STORAGE_WORKSHOP_LOCALE"
REQUIRED_RELEASE_VERSION = (0, 3, 0)
WORKSHOP_LOCALE_PATHS = {
    "en-US": ROOT / "workshop" / "i18n" / "en-US.txt",
    "zh-CN": ROOT / "workshop" / "i18n" / "zh-CN.txt",
}


class WorkshopValidationError(ValueError):
    """Raised when Workshop input or local publishing state is invalid."""


@dataclass(frozen=True)
class WorkshopSettings:
    package_directory: Path
    steamcmd_directory: Path
    vdf_path: Path
    preview_path: Path
    steam_username: str
    visibility: str = "0"
    change_note: str | None = None
    locale: str = "en-US"


@dataclass(frozen=True)
class WorkshopMetadata:
    version: str
    title: str
    description: str


@dataclass(frozen=True)
class PreparedWorkshopItem:
    published_file_id: str
    metadata: WorkshopMetadata
    vdf_path: Path


def _plain_path(path: Path, label: str) -> Path:
    candidate = Path(os.path.abspath(str(path)))
    for current in (candidate, *candidate.parents):
        try:
            status = current.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(status.st_mode) or getattr(status, "st_file_attributes", 0) & 0x400:
            raise WorkshopValidationError(f"{label} must not contain links or reparse points: {current}")
    return candidate


def _resolve_path(value: str | Path, label: str) -> Path:
    expanded = Path(os.path.expandvars(str(value))).expanduser()
    return _plain_path(expanded, label)


def _required_value(value: str | None, name: str) -> str:
    if value is None or not value.strip():
        raise WorkshopValidationError(f"{name} must be configured before publishing.")
    return value.strip()


def _configured_value(
    argument: str | None, environment: Mapping[str, str], name: str, default: str | None = None
) -> str | None:
    if argument is not None and argument.strip():
        return argument
    value = environment.get(name)
    if value is not None and value.strip():
        return value
    return default


def settings_from_arguments(
    arguments: argparse.Namespace,
    environ: Mapping[str, str] | None = None,
    repository_root: Path = ROOT,
) -> WorkshopSettings:
    environment = os.environ if environ is None else environ
    package_value = _required_value(arguments.package_dir, "Package directory")
    steamcmd_value = _required_value(
        _configured_value(arguments.steamcmd_dir, environment, _ENV_STEAMCMD_DIR),
        _ENV_STEAMCMD_DIR,
    )
    preview_value = _required_value(
        _configured_value(arguments.preview, environment, _ENV_WORKSHOP_PREVIEW),
        _ENV_WORKSHOP_PREVIEW,
    )
    username = _required_value(
        _configured_value(arguments.steam_username, environment, _ENV_STEAM_USERNAME),
        _ENV_STEAM_USERNAME,
    )
    vdf_value = _configured_value(
        arguments.vdf_path,
        environment,
        _ENV_WORKSHOP_VDF,
        str(Path(".secrets") / "workshop.vdf"),
    )
    visibility = _configured_value(
        arguments.visibility,
        environment,
        _ENV_WORKSHOP_VISIBILITY,
        "0",
    )
    if visibility not in _VALID_VISIBILITY:
        raise WorkshopValidationError("Workshop visibility must be 0, 1, or 2.")
    change_note = _configured_value(arguments.change_note, environment, _ENV_WORKSHOP_CHANGE_NOTE)
    locale = _configured_value(getattr(arguments, "locale", None), environment, _ENV_WORKSHOP_LOCALE, "en-US")
    if locale not in WORKSHOP_LOCALE_PATHS:
        raise WorkshopValidationError("Workshop locale must be en-US or zh-CN.")
    root = repository_root.resolve()
    return WorkshopSettings(
        package_directory=_resolve_path(
            package_value if Path(package_value).is_absolute() else root / package_value,
            "Package directory",
        ),
        steamcmd_directory=_resolve_path(
            steamcmd_value if Path(steamcmd_value).is_absolute() else root / steamcmd_value,
            "SteamCMD directory",
        ),
        vdf_path=_resolve_path(
            vdf_value if Path(vdf_value).is_absolute() else root / vdf_value,
            "Workshop VDF",
        ),
        preview_path=_resolve_path(
            preview_value if Path(preview_value).is_absolute() else root / preview_value,
            "Workshop preview",
        ),
        steam_username=username,
        visibility=visibility,
        change_note=change_note.strip() if change_note and change_note.strip() else None,
        locale=locale,
    )


def _localized_description(locale: str) -> str:
    descriptions: dict[str, str] = {}
    for name, path in WORKSHOP_LOCALE_PATHS.items():
        try:
            description = path.read_text(encoding="utf-8").strip()
        except (OSError, UnicodeError) as error:
            raise WorkshopValidationError(f"Could not read Workshop description for {name}: {path}") from error
        if not description:
            raise WorkshopValidationError(f"Workshop description for {name} must not be empty.")
        descriptions[name] = description
    return descriptions[locale]


def _canonical_existing_identity(path: Path) -> str:
    candidate = Path(os.path.abspath(str(path)))
    unresolved: list[str] = []
    existing = candidate
    while not existing.exists():
        unresolved.append(existing.name)
        if existing.parent == existing:
            return os.path.normcase(os.path.realpath(candidate))
        existing = existing.parent
    if os.name != "nt":
        return os.path.normcase(os.path.join(os.path.realpath(existing), *reversed(unresolved)))

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    create_file = kernel32.CreateFileW
    create_file.argtypes = (ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p,
                            ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p)
    create_file.restype = ctypes.c_void_p
    get_final_path = kernel32.GetFinalPathNameByHandleW
    get_final_path.argtypes = (ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32)
    get_final_path.restype = ctypes.c_uint32
    invalid_handle = ctypes.c_void_p(-1).value
    handle = create_file(str(existing), 0, 0x1 | 0x2 | 0x4, None, 3, 0x02000000, None)
    if handle == invalid_handle:
        return os.path.normcase(os.path.join(os.path.realpath(existing), *reversed(unresolved)))
    try:
        buffer = ctypes.create_unicode_buffer(32768)
        length = get_final_path(handle, buffer, len(buffer), 0)
        if not length or length >= len(buffer):
            return os.path.normcase(os.path.join(os.path.realpath(existing), *reversed(unresolved)))
        value = buffer.value
        if value.startswith("\\\\?\\UNC\\"):
            value = "\\\\" + value[8:]
        elif value.startswith("\\\\?\\"):
            value = value[4:]
        return os.path.normcase(os.path.join(value, *reversed(unresolved)))
    finally:
        kernel32.CloseHandle(handle)


def _validate_output_paths(settings: WorkshopSettings, pending: Path) -> None:
    package = _canonical_existing_identity(settings.package_directory)
    for path, label in ((settings.vdf_path, "Workshop VDF"), (pending, "Workshop pending marker")):
        candidate = _canonical_existing_identity(path)
        try:
            inside = os.path.commonpath((candidate, package)) == package
        except ValueError:
            inside = False
        if inside:
            raise WorkshopValidationError(f"{label} must be outside the package contentfolder: {candidate}")


def _manifest_metadata(package_directory: Path, locale: str = "en-US") -> WorkshopMetadata:
    path = package_directory / "manifest.xml"
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as error:
        raise WorkshopValidationError(f"Could not read package manifest: {error}") from error
    if root.tag != "Manifest":
        raise WorkshopValidationError("Package manifest must have a Manifest root element.")
    fields = {element.tag: (element.text or "").strip() for element in root}
    for name in ("ModVersion", "Name", "Description"):
        if not fields.get(name):
            raise WorkshopValidationError(f"Package manifest field {name} must not be empty.")
    version_text = fields["ModVersion"]
    match = re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version_text)
    if match is None or tuple(map(int, match.groups())) < REQUIRED_RELEASE_VERSION:
        minimum = ".".join(map(str, REQUIRED_RELEASE_VERSION))
        raise WorkshopValidationError(f"Package manifest version must be at least {minimum} before publishing.")
    return WorkshopMetadata(version_text, fields["Name"], _localized_description(locale))


def _validate_preview(path: Path) -> None:
    if not path.is_file():
        raise WorkshopValidationError(f"Workshop preview does not exist: {path}")
    if path.stat().st_size == 0 or path.stat().st_size > MAX_PREVIEW_BYTES:
        raise WorkshopValidationError("Workshop preview must be non-empty and no larger than 1 MiB.")
    if path.suffix.casefold() not in {".png", ".jpg", ".jpeg"}:
        raise WorkshopValidationError("Workshop preview must be a PNG or JPEG file.")
    try:
        from PIL import Image

        with Image.open(path) as image:
            image.verify()
    except ImportError as error:
        raise WorkshopValidationError("Preview validation requires Pillow. Run mise run setup.") from error
    except (OSError, ValueError) as error:
        raise WorkshopValidationError(f"Workshop preview is not a valid image: {path}") from error


def _vdf_tokens(text: str) -> list[str]:
    tokens: list[str] = []
    index = 0
    while index < len(text):
        if text[index].isspace():
            index += 1
            continue
        if text.startswith("//", index):
            newline = text.find("\n", index + 2)
            index = len(text) if newline < 0 else newline + 1
            continue
        if text[index] in "{}":
            tokens.append(text[index])
            index += 1
            continue
        if text[index] == '"':
            index += 1
            value: list[str] = []
            while index < len(text):
                character = text[index]
                index += 1
                if character == '"':
                    break
                if character == "\\":
                    if index >= len(text):
                        raise WorkshopValidationError("Workshop VDF contains an incomplete escape.")
                    escaped = text[index]
                    index += 1
                    value.append({"n": "\n", "r": "\r", "t": "\t"}.get(escaped, escaped))
                else:
                    value.append(character)
            else:
                raise WorkshopValidationError("Workshop VDF contains an unterminated quoted value.")
            tokens.append("".join(value))
            continue
        start = index
        while index < len(text) and not text[index].isspace() and text[index] not in "{}":
            index += 1
        tokens.append(text[start:index])
    return tokens


def _vdf_object(tokens: Sequence[str], index: int) -> tuple[dict[str, str | dict[str, object]], int]:
    values: dict[str, str | dict[str, object]] = {}
    while index < len(tokens) and tokens[index] != "}":
        key = tokens[index]
        index += 1
        if index >= len(tokens):
            raise WorkshopValidationError(f"Workshop VDF key has no value: {key}")
        if tokens[index] == "{":
            value, index = _vdf_object(tokens, index + 1)
        else:
            value = tokens[index]
            index += 1
        if key in values:
            raise WorkshopValidationError(f"Workshop VDF contains duplicate key: {key}")
        values[key] = value
    if index >= len(tokens) or tokens[index] != "}":
        raise WorkshopValidationError("Workshop VDF contains an unterminated object.")
    return values, index + 1


def _read_vdf(path: Path) -> dict[str, str | dict[str, object]]:
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as error:
        raise WorkshopValidationError(f"Could not read Workshop VDF: {path}") from error
    tokens = _vdf_tokens(text)
    if len(tokens) < 3 or tokens[0] != "workshopitem" or tokens[1] != "{":
        raise WorkshopValidationError("Workshop VDF must contain one workshopitem object.")
    values, index = _vdf_object(tokens, 2)
    if index != len(tokens):
        raise WorkshopValidationError("Workshop VDF contains data after the workshopitem object.")
    return values


def _published_file_id(path: Path) -> str:
    if not path.exists():
        return "0"
    values = _read_vdf(path)
    value = values.get("publishedfileid")
    if not isinstance(value, str) or not _VALID_ID.fullmatch(value):
        raise WorkshopValidationError("Workshop VDF publishedfileid must be a non-negative integer.")
    return value


def _vdf_quote(value: str) -> str:
    escaped = value.replace("\\", "\\\\").replace('"', '\\"')
    # Keep real line breaks; SteamCMD may treat C style newline escapes as literal text.
    escaped = escaped.replace("\r\n", "\n").replace("\r", "\n")
    return f'"{escaped}"'


def render_vdf(
    metadata: WorkshopMetadata,
    settings: WorkshopSettings,
    published_file_id: str,
) -> str:
    change_note = settings.change_note or f"Release {metadata.version}"
    fields = (
        ("appid", WORKSHOP_APP_ID),
        ("publishedfileid", published_file_id),
        ("contentfolder", str(settings.package_directory)),
        ("previewfile", str(settings.preview_path)),
        ("visibility", settings.visibility),
        ("title", metadata.title),
        ("description", metadata.description),
        ("changenote", change_note),
    )
    lines = ['"workshopitem"', "{"]
    lines.extend(f"    {_vdf_quote(key)}    {_vdf_quote(value)}" for key, value in fields)
    lines.append("}")
    return "\n".join(lines) + "\n"


def _write_vdf(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", newline="\n", dir=path.parent, prefix=f".{path.name}.", delete=False
        ) as stream:
            temporary_path = Path(stream.name)
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_path, path)
        temporary_path = None
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def prepare_workshop_item(settings: WorkshopSettings) -> PreparedWorkshopItem:
    pending = _plain_path(Path(str(settings.vdf_path) + ".pending"), "Workshop pending marker")
    _validate_output_paths(settings, pending)
    executable = _plain_path(settings.steamcmd_directory / "steamcmd.exe", "SteamCMD executable")
    if not settings.steamcmd_directory.is_dir() or not executable.is_file():
        raise WorkshopValidationError(f"SteamCMD executable was not found: {executable}")
    if not settings.steam_username.strip():
        raise WorkshopValidationError("Steam username must not be empty.")
    if settings.visibility not in _VALID_VISIBILITY:
        raise WorkshopValidationError("Workshop visibility must be 0, 1, or 2.")
    try:
        validate_package_directory(settings.package_directory, require_current_metadata=True)
    except PackageValidationError as error:
        raise WorkshopValidationError(f"Package validation failed: {error}") from error
    _validate_preview(settings.preview_path)
    metadata = _manifest_metadata(settings.package_directory, settings.locale)
    published_file_id = _published_file_id(settings.vdf_path)
    _write_vdf(settings.vdf_path, render_vdf(metadata, settings, published_file_id))
    return PreparedWorkshopItem(published_file_id, metadata, settings.vdf_path)


def publish_workshop(
    settings: WorkshopSettings,
    runner: Callable[..., subprocess.CompletedProcess[object]] = subprocess.run,
) -> int:
    pending = _plain_path(Path(str(settings.vdf_path) + ".pending"), "Workshop pending marker")
    _validate_output_paths(settings, pending)
    pending.parent.mkdir(parents=True, exist_ok=True)
    marker_owned = False
    try:
        marker = pending.open("x", encoding="utf-8")
        marker_owned = True
        with marker:
            marker.write("publish in progress; inspect the VDF and Steam before clearing this marker\n")
            marker.flush()
            os.fsync(marker.fileno())
    except FileExistsError as error:
        raise WorkshopValidationError(
            f"A previous upload has an uncertain outcome. Inspect {pending} and the VDF before clearing the marker."
        ) from error
    except BaseException:
        if marker_owned:
            pending.unlink(missing_ok=True)
        raise

    try:
        prepared = prepare_workshop_item(settings)
    except (OSError, WorkshopValidationError):
        pending.unlink(missing_ok=True)
        raise
    executable = settings.steamcmd_directory / "steamcmd.exe"
    command = [
        str(executable),
        "+login",
        settings.steam_username,
        "+workshop_build_item",
        str(prepared.vdf_path),
        "+quit",
    ]
    result = runner(command, cwd=str(settings.steamcmd_directory), check=False)
    if result.returncode != 0:
        raise WorkshopValidationError(f"SteamCMD exited with code {result.returncode}.")
    current_published_file_id = _published_file_id(prepared.vdf_path)
    if current_published_file_id == "0" or (
        prepared.published_file_id != "0" and current_published_file_id != prepared.published_file_id
    ):
        raise WorkshopValidationError(
            "SteamCMD returned success but the Workshop item ID is missing or changed; "
            "check Steam and the retained VDF before retrying."
        )
    pending.unlink()
    print(f"[PASS] Workshop item published from {settings.package_directory}.")
    print(f"[INFO] Workshop VDF: {prepared.vdf_path}")
    print(f"[INFO] Published file ID: {current_published_file_id}")
    return result.returncode


def _arguments(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package-dir", required=True)
    parser.add_argument("--steamcmd-dir")
    parser.add_argument("--vdf-path")
    parser.add_argument("--preview")
    parser.add_argument("--steam-username")
    parser.add_argument("--visibility")
    parser.add_argument("--change-note")
    parser.add_argument("--locale", choices=tuple(WORKSHOP_LOCALE_PATHS))
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    try:
        arguments = _arguments(argv)
        settings = settings_from_arguments(arguments)
        publish_workshop(settings)
        return 0
    except (OSError, WorkshopValidationError) as error:
        print(f"Workshop publishing error: {error}", file=sys.stderr)
        return 2
