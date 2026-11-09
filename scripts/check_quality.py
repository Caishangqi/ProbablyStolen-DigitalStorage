#!/usr/bin/env python3
"""Run a read-only, project-neutral source quality audit."""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path


SOURCE_SUFFIXES = {
    ".c", ".cc", ".cpp", ".cs", ".csproj", ".css", ".gradle", ".h", ".hpp",
    ".java", ".js", ".json", ".kt", ".mjs", ".py", ".ps1", ".props",
    ".sh", ".targets", ".toml", ".ts", ".tsx", ".xml", ".yaml", ".yml",
}
# Used only without Git; with Git, .gitignore decides what belongs to the project.
EXCLUDED_PARTS = {
    ".git", ".reference", ".idea", ".vscode",
    "node_modules", "artifacts", "bin", "obj", "dist", "coverage", "TestResults", "__pycache__", ".venv",
    ".pytest_cache", ".ruff_cache",
}
MERGE_MARKERS = ("<<<<<<< ", ">>>>>>> ")


def repository_root() -> Path:
    root = Path(__file__).resolve().parents[1]
    if not (root / "mise.toml").exists():
        raise RuntimeError("Could not locate the project root from this script path.")
    return root


def run(root: Path, *args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args], cwd=root, text=True, capture_output=True, encoding="utf-8", errors="replace", check=False
    )


def has_git(root: Path) -> bool:
    return run(root, "rev-parse", "--show-toplevel").returncode == 0


def changed_paths(root: Path) -> set[str]:
    paths: set[str] = set()
    for args in (
        ("diff", "--name-only"),
        ("diff", "--cached", "--name-only"),
        ("ls-files", "--others", "--exclude-standard"),
    ):
        paths |= git_listed_paths(root, *args)
    return paths


def git_listed_paths(root: Path, *args: str) -> set[str]:
    result = run(root, *args)
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or "git could not list project paths")
    return {line.strip().replace("\\", "/") for line in result.stdout.splitlines() if line.strip()}


def all_source_paths(root: Path) -> set[str]:
    if has_git(root):
        # Tracked files plus untracked files that .gitignore admits; everything else is not project source.
        listed = git_listed_paths(root, "ls-files", "--cached", "--others", "--exclude-standard")
        return {
            relative for relative in listed
            if is_auditable(relative) and (root / relative).is_file() and not (root / relative).is_symlink()
        }
    return walked_source_paths(root)


def walked_source_paths(root: Path) -> set[str]:
    paths: set[str] = set()

    def fail(error: OSError) -> None:
        raise RuntimeError(f"Could not enumerate source directory: {error}") from error

    for directory, children, files in os.walk(root, followlinks=False, onerror=fail):
        parent = Path(directory)
        children[:] = [
            name for name in children
            if name not in EXCLUDED_PARTS
            and not (parent / name).is_symlink()
            and not (parent / name).is_junction()
        ]
        for name in files:
            path = parent / name
            relative = path.relative_to(root).as_posix()
            if is_auditable(relative) and not any(part in EXCLUDED_PARTS for part in Path(relative).parts) \
                    and not path.is_symlink():
                paths.add(relative)
    return paths


def is_auditable(relative: str) -> bool:
    return Path(relative).suffix.lower() in SOURCE_SUFFIXES


def audit_file(root: Path, relative: str) -> list[str]:
    path = root / relative
    findings: list[str] = []
    try:
        text = path.read_text(encoding="utf-8")
    except UnicodeDecodeError as error:
        return [f"[BLOCKER] {relative}: not valid UTF-8 at byte {error.start}"]
    except OSError as error:
        return [f"[WARNING] {relative}: could not read file: {error}"]

    for number, line in enumerate(text.splitlines(), start=1):
        if line.rstrip(" \t") != line:
            findings.append(f"[WARNING] {relative}:{number}: trailing whitespace")
        if line.lstrip().startswith(MERGE_MARKERS) or line.strip() == "=======" :
            findings.append(f"[BLOCKER] {relative}:{number}: possible unresolved merge marker")
    return findings


def check_git_whitespace(root: Path) -> list[str]:
    findings: list[str] = []
    for args, label in ((("diff", "--check"), "working tree"), (("diff", "--cached", "--check"), "index")):
        result = run(root, *args)
        if result.returncode != 0:
            detail = (result.stdout + result.stderr).strip() or f"git exited with {result.returncode}"
            findings.append(f"[BLOCKER] {label} diff check failed: {detail.splitlines()[0]}")
    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scope", choices=("changed", "all"), default="changed")
    args = parser.parse_args()

    try:
        root = repository_root()
        if args.scope == "changed":
            if not has_git(root):
                print("[INFO] No Git repository is initialized; auditing all source files instead.")
                paths = all_source_paths(root)
            else:
                paths = {path for path in changed_paths(root) if is_auditable(path) and (root / path).is_file()}
        else:
            paths = all_source_paths(root)
    except RuntimeError as error:
        print(f"[BLOCKER] {error}", file=sys.stderr)
        return 2

    findings = [finding for relative in sorted(paths) for finding in audit_file(root, relative)]
    if has_git(root):
        findings.extend(check_git_whitespace(root))

    print(f"Project: {root}")
    print(f"Scope: {args.scope}; audited source files: {len(paths)}")
    if findings:
        print("\nFindings:")
        print("\n".join(findings))
    else:
        print("\n[PASS] Read-only source audit passed.")
    return 1 if any("[BLOCKER]" in finding for finding in findings) else 0


if __name__ == "__main__":
    raise SystemExit(main())
