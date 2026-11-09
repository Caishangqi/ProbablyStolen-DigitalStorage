"""Resource checks exercise the actual preparation functions with temporary assets."""

import hashlib
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

REPOSITORY = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPOSITORY / "scripts"))
from digital_storage_tools.assets import (  # noqa: E402
    ASSETS, ContentValidationError, check_assets, check_image, check_matches_source, image_api,
    check_content, format_parameters, main, prepare_assets, read_localization, validate_write_root,
)


class ContentAssetTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="digital-storage-assets-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.source = self.root / "source"
        self.output = self.root / "runtime"
        self.source.mkdir()
        self.Image = image_api()
        for name, (size, _) in ASSETS.items():
            image = self.Image.new("RGBA", size, (27, 43, 83, 255))
            image.putpixel((0, 0), (93, 3, 7, 0))
            image.save(self.source / (name + ".png"))

    def runtime(self, name):
        return self.output / "Textures" / "Items" / (name + ".png")

    def snapshot(self):
        return {p.relative_to(self.root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in self.root.rglob("*") if p.is_file()}

    def test_prepare_is_repeatable_preserves_sources_and_check_is_read_only(self):
        before = {p.name: p.read_bytes() for p in self.source.iterdir()}
        prepare_assets(self.source, self.output)
        first = self.snapshot()
        check_assets(self.output)
        self.assertEqual(self.snapshot(), first)
        prepare_assets(self.source, self.output)
        self.assertEqual(self.snapshot(), first)
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.source.iterdir()})
        for name in ASSETS:
            if name != "drive":
                self.assertEqual(self.runtime(name).read_bytes(), before[name + ".png"])

    def test_drive_keeps_rgba_including_transparent_rgb_and_zero_padding(self):
        prepare_assets(self.source, self.output)
        with self.Image.open(self.runtime("drive")) as image:
            self.assertEqual(image.size, (48, 48))
            self.assertEqual(image.getpixel((0, 0)), (93, 3, 7, 0))
            self.assertEqual(image.getpixel((47, 0)), (0, 0, 0, 0))
            self.assertEqual(image.getpixel((0, 47)), (0, 0, 0, 0))

    def test_missing_source_fails_before_outputs_are_written(self):
        (self.source / "item_storage_cell_1k.png").unlink()
        with self.assertRaises(ContentValidationError):
            prepare_assets(self.source, self.output)
        self.assertFalse(self.output.exists())

    def test_missing_runtime_check_does_not_repair(self):
        prepare_assets(self.source, self.output)
        self.runtime("item_storage_cell_1k").unlink()
        before = self.snapshot()
        with self.assertRaises(ContentValidationError):
            check_assets(self.output)
        self.assertEqual(self.snapshot(), before)

    def test_wrong_source_size_is_rejected(self):
        self.Image.new("RGBA", (48, 48), "white").save(self.source / "drive.png")
        with self.assertRaisesRegex(ContentValidationError, "source dimensions"):
            prepare_assets(self.source, self.output)

    def test_crop_and_stretch_are_rejected(self):
        prepare_assets(self.source, self.output)
        for size in ((47, 47), (64, 64)):
            with self.subTest(size=size):
                self.Image.new("RGBA", size, "white").save(self.runtime("drive"))
                with self.assertRaisesRegex(ContentValidationError, "dimensions"):
                    check_image(self.output, "drive")

    def test_changed_source_pixel_and_opaque_padding_are_rejected(self):
        checks = (((1, 1), "pixels", lambda: check_matches_source(self.source, self.output, "drive")),
                  ((47, 1), "Padding", lambda: check_image(self.output, "drive")))
        for position, message, check in checks:
            with self.subTest(position=position):
                prepare_assets(self.source, self.output)
                path = self.runtime("drive")
                with self.Image.open(path) as opened:
                    image = opened.copy()
                image.putpixel(position, (1, 2, 3, 255))
                image.save(path)
                with self.assertRaisesRegex(ContentValidationError, message):
                    check()

    def test_transparent_grid_center_is_rejected_even_if_source_matches(self):
        path = self.source / "drive.png"
        with self.Image.open(path) as opened:
            image = opened.copy()
        image.putpixel((8, 8), (0, 0, 0, 0))
        image.save(path)
        with self.assertRaisesRegex(ContentValidationError, "grid center"):
            prepare_assets(self.source, self.output)

    def test_wrong_image_with_correct_size_is_rejected_against_its_source(self):
        prepare_assets(self.source, self.output)
        self.Image.new("RGBA", (48, 48), "white").save(self.runtime("terminal"))
        with self.assertRaisesRegex(ContentValidationError, "pixels"):
            check_matches_source(self.source, self.output, "terminal")

    def test_check_validates_runtime_images_without_art_sources(self):
        prepare_assets(self.source, self.output)
        shutil.rmtree(self.source)
        check_assets(self.output)
        self.assertEqual(main(["--check", "--output-dir", str(self.output), "--items", "drive"]), 0)

    def test_source_overlap_and_repository_protected_paths_are_rejected(self):
        for output in (self.source, self.source / "runtime", self.root, REPOSITORY / "art", REPOSITORY / ".reference" / "out"):
            with self.subTest(output=output), self.assertRaises(ContentValidationError):
                validate_write_root(self.source, output)

    def test_reparse_point_is_rejected_without_following_it(self):
        from types import SimpleNamespace
        with patch("digital_storage_tools.assets.Path.lstat", return_value=SimpleNamespace(st_mode=0o040755, st_file_attributes=0x400)):
            with self.assertRaisesRegex(ContentValidationError, "reparse"):
                validate_write_root(self.source, self.output)

    def test_missing_pillow_has_actionable_error(self):
        with patch.dict(sys.modules, {"PIL": None}):
            with self.assertRaisesRegex(ContentValidationError, "mise run setup"):
                image_api()

    def test_cli_requires_explicit_mode_and_reports_failure_nonzero(self):
        with self.assertRaises(SystemExit):
            main([])
        with self.assertRaises(SystemExit):
            main(["--write", "--output-dir", str(self.output)])
        self.assertEqual(main(["--check", "--output-dir", str(self.output)]), 1)


