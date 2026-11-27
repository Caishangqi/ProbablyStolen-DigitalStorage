import hashlib
import json
import os
import stat
import struct
import subprocess
import sys
import tempfile
import unittest
import warnings
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZIP_STORED, ZipFile, ZipInfo

from digital_storage_tools.commands import ROOT
from digital_storage_tools.package import SOURCE_MANIFEST_PATH, PackageValidationError, load_layouts
from digital_storage_tools.package_zip import create_package_zip, verify_package_zip

BUILD = ("open_beta", "26772488")


class ReleaseZipTests(unittest.TestCase):
    version = "42.0.1"
    archive_name = f"DigitalStorage-v{version}-{BUILD[0]}-{BUILD[1]}.zip"

    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="digital-storage-zip-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.package = self.root / "package"
        self.output = self.root / "release"
        self.names = load_layouts()["current"]
        for name in self.names:
            path = self.package / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(f"synthetic file {name}".encode("utf-8"))
        manifest = SOURCE_MANIFEST_PATH.read_text(encoding="utf-8")
        start = manifest.index("<ModVersion>") + len("<ModVersion>")
        end = manifest.index("</ModVersion>", start)
        (self.package / "manifest.xml").write_text(manifest[:start] + self.version + manifest[end:], encoding="utf-8")

    def write_build_record(self):
        assemblies = {name: hashlib.sha256((self.package / name).read_bytes()).hexdigest()
                      for name in load_layouts()["assemblies"]}
        record = {"schemaVersion": 1, "steamBranch": BUILD[0], "steamBuildId": BUILD[1], "assemblies": assemblies}
        (self.root / "package-build.json").write_text(json.dumps(record), encoding="utf-8")

    def run_cli(self, *args, **overrides):
        env = os.environ.copy()
        env.pop("DIGITAL_STORAGE_DEPLOY_DIR", None)
        env["ArtifactsPath"] = str(self.root)
        env.update(overrides)
        return subprocess.run([sys.executable, "-B", str(ROOT / "scripts/tool.py"), *args], cwd=ROOT, env=env,
                              capture_output=True, text=True, encoding="utf-8", timeout=30)

    def write_archive(self, path, *, omit=None, extra=None, compression=ZIP_STORED):
        with ZipFile(path, "w", compression=compression) as archive:
            for name in self.names:
                if name != omit:
                    archive.write(self.package / name, f"DigitalStorage/{name}")
            if extra:
                archive.writestr(extra, b"extra")

    def test_release_cli_creates_and_verifies_zip_without_overwriting(self):
        self.write_build_record()
        result = self.run_cli("package-zip", "--package-dir", str(self.package))
        self.assertEqual(result.returncode, 0, result.stderr)
        path = self.output / self.archive_name
        with ZipFile(path) as archive:
            self.assertEqual(set(archive.namelist()), {f"DigitalStorage/{name}" for name in self.names})
            for name in self.names:
                self.assertEqual(archive.read(f"DigitalStorage/{name}"), (self.package / name).read_bytes())
        verified = self.run_cli("verify-package-zip", "--archive", str(path))
        self.assertEqual(verified.returncode, 0, verified.stderr)
        before = path.read_bytes()
        repeated = self.run_cli("package-zip", "--package-dir", str(self.package))
        self.assertNotEqual(repeated.returncode, 0)
        self.assertIn("already exists", repeated.stderr)
        self.assertEqual(path.read_bytes(), before)

    def test_source_allowlist_failure_creates_no_archive(self):
        (self.package / "unknown.dll").write_bytes(b"not a product DLL")
        with self.assertRaisesRegex(PackageValidationError, "unknown.dll"):
            create_package_zip(self.package, self.output, BUILD)
        self.assertFalse(self.output.exists())

    def test_verifier_reports_missing_and_unexpected_members_without_extracting(self):
        path = self.root / self.archive_name
        missing = self.names[-1]
        self.write_archive(path, omit=missing, extra="DigitalStorage/../foreign.dll")
        result = self.run_cli("verify-package-zip", "--archive", str(path))
        self.assertEqual(result.returncode, 2)
        self.assertIn(f"missing=['DigitalStorage/{missing}']", result.stderr)
        self.assertIn("unexpected=['DigitalStorage/../foreign.dll']", result.stderr)
        self.assertFalse((self.root / "foreign.dll").exists())

    def test_verifier_rejects_duplicate_and_nonregular_members(self):
        path = self.root / self.archive_name
        relative = self.names[-1]
        member_name = f"DigitalStorage/{relative}"
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            self.write_archive(path, extra=member_name)
        with self.assertRaisesRegex(PackageValidationError, "Duplicate ZIP member"):
            verify_package_zip(path)
        self.write_archive(path, omit=relative)
        link = ZipInfo(member_name)
        link.create_system = 3
        link.external_attr = (stat.S_IFLNK | 0o777) << 16
        with ZipFile(path, "a") as archive:
            archive.writestr(link, "target")
        with self.assertRaisesRegex(PackageValidationError, "regular files"):
            verify_package_zip(path)

    def test_release_cli_requires_a_build_record_of_this_package(self):
        missing = self.run_cli("package-zip", "--package-dir", str(self.package))
        self.assertEqual(missing.returncode, 2)
        self.assertIn("No package build record", missing.stderr)
        self.write_build_record()
        assembly = self.package / load_layouts()["assemblies"][0]
        assembly.write_bytes(assembly.read_bytes() + b"rebuilt")
        changed = self.run_cli("package-zip", "--package-dir", str(self.package))
        self.assertEqual(changed.returncode, 2)
        self.assertIn("changed since they were built", changed.stderr)
        self.assertFalse(self.output.exists())

    def test_verifier_requires_the_build_in_the_filename(self):
        path = self.root / self.archive_name
        self.write_archive(path)
        untagged = self.root / f"DigitalStorage-v{self.version}.zip"
        path.rename(untagged)
        with self.assertRaisesRegex(PackageValidationError, "<branch>-<buildid>"):
            verify_package_zip(untagged)

    def test_create_refuses_derived_release_directory_inside_protected_root(self):
        result = self.run_cli("package-zip", "--package-dir", str(self.package),
                              DIGITAL_STORAGE_GAME_DIR=str(self.output))
        self.assertEqual(result.returncode, 2)
        self.assertIn("release directory cannot be inside a protected directory", result.stderr)
        self.assertFalse(self.output.exists())

    def test_verifier_rejects_compressed_members_and_inconsistent_stored_sizes(self):
        path = self.root / self.archive_name
        self.write_archive(path, compression=ZIP_DEFLATED)
        with self.assertRaisesRegex(PackageValidationError, "without compression"):
            verify_package_zip(path)

        self.write_archive(path)
        data = bytearray(path.read_bytes())
        central = data.index(b"PK\x01\x02")
        size = struct.unpack_from("<I", data, central + 20)[0]
        struct.pack_into("<I", data, central + 20, size + 1)
        path.write_bytes(data)
        with self.assertRaisesRegex(PackageValidationError, "Stored ZIP member sizes differ"):
            verify_package_zip(path)

    def test_verifier_reports_invalid_zip_filename_encoding(self):
        path = self.root / self.archive_name
        self.write_archive(path)
        data = bytearray(path.read_bytes())
        central = data.index(b"PK\x01\x02")
        flags = struct.unpack_from("<H", data, central + 8)[0]
        struct.pack_into("<H", data, central + 8, flags | 0x800)
        data[central + 46] = 0xff
        path.write_bytes(data)
        result = self.run_cli("verify-package-zip", "--archive", str(path))
        self.assertEqual(result.returncode, 2)
        self.assertIn("Package ZIP error: Package ZIP could not be read", result.stderr)
        self.assertNotIn("Traceback", result.stderr)

    def test_verifier_checks_manifest_metadata(self):
        path = self.root / self.archive_name
        manifest = self.package / "manifest.xml"
        text = manifest.read_text(encoding="utf-8")
        manifest.write_text(text.replace("<ID>digital_storage</ID>", "<ID>different_mod</ID>"), encoding="utf-8")
        self.write_archive(path)
        with self.assertRaisesRegex(PackageValidationError, "Expected package ID"):
            verify_package_zip(path)

    def test_verifier_reads_member_payloads_and_enforces_versioned_filename(self):
        path = self.root / self.archive_name
        self.write_archive(path)
        mismatched = self.root / "DigitalStorage-v0.zip"
        path.rename(mismatched)
        with self.assertRaisesRegex(PackageValidationError, "must match ModVersion"):
            verify_package_zip(mismatched)
        mismatched.rename(path)
        payload = (self.package / self.names[-1]).read_bytes()
        data = path.read_bytes()
        self.assertEqual(data.count(payload), 1)
        path.write_bytes(data.replace(payload, b"!" + payload[1:]))
        with self.assertRaisesRegex(PackageValidationError, "could not be read"):
            verify_package_zip(path)
