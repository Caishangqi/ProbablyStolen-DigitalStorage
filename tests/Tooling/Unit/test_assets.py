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
from digital_storage_tools.package import load_layouts  # noqa: E402
from digital_storage_tools.assets import (  # noqa: E402
    ASSETS, ContentValidationError, check_assets, check_image, check_matches_source, check_resource_tree,
    image_api, check_content, format_parameters, main, prepare_assets, read_localization, validate_write_root,
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
        self.image_contracts = load_layouts()["images"]
        for name, (size, _) in ASSETS.items():
            image = self.Image.new("RGBA", size, (27, 43, 83, 255))
            image.putpixel((0, 0), (93, 3, 7, 0))
            if name == "button_io_finish_done":
                image.save(self.source / (name + ".png"))
                alternate = self.source / "io-port-artwork"
                alternate.mkdir()
                candidate = self.Image.new("RGBA", size, (0, 0, 0, 0))
                candidate.putpixel((7, 7), (241, 197, 13, 255))
                candidate.save(alternate / (name + ".png"))
            else:
                image.save(self.source / (name + ".png"))

    def image(self, predicate):
        return next(name for name, contract in self.image_contracts.items() if predicate(name, contract))

    def runtime(self, name):
        return self.output / self.image_contracts[name]["path"]

    def snapshot(self):
        return {p.relative_to(self.root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in self.root.rglob("*") if p.is_file()}

    def test_prepare_is_repeatable_preserves_sources_and_check_is_read_only(self):
        before = {p.relative_to(self.source).as_posix(): p.read_bytes()
                  for p in self.source.rglob("*") if p.is_file()}
        prepare_assets(self.source, self.output)
        first = self.snapshot()
        check_assets(self.output)
        self.assertEqual(self.snapshot(), first)
        prepare_assets(self.source, self.output)
        self.assertEqual(self.snapshot(), first)
        self.assertEqual(before, {p.relative_to(self.source).as_posix(): p.read_bytes()
                                  for p in self.source.rglob("*") if p.is_file()})
        for name, (source_size, runtime_size) in ASSETS.items():
            if source_size == runtime_size:
                source_path = f"io-port-artwork/{name}.png" if name == "button_io_finish_done" else f"{name}.png"
                self.assertEqual(self.runtime(name).read_bytes(), before[source_path])

    def test_finish_done_uses_nested_candidate_and_never_falls_back_to_old_source(self):
        name = "button_io_finish_done"
        old_source = self.source / (name + ".png")
        candidate = self.source / "io-port-artwork" / (name + ".png")
        with self.Image.open(old_source) as old, self.Image.open(candidate) as approved:
            old_pixel = old.getpixel((7, 7))
            self.assertNotEqual(old_pixel, approved.getpixel((7, 7)))
            expected = approved.getpixel((7, 7))

        prepare_assets(self.source, self.output, (name,))
        with self.Image.open(self.runtime(name)) as result:
            result_pixel = result.getpixel((7, 7))
            self.assertEqual(result_pixel, expected)
            self.assertNotEqual(result_pixel, old_pixel)
        prepared = self.runtime(name).read_bytes()
        candidate.unlink()
        with self.assertRaisesRegex(ContentValidationError, "Could not read source"):
            prepare_assets(self.source, self.output, (name,))
        self.assertEqual(self.runtime(name).read_bytes(), prepared)

    def test_button_image_requires_visible_pixels_and_rejects_old_sheet(self):
        name = self.image(lambda _, item: item["interface"] and item["runtime"] == [16, 16])
        prepare_assets(self.source, self.output, (name,))
        path = self.runtime(name)
        self.Image.new("RGBA", tuple(self.image_contracts[name]["runtime"]), (0, 0, 0, 0)).save(path)
        with self.assertRaisesRegex(ContentValidationError, "Image has no visible pixels"):
            check_image(self.output, name)
        self.Image.new("RGBA", (self.image_contracts[name]["runtime"][0] * 3, self.image_contracts[name]["runtime"][1]), "white").save(path)
        with self.assertRaisesRegex(ContentValidationError, "dimensions"):
            check_image(self.output, name)

    def test_icon_sheet_requires_declared_visible_cells(self):
        name = self.image(lambda _, item: item["sheetCells"] > 0)
        width, height = self.image_contracts[name]["runtime"]
        cell_width = width // (self.image_contracts[name]["sheetCells"] + 1)
        prepare_assets(self.source, self.output, (name,))
        path = self.runtime(name)
        sheet = self.Image.new("RGBA", (width, height), (0, 0, 0, 0))
        sheet.paste((27, 43, 83, 255), (0, 0, cell_width * self.image_contracts[name]["sheetCells"], height))
        sheet.save(path)
        check_image(self.output, name)
        sheet.paste((0, 0, 0, 0), (cell_width, 0, cell_width * 2, height))
        sheet.save(path)
        with self.assertRaisesRegex(ContentValidationError, "cell 2"):
            check_image(self.output, name)

    def test_padded_image_keeps_rgba_and_zero_padding(self):
        name = self.image(lambda _, item: item["source"] != item["runtime"])
        prepare_assets(self.source, self.output, (name,))
        with self.Image.open(self.runtime(name)) as image:
            self.assertEqual(image.size, tuple(self.image_contracts[name]["runtime"]))
            self.assertEqual(image.getpixel((0, 0)), (93, 3, 7, 0))
            width, height = self.image_contracts[name]["runtime"]
            source_width, source_height = self.image_contracts[name]["source"]
            self.assertEqual(image.getpixel((width - 1, 0)), (0, 0, 0, 0) if width > source_width else image.getpixel((width - 1, 0)))
            self.assertEqual(image.getpixel((0, height - 1)), (0, 0, 0, 0) if height > source_height else image.getpixel((0, height - 1)))

    def test_missing_source_fails_before_outputs_are_written(self):
        name = next(iter(ASSETS))
        (self.source / (name + ".png")).unlink()
        with self.assertRaises(ContentValidationError):
            prepare_assets(self.source, self.output)
        self.assertFalse((self.output / "Textures").exists())

    def test_missing_runtime_check_does_not_repair(self):
        name = next(iter(ASSETS))
        prepare_assets(self.source, self.output)
        self.runtime(name).unlink()
        before = self.snapshot()
        with self.assertRaises(ContentValidationError):
            check_assets(self.output)
        self.assertEqual(self.snapshot(), before)

    def test_runtime_tree_reports_missing_and_unexpected_declared_paths(self):
        from digital_storage_tools.package import load_layouts
        for entry in load_layouts()["resources"]:
            path = self.output / entry["path"]
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"synthetic resource")
        missing = load_layouts()["resources"][-1]["path"]
        (self.output / missing).unlink()
        unexpected = self.output / "unexpected-resource.bin"
        unexpected.write_bytes(b"unexpected")
        with self.assertRaisesRegex(ContentValidationError, "missing=.*unexpected=.*unexpected-resource"):
            check_resource_tree(self.output)

    def test_art_edit_after_prepare_is_detected_against_source(self):
        name = self.image(lambda _, item: item["source"] != item["runtime"])
        prepare_assets(self.source, self.output, (name,))
        source_path = self.source / (name + ".png")
        with self.Image.open(source_path) as opened:
            edited = opened.copy()
        edited.putpixel((1, 1), (249, 31, 7, 255))
        edited.save(source_path)
        with self.assertRaisesRegex(ContentValidationError, "Source pixels changed"):
            check_matches_source(self.source, self.output, name)

    def test_wrong_source_size_is_rejected(self):
        name = self.image(lambda _, item: item["source"] != item["runtime"])
        width, height = self.image_contracts[name]["source"]
        self.Image.new("RGBA", (width + 3, height + 5), "white").save(self.source / (name + ".png"))
        with self.assertRaisesRegex(ContentValidationError, "source dimensions"):
            prepare_assets(self.source, self.output, (name,))

    def test_crop_and_stretch_are_rejected(self):
        name = self.image(lambda _, item: item["source"] != item["runtime"])
        prepare_assets(self.source, self.output, (name,))
        width, height = self.image_contracts[name]["runtime"]
        for size in ((width - 1, height - 1), (width + 16, height + 16)):
            with self.subTest(size=size):
                self.Image.new("RGBA", size, "white").save(self.runtime(name))
                with self.assertRaisesRegex(ContentValidationError, "dimensions"):
                    check_image(self.output, name)

    def test_changed_source_pixel_and_opaque_padding_are_rejected(self):
        name = self.image(lambda _, item: item["source"] != item["runtime"])
        name = self.image(lambda _, item: item["source"] != item["runtime"])
        if self.image_contracts[name]["runtime"][0] > self.image_contracts[name]["source"][0]:
            padding = (self.image_contracts[name]["runtime"][0] - 1, 1)
        else:
            padding = (1, self.image_contracts[name]["runtime"][1] - 1)
        checks = (((1, 1), "pixels", lambda: check_matches_source(self.source, self.output, name)),
                  (padding, "Padding", lambda: check_image(self.output, name)))
        for position, message, check in checks:
            with self.subTest(position=position):
                prepare_assets(self.source, self.output, (name,))
                path = self.runtime(name)
                with self.Image.open(path) as opened:
                    image = opened.copy()
                image.putpixel(position, (1, 2, 3, 255))
                image.save(path)
                with self.assertRaisesRegex(ContentValidationError, message):
                    check()

    def test_transparent_grid_center_is_rejected_even_if_source_matches(self):
        name = self.image(lambda _, item: not item["openCenters"] and not item["visible"] and item["source"] == item["runtime"])
        path = self.source / (name + ".png")
        with self.Image.open(path) as opened:
            image = opened.copy()
        image.putpixel((8, 8), (0, 0, 0, 0))
        image.save(path)
        with self.assertRaisesRegex(ContentValidationError, "grid center"):
            prepare_assets(self.source, self.output, (name,))

    def test_wrong_image_with_correct_size_is_rejected_against_its_source(self):
        name = self.image(lambda _, item: item["source"] == item["runtime"] and not item["visible"] and not item["openCenters"])
        prepare_assets(self.source, self.output)
        self.Image.new("RGBA", tuple(self.image_contracts[name]["runtime"]), "white").save(self.runtime(name))
        with self.assertRaisesRegex(ContentValidationError, "pixels"):
            check_matches_source(self.source, self.output, name)

    def test_check_validates_runtime_images_without_art_sources(self):
        prepare_assets(self.source, self.output)
        shutil.rmtree(self.source)
        check_assets(self.output)
        self.assertEqual(main(["--check", "--output-dir", str(self.output), "--items", next(iter(ASSETS))]), 0)

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


