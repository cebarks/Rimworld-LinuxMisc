import importlib.util
import io
import struct
import tempfile
import unittest
import zipfile
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("verify_release", Path(__file__).with_name("verify_release.py"))
verifier = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verifier)


def archive(overrides=None):
    elf = bytearray(64)
    elf[:6] = b"\x7fELF\x02\x01"
    struct.pack_into("<HH", elf, 16, 3, 62)
    files = {
        "LinuxMisc/About/About.xml": b"<ModMetaData><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>",
        "LinuxMisc/Assemblies/RimWorldLinuxMisc.dll": b"managed",
        "LinuxMisc/Assemblies/liblinuxmisc_gamemode.so": bytes(elf),
        "LinuxMisc/README.md": b"documentation",
        "LinuxMisc/Source/RimWorldLinuxMisc.csproj": b'<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><GenerateDependencyFile>false</GenerateDependencyFile></PropertyGroup><ItemGroup><PackageReference Include="Krafs.Rimworld.Ref" Version="1.7.4985-unstable" ExcludeAssets="runtime" /></ItemGroup></Project>',
        "LinuxMisc/Native/gamemode.c": b"/* bridge */",
        "LinuxMisc/Native/vendor/gamemode_client.h": b"/* supported upstream client */",
        "LinuxMisc/Native/vendor/LICENSE": b"upstream license",
    }
    for key, value in (overrides or {}).items():
        if value is None:
            files.pop(key, None)
        else:
            files[key] = value
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w") as zf:
        for name, data in files.items():
            zf.writestr(name, data)
    out.seek(0)
    return out


class ReleaseContractTests(unittest.TestCase):
    def validate(self, overrides=None):
        with zipfile.ZipFile(archive(overrides)) as zf:
            return verifier.validate_archive(zf)

    def test_valid_contract(self):
        self.assertEqual(self.validate(), [])

    def test_missing_native_binary(self):
        errors = self.validate({"LinuxMisc/Assemblies/liblinuxmisc_gamemode.so": None})
        self.assertTrue(any("liblinuxmisc_gamemode.so" in error for error in errors))

    def test_reject_wrong_architecture(self):
        self.assertTrue(self.validate({"LinuxMisc/Assemblies/liblinuxmisc_gamemode.so": b"not ELF"}))

    def test_only_17_supported(self):
        xml = b"<ModMetaData><supportedVersions><li>1.6</li><li>1.7</li></supportedVersions></ModMetaData>"
        self.assertTrue(self.validate({"LinuxMisc/About/About.xml": xml}))

    def test_reject_runtime_or_development_leaks(self):
        for name in ["Assemblies/0Harmony.dll", "Assemblies/RimWorldLinuxMisc.deps.json", "Source/obj/project.assets.json", "Source/bin/debug.dll", "Tests/test.py"]:
            with self.subTest(name=name):
                self.assertTrue(self.validate({"LinuxMisc/" + name: b"leak"}))

    def test_require_native_source_and_upstream_license(self):
        for name in ["Native/gamemode.c", "Native/vendor/gamemode_client.h", "Native/vendor/LICENSE"]:
            with self.subTest(name=name):
                self.assertTrue(self.validate({"LinuxMisc/" + name: None}))

    def test_reject_traversal_and_absolute_entries(self):
        for name in ["../escape", "/escape", "LinuxMisc/../../escape", "LinuxMisc\\..\\escape"]:
            with self.subTest(name=name):
                self.assertTrue(self.validate({name: b"untrusted"}))

    def test_wrong_framework_or_reference(self):
        self.assertTrue(self.validate({"LinuxMisc/Source/RimWorldLinuxMisc.csproj": b"<Project><PropertyGroup><TargetFramework>net472</TargetFramework></PropertyGroup></Project>"}))


if __name__ == "__main__":
    unittest.main()
