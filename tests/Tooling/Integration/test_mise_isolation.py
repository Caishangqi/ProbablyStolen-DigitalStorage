import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]


class MiseIsolationTests(unittest.TestCase):
    def test_missing_tool_does_not_install_during_exec(self):
        mise = shutil.which("mise")
        self.assertIsNotNone(mise)
        with tempfile.TemporaryDirectory(prefix="mise isolation ") as directory:
            root = Path(directory)
            env = os.environ.copy()
            for key in tuple(env):
                if key.startswith("MISE_"):
                    env.pop(key)
            for name in ("DATA", "CACHE"):
                env[f"MISE_{name}_DIR"] = str(root / name.lower())
            env["MISE_GLOBAL_CONFIG_FILE"] = str(root / "global.toml")
            env["MISE_SYSTEM_CONFIG_FILE"] = str(root / "system.toml")
            env["MISE_YES"] = "0"
            env["MISE_ENV"] = "isolated-test"
            env["MISE_CONFIG_DIR"] = str(root / "config")
            env["MISE_DATA_DIR"] = str(root / "data")
            env["MISE_CACHE_DIR"] = str(root / "cache")
            env["MISE_STATE_DIR"] = str(root / "state")
            env["MISE_CONFIG_FILE"] = str(root / "mise.toml")
            (root / "mise.toml").write_text((ROOT / 'mise.toml').read_text(encoding='utf-8'), encoding="utf-8")
            env["PATH"] = str(root)
            result = subprocess.run([mise, "exec", "--", "uv", "--version"], cwd=root, env=env,
                                    capture_output=True, text=True, errors="replace", timeout=30)
            self.assertNotEqual(result.returncode, 0)
            installs = root / "data" / "installs"
            self.assertFalse(installs.exists() and any(installs.rglob("uv.exe")))
