"""Validate the distributable without extracting or executing its contents."""

import argparse
import struct
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import PurePosixPath


def validate_archive(archive):
    errors = []
    names = {entry.filename for entry in archive.infolist() if not entry.is_dir()}
    root = "LinuxMisc/"
    required = {
        "About/About.xml", "Assemblies/RimWorldLinuxMisc.dll",
        "Assemblies/liblinuxmisc_gamemode.so", "README.md", "Source/RimWorldLinuxMisc.csproj",
    }
    for name in sorted(names):
        path = PurePosixPath(name)
        if not name.startswith(root) or path.is_absolute() or ".." in path.parts or "\\" in name:
            errors.append(f"Unexpected or unsafe archive entry: {name}")
        if "/obj/" in name or "/bin/" in name or "/Tests/" in name or name.endswith(".deps.json"):
            errors.append(f"Development/runtime artifact leaked into release: {name}")
    for name in sorted(required):
        if root + name not in names:
            errors.append(f"Missing required release artifact: {name}")
    dlls = {name for name in names if name.startswith(root + "Assemblies/") and name.endswith(".dll")}
    if dlls != {root + "Assemblies/RimWorldLinuxMisc.dll"}:
        errors.append(f"Expected only the mod DLL, got {sorted(dlls)}")

    native_names = {name for name in names if "/Native/" in name or name.startswith(root + "Source/Native/")}
    if not any(name.endswith(".c") for name in native_names):
        errors.append("Native wrapper C source missing")
    if not any(name.endswith("/gamemode_client.h") for name in native_names):
        errors.append("Pinned upstream gamemode_client.h missing")
    if not any("license" in PurePosixPath(name).name.lower() or "copying" in PurePosixPath(name).name.lower() for name in native_names):
        errors.append("Native upstream license missing")

    metadata = root + "About/About.xml"
    if metadata in names:
        try:
            versions = [item.text for item in ET.fromstring(archive.read(metadata)).findall("supportedVersions/li")]
            if versions != ["1.7"]:
                errors.append(f"Supported game versions must be exactly 1.7, got {versions}")
        except ET.ParseError as error:
            errors.append(f"Invalid About.xml: {error}")

    project = root + "Source/RimWorldLinuxMisc.csproj"
    if project in names:
        try:
            tree = ET.fromstring(archive.read(project))
            if tree.findtext("PropertyGroup/TargetFramework") != "net10.0":
                errors.append("Managed target must be net10.0")
            if tree.findtext("PropertyGroup/GenerateDependencyFile") != "false":
                errors.append("Dependency-file generation must be disabled")
            references = tree.findall("ItemGroup/PackageReference")
            if not any(ref.get("Include") == "Krafs.Rimworld.Ref" and ref.get("Version") == "1.7.4985-unstable" and ref.get("ExcludeAssets") == "runtime" for ref in references):
                errors.append("Expected exact RimRef 1.7.4985-unstable with runtime assets excluded")
        except ET.ParseError as error:
            errors.append(f"Invalid project file: {error}")

    native = root + "Assemblies/liblinuxmisc_gamemode.so"
    if native in names:
        data = archive.read(native)
        if len(data) < 64 or data[:6] != b"\x7fELF\x02\x01" or struct.unpack_from("<HH", data, 16) != (3, 62):
            errors.append("liblinuxmisc_gamemode.so must be an ELF64 little-endian x86-64 shared object")
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", help="LinuxMisc release ZIP to verify")
    args = parser.parse_args()
    try:
        with zipfile.ZipFile(args.archive) as archive:
            errors = validate_archive(archive)
    except (OSError, zipfile.BadZipFile) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    for error in errors:
        print(f"FAIL: {error}", file=sys.stderr)
    if not errors:
        print("PASS: 1.7-only metadata, .NET 10 reference, native/source/license artifacts and archive layout")
    return int(bool(errors))


if __name__ == "__main__":
    sys.exit(main())
