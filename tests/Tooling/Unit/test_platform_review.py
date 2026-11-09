import json
import shutil
import tempfile
import unittest
from pathlib import Path
from digital_storage_tools import platform_review


class PlatformReviewTests(unittest.TestCase):
    def test_new_ancestor_build_imports_fail_before_test_execution(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in platform_review.CONFIG:
                (root / name).parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(platform_review.ROOT / name, root / name)
            for name in platform_review.PROJECTS:
                shutil.copytree(platform_review.ROOT / name, root / name, ignore=shutil.ignore_patterns('bin', 'obj'))
            platform_review.validate(root)
            for name in (
                "tests/Unit/Directory.Build.targets",
                "src/Directory.Build.props",
                "tests/Directory.Packages.props",
                "src/Directory.Build.rsp",
                platform_review.PROJECTS[-1] + "/Directory.Build.PROPS",
                platform_review.PROJECTS[-1] + "/NewModuleInitializer.CS",
                platform_review.PROJECTS[-1] + "/MSBuild.rsp",
            ):
                with self.subTest(path=name):
                    added = root / name
                    added.write_text("<Project />", encoding="utf-8")
                    try:
                        with self.assertRaisesRegex(ValueError, "reviewed inputs changed"):
                            platform_review.validate(root)
                    finally:
                        added.unlink()

    def test_source_changes_and_new_inputs_fail_review(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in platform_review.CONFIG:
                (root / name).parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(platform_review.ROOT / name, root / name)
            for name in platform_review.PROJECTS:
                shutil.copytree(platform_review.ROOT / name, root / name, ignore=shutil.ignore_patterns('bin', 'obj'))
            platform_review.validate(root)
            test = root / platform_review.PROJECTS[-1] / 'NativeItemIdentityReaderTests.cs'
            original = test.read_bytes()
            for suffix in (b'\n// game call change\n', b'\nclass AdditionalTest {}\n'):
                test.write_bytes(original + suffix)
                with self.assertRaisesRegex(ValueError, 'reviewed inputs changed'):
                    platform_review.validate(root)
            test.write_bytes(original)
            added = root / platform_review.PROJECTS[0] / 'NewModuleInitializer.cs'
            added.write_text('class NewModuleInitializer {}', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'reviewed inputs changed'):
                platform_review.validate(root)

    def test_tests_only_ignores_product_sources_but_pins_the_test_boundary(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in platform_review.CONFIG:
                (root / name).parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(platform_review.ROOT / name, root / name)
            for name in platform_review.PROJECTS:
                shutil.copytree(platform_review.ROOT / name, root / name, ignore=shutil.ignore_patterns('bin', 'obj'))
            platform_review.validate(root, tests_only=True)
            product = root / platform_review.PROJECTS[0] / 'UnreviewedProductChange.cs'
            product.write_text('class UnreviewedProductChange {}', encoding='utf-8')
            (root / 'scripts/test.ps1').write_text('# changed runner', encoding='utf-8')
            platform_review.validate(root, tests_only=True)
            tests = root / platform_review.PROJECTS[-1]
            for relative in ('NativeItemIdentityReaderTests.cs', 'ProbablyStolen.DigitalStorage.Platform.Tests.csproj'):
                with self.subTest(path=relative):
                    target = tests / relative
                    original = target.read_bytes()
                    target.write_bytes(original + b'\n// same name, game call added\n')
                    try:
                        with self.assertRaisesRegex(ValueError, 'test boundary inputs changed'):
                            platform_review.validate(root, tests_only=True)
                    finally:
                        target.write_bytes(original)
            imported = root / 'src/Directory.Build.props'
            imported.write_text('<Project />', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'test boundary inputs changed'):
                platform_review.validate(root, tests_only=True)
