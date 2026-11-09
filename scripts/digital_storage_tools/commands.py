"""Shared developer command dispatch."""
from __future__ import annotations

import os
import subprocess
import sys
import tomllib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LOCAL_ENV = {"DIGITAL_STORAGE_GAME_DIR", "DIGITAL_STORAGE_STEAM_LIBRARY_DIR", "DIGITAL_STORAGE_ILSPYCMD", "DIGITAL_STORAGE_ASSET_RIPPER_EXE", "DIGITAL_STORAGE_PACKAGE_DIR", "DIGITAL_STORAGE_DEPLOY_DIR", "DIGITAL_STORAGE_STEAMCMD_DIR", "DIGITAL_STORAGE_STEAM_USERNAME", "DIGITAL_STORAGE_WORKSHOP_VDF", "DIGITAL_STORAGE_WORKSHOP_PREVIEW", "DIGITAL_STORAGE_WORKSHOP_VISIBILITY", "DIGITAL_STORAGE_WORKSHOP_CHANGE_NOTE", "DIGITAL_STORAGE_WORKSHOP_LOCALE", "ArtifactsPath"}
SCRIPTS = {
    "build-core": ("build.ps1", "-Target", "Core"), "build-game": ("build.ps1", "-Target", "Game"),
    **{f"test-{name.lower()}": ("test.ps1", "-Target", name) for name in ("Core", "Platform", "Common", "Client", "Integration", "Architecture", "All")},
    "package": ("package.ps1",), "deploy": ("deploy.ps1", "-Action", "Deploy"),
    "remove": ("deploy.ps1", "-Action", "Remove"), "launch": ("start-game.ps1",),
    "launch-plan": ("start-game.ps1", "-BuildAndDeploy", "-Plan"),
    "build-deploy-launch": ("start-game.ps1", "-BuildAndDeploy"),
    "publish-workshop": ("publish-workshop.ps1",),
}
CHECKS = {"check-source": ("scripts/check_quality.py", "--scope", "all")}
PUBLIC = tuple(SCRIPTS) + tuple(CHECKS) + ("doctor", "check-assets", "check-ci", "check-format", "ci-core", "test-tools-unit", "test-tools-integration", "verify-package", "decompile", "extract-assets", "prepare-assets")


def local_environment() -> dict[str, str]:
    env = os.environ.copy()
    path = ROOT / "mise.local.toml"
    if path.exists():
        with path.open("rb") as stream:
            values = tomllib.load(stream).get("env", {})
        if not isinstance(values, dict):
            raise ValueError("mise.local.toml [env] must be a table")
        for key in LOCAL_ENV:
            if key in values:
                if not isinstance(values[key], str):
                    raise ValueError(f"{key} must be a plain string")
                env.setdefault(key, values[key])
    return env


def run(command: str, args: list[str]) -> int:
    env = local_environment()
    if command in SCRIPTS:
        spec = SCRIPTS[command]
        argv = ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(ROOT / "scripts" / spec[0]), *spec[1:], *args]
    elif command in CHECKS:
        argv = [sys.executable, "-B", *(str(ROOT / part) if i == 0 else part for i, part in enumerate(CHECKS[command])), *args]
    elif command == "check-ci":
        if args:
            raise ValueError("check-ci takes no arguments")
        argv = ["mise", "exec", "--", "actionlint"]
    elif command == "check-format":
        if args:
            raise ValueError("check-format takes no arguments")
        from .environment import parse_arguments, resolve_environment
        resolved = resolve_environment(parse_arguments([]), environ=env, repository_root=ROOT)
        escaped = str(resolved.output_directory).replace("%", "%25").replace(";", "%3B").replace(",", "%2C")
        artifacts = f"-p:ArtifactsPath={escaped}"
        env = {key: value for key, value in env.items() if key.casefold() != "artifactspath"}
        env["ArtifactsPath"] = str(resolved.output_directory)
        for argv in (["dotnet", "restore", "DigitalStorage.Core.slnf", "--locked-mode", artifacts],
                     ["dotnet", "format", "DigitalStorage.Core.slnf", "--no-restore", "--verify-no-changes"]):
            result = subprocess.run(argv, cwd=ROOT, env=env)
            if result.returncode:
                return result.returncode
        return 0
    elif command == "doctor":
        if args:
            raise ValueError("doctor takes no arguments")
        result = subprocess.run(["dotnet", "--version"], cwd=ROOT, env=env, capture_output=True, text=True)
        if result.returncode or result.stdout.strip() != "10.0.301":
            raise ValueError(".NET SDK 10.0.301 is required")
        print("Python 3.13.7, Pillow 12.0.0, locked uv environment and .NET SDK 10.0.301 verified.")
        return 0
    elif command == "ci-core":
        if args:
            raise ValueError("ci-core takes no arguments")
        for name in ("check-source", "check-assets", "check-ci", "check-format", "test-tools-unit", "test-tools-integration", "test-core"):
            result = subprocess.run([sys.executable, "-B", str(ROOT / "scripts/tool.py"), name], cwd=ROOT, env=env)
            if result.returncode:
                return result.returncode
        return 0
    else:
        raise ValueError(f"Unknown command: {command}")
    return subprocess.run(argv, cwd=ROOT, env=env).returncode
