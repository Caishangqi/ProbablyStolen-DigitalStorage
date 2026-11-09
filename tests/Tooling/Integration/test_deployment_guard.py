import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]


class DeploymentGuardIntegrationTests(unittest.TestCase):
    def test_direct_mutating_entry_rejects_process_and_preserves_tree(self):
        with tempfile.TemporaryDirectory(prefix='guard fixture ') as directory:
            root = Path(directory)
            game = root / 'game'
            for name in ('Probably Stolen.exe', 'Probably Stolen_Data/Managed/Assembly-CSharp.dll',
                         'Probably Stolen_Data/Managed/UnityEngine.dll', 'Probably Stolen_Data/Managed/UnityEngine.CoreModule.dll'):
                path = game / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b'fixture')
            package = root / 'package'
            package.mkdir()
            env = os.environ.copy()
            env['PYTHONPATH'] = str(ROOT / 'scripts')
            code = '''import sys
from unittest.mock import patch
from digital_storage_tools import deployment
with patch.object(deployment, '_running_game_paths', return_value=[sys.argv[3]]):
    sys.exit(deployment.main(['--action','remove','--game-dir',sys.argv[1],'--deploy-dir',sys.argv[2]]))
'''
            for running in (str(game / 'Probably Stolen.exe'), ''):
                # A complete owned package is needed to reach the mutating removal function.
                from digital_storage_tools.package import CURRENT_FILES
                target = game / 'Mods' / 'DigitalStorage'
                for name in CURRENT_FILES:
                    path = target / name
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes((ROOT / 'packaging/manifest.xml').read_bytes() if name == 'manifest.xml' else b'fixture')
                before = {str(p.relative_to(game)): p.read_bytes() for p in game.rglob('*') if p.is_file()}
                result = subprocess.run([sys.executable, '-B', '-c', code, str(game), str(target), running],
                                        env=env, capture_output=True, text=True)
                self.assertEqual(result.returncode, 2, result.stderr)
                self.assertEqual(before, {str(p.relative_to(game)): p.read_bytes() for p in game.rglob('*') if p.is_file()})
