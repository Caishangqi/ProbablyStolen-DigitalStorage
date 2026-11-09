"""Export the Unity project through the local AssetRipper web interface."""

from __future__ import annotations

import argparse
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

GAME_ENV = "DIGITAL_STORAGE_GAME_DIR"
RIPPER_ENV = "DIGITAL_STORAGE_ASSET_RIPPER_EXE"


class WorkflowError(RuntimeError):
    pass


def _is_link(path: Path) -> bool:
    is_junction = getattr(os.path, "isjunction", lambda _: False)
    return path.is_symlink() or is_junction(path)


def _is_reparse_point(path: Path) -> bool:
    try:
        status = path.lstat()
    except FileNotFoundError:
        return False
    return _is_link(path) or bool(getattr(status, "st_file_attributes", 0) & 0x400)


def _reject_reparse_components(path: Path, root: Path) -> None:
    try:
        relative = path.absolute().relative_to(root.absolute())
    except ValueError as error:
        raise WorkflowError(f"Path is outside the repository: {path}") from error
    cursor = root
    for part in relative.parts:
        cursor = cursor / part
        if _is_reparse_point(cursor):
            raise WorkflowError(f"Refusing a linked or reparse path component: {cursor}")


def _assert_plain_path(path: Path, repo_root: Path, label: str) -> Path:
    expected = (repo_root / ".reference" / "probably-stolen-content").resolve(strict=False)
    _reject_reparse_components(path, repo_root)
    resolved = path.resolve(strict=False)
    if os.path.normcase(str(resolved)) != os.path.normcase(str(expected)):
        raise WorkflowError(f"Refusing {label} outside the approved content archive: {resolved}")
    if path.exists() and not path.is_dir():
        raise WorkflowError(f"{label} exists but is not a directory: {path}")
    return expected


def _reject_reparse_descendants(path: Path) -> None:
    pending = [path]
    while pending:
        current = pending.pop()
        for child in current.iterdir():
            if _is_reparse_point(child):
                raise WorkflowError(f"Refusing to clear output containing linked or reparse entry: {child}")
            if child.is_dir():
                pending.append(child)


def _clear_contents(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)
    _reject_reparse_descendants(path)
    for child in path.iterdir():
        if child.is_dir():
            shutil.rmtree(child)
        else:
            child.unlink()


def _reserve_port() -> int:
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


def _request(url: str, fields: dict[str, str] | None = None, timeout: int = 15) -> tuple[int, str | None, str]:
    data = urllib.parse.urlencode(fields).encode("utf-8") if fields is not None else None
    request = urllib.request.Request(url, data=data, headers={"Content-Type": "application/x-www-form-urlencoded"})
    opener = urllib.request.build_opener(_NoRedirectHandler())
    try:
        with opener.open(request, timeout=timeout) as response:
            return response.status, response.headers.get("Location"), response.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as error:
        if 300 <= error.code < 400:
            return error.code, error.headers.get("Location"), error.read().decode("utf-8", errors="replace")
        raise


class _NoRedirectHandler(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, new):
        return None


def _require_redirect(response: tuple[int, str | None, str], route: str) -> None:
    status, location, _ = response
    if status not in {301, 302, 303, 307, 308} or location != "/":
        raise WorkflowError(f"AssetRipper {route} request failed with HTTP {status} and Location {location!r}")


def _validate_export(staging_dir: Path, log_path: Path) -> Path:
    project_dir = staging_dir / "ExportedProject"
    if not project_dir.is_dir():
        project_dir = staging_dir
    required = (project_dir / "Assets", project_dir / "ProjectSettings", project_dir / "Packages")
    if not all(path.is_dir() for path in required):
        missing = ", ".join(str(path.relative_to(project_dir)) for path in required if not path.is_dir())
        raise WorkflowError(f"AssetRipper did not produce a complete Unity project; missing {missing}")
    if not log_path.is_file():
        raise WorkflowError("AssetRipper did not produce an operation log")
    log_text = log_path.read_text(encoding="utf-8", errors="replace")
    if "Finished reading files" not in log_text:
        raise WorkflowError("AssetRipper did not finish loading the game data")
    if "Finished post-export" not in log_text:
        raise WorkflowError("AssetRipper did not finish exporting the Unity project")
    if "ErrorHandlingMiddleware" in log_text or "Unhandled exception" in log_text:
        raise WorkflowError("AssetRipper reported an export exception; inspect the temporary AssetRipper log")
    return project_dir


def export_assets(game_dir: Path, ripper_exe: Path, output_dir: Path) -> None:
    repo_root = Path(__file__).resolve().parents[2]
    expected_output = _assert_plain_path(output_dir, repo_root, "content output")
    game_dir = game_dir.resolve(strict=True)
    data_dir = game_dir / "Probably Stolen_Data"
    if not data_dir.is_dir() or _is_reparse_point(data_dir):
        raise WorkflowError(f"Game asset directory is missing or linked: {data_dir}")
    ripper_exe = ripper_exe.resolve(strict=True)
    if not ripper_exe.is_file() or _is_reparse_point(ripper_exe):

        raise WorkflowError(f"AssetRipper executable is missing or linked: {ripper_exe}")

    _clear_contents(expected_output)
    staging_dir = Path(tempfile.mkdtemp(prefix=".probably-stolen-content-", dir=expected_output.parent))
    log_path = staging_dir.parent / f"{staging_dir.name}.log"
    port = _reserve_port()
    process = subprocess.Popen(
        [str(ripper_exe), "--headless", "--port", str(port), "--log:true", "--log-path", str(log_path)],
        cwd=str(ripper_exe.parent),
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    base_url = f"http://127.0.0.1:{port}"
    try:
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise WorkflowError(f"AssetRipper exited during startup with code {process.returncode}")
            try:
                status, _, _ = _request(base_url, timeout=2)
                if status < 500:
                    break
            except (OSError, urllib.error.URLError):
                time.sleep(0.25)
        else:
            raise WorkflowError("AssetRipper local web service did not become ready within 45 seconds")

        _require_redirect(_request(f"{base_url}/LoadFolder", {"Path": str(data_dir)}, timeout=600), "LoadFolder")
        _require_redirect(
            _request(
                f"{base_url}/Export/UnityProject",
                {"Path": str(staging_dir), "CreateSubfolder": "false"},
                timeout=3600,
            ),
            "Export/UnityProject",
        )
        project_dir = _validate_export(staging_dir, log_path)
        for child in project_dir.iterdir():
            child.rename(expected_output / child.name)
    finally:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=10)
        shutil.rmtree(staging_dir, ignore_errors=True)
        log_path.unlink(missing_ok=True)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-dir", default=os.environ.get(GAME_ENV))
    parser.add_argument("--assetripper", default=os.environ.get(RIPPER_ENV))
    parser.add_argument("--output-dir", default=str(Path(__file__).resolve().parents[2] / ".reference" / "probably-stolen-content"))
    args = parser.parse_args(argv)
    if not args.game_dir:
        parser.error(f"Set {GAME_ENV} in mise.local.toml or pass --game-dir")
    if not args.assetripper:
        parser.error(f"Set {RIPPER_ENV} to the AssetRipper executable in mise.local.toml or pass --assetripper")
    try:
        export_assets(Path(args.game_dir), Path(args.assetripper), Path(args.output_dir))
        print(f"Asset export completed: {Path(args.output_dir).resolve()}")
        return 0
    except (OSError, ValueError, WorkflowError, subprocess.SubprocessError, urllib.error.URLError) as error:
        print(f"Asset export error: {error}", file=sys.stderr)
        return 2


