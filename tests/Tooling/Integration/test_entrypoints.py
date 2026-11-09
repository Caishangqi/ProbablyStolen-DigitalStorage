import subprocess
import sys
import tempfile
import tomllib
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

from digital_storage_tools.commands import PUBLIC, SCRIPTS, ROOT


class EntrypointTests(unittest.TestCase):
    def test_mise_launcher_handles_repository_path_with_spaces(self):
        with tempfile.TemporaryDirectory(prefix='repo with spaces ') as directory:
            root = Path(directory)
            subprocess.run([sys.executable, '-m', 'venv', str(root / '.venv')], check=True, capture_output=True)
            (root / 'mise.toml').write_text('''[settings]\ntask.run_auto_install = false\n[tasks.probe]\ndir = '{{config_root}}'\nrun = '"{{config_root}}/.venv/Scripts/python.exe" -c "print(1234567)"'\n''', encoding='utf-8')
            result = subprocess.run(['mise', 'run', 'probe'], cwd=root, capture_output=True, text=True, check=True)
            self.assertIn('1234567', result.stdout)

    def test_mise_and_rider_map_to_public_commands(self):
        tasks = tomllib.loads((ROOT / 'mise.toml').read_text(encoding='utf-8'))['tasks']
        self.assertEqual(set(tasks), set(PUBLIC) | {'setup'})
        for name in PUBLIC:
            self.assertIn('scripts/tool.py ' + name, tasks[name]['run'])
        for spec in SCRIPTS.values():
            self.assertTrue((ROOT / 'scripts' / spec[0]).is_file())
        configs = list((ROOT / '.run').glob('*.run.xml'))
        self.assertGreater(len(configs), 0)
        for path in configs:
            cfg = ET.parse(path).getroot().find('configuration')
            self.assertEqual((cfg.get('type'), cfg.get('factoryName')), ('RunNativeExe', 'Native Executable'))
            options = {x.get('name'): x.get('value') for x in cfg.findall('option')}
            self.assertEqual(options['EXE_PATH'], '$PROJECT_DIR$/.venv/Scripts/python.exe')
            self.assertEqual(options['WORKING_DIRECTORY'], '$PROJECT_DIR$')
            self.assertEqual(options['PASS_PARENT_ENVS'], '1')
            self.assertIn(options['PROGRAM_PARAMETERS'].split()[-1], PUBLIC)
            self.assertIsNone(cfg.find('method'))
