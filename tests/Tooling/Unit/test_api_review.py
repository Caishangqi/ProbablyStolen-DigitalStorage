import json
import shutil
import tempfile
import unittest
from pathlib import Path

from digital_storage_tools import platform_review


class ApiReviewTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        for name in platform_review.CONFIG:
            (self.root / name).parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(platform_review.ROOT / name, self.root / name)
        for name in platform_review.API_PROJECTS:
            shutil.copytree(platform_review.ROOT / name, self.root / name,
                            ignore=shutil.ignore_patterns('bin', 'obj', 'reviewed-inputs.json'))
        self.manifest = self.root / platform_review.API_MANIFEST.relative_to(platform_review.ROOT)
        self.data = {'inputs': platform_review.input_hashes(self.root, platform_review.API_PROJECTS), 'tests': ['Example.Test']}

    def accept_fixture(self):
        self.manifest.write_text(json.dumps(self.data), encoding='utf-8')

    def test_missing_and_wrong_scope_manifest_rejected(self):
        with self.assertRaises(FileNotFoundError):
            platform_review.validate_api(self.root)
        self.data['inputs'] = {}
        self.accept_fixture()
        with self.assertRaises(ValueError):
            platform_review.validate_api(self.root)

    def test_changed_sources_new_tests_and_auto_imports_rejected(self):
        self.accept_fixture()
        platform_review.validate_api(self.root)
        for relative in ('src/ProbablyStolen.DigitalStorage.Api/Storage/StorageResult.cs',
                         'tests/Unit/ProbablyStolen.DigitalStorage.Api.Tests/Added.cs',
                         'tests/Directory.Build.targets'):
            with self.subTest(relative=relative):
                target = self.root / relative
                original = target.read_bytes() if target.exists() else None
                target.write_bytes(b'changed')
                with self.assertRaises(ValueError):
                    platform_review.validate_api(self.root)
                if original is None:
                    target.unlink()
                else:
                    target.write_bytes(original)

    def test_empty_identity_list_rejected(self):
        self.data['tests'] = []
        self.accept_fixture()
        with self.assertRaises(ValueError):
            platform_review.validate_api(self.root)
