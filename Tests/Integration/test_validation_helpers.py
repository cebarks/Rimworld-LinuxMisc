import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class ValidationHelperTests(unittest.TestCase):
    def test_managed_gamemode_runner_refreshes_existing_native_artifacts(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            managed = root / "Tests/GameMode"
            native = root / "Tests/Native"
            tools = root / "tools"
            for path in [managed, native / "build", tools]:
                path.mkdir(parents=True)
            runner = managed / "run_tests.sh"
            shutil.copy2(ROOT / "Tests/GameMode/run_tests.sh", runner)
            (native / "build/liblinuxmisc_gamemode_test.so").write_text("stale artifact")
            refresh = native / "run_tests.sh"
            refresh.write_text("#!/bin/sh\nexit 42\n")
            refresh.chmod(0o755)
            dotnet = tools / "dotnet"
            dotnet.write_text("#!/bin/sh\nexit 43\n")
            dotnet.chmod(0o755)
            env = dict(os.environ, PATH=str(tools) + os.pathsep + os.environ["PATH"])
            result = subprocess.run(["bash", str(runner)], env=env, capture_output=True, text=True)
            self.assertEqual(result.returncode, 42, "native fixtures must refresh before dotnet even if a stale shim exists")

    def test_benchmark_rollup_program_includes_private_memory_fields(self):
        document = (ROOT / "docs/benchmarking.md").read_text()
        match = re.search(r"awk '([^']+)'[^\n]*smaps_rollup", document)
        self.assertIsNotNone(match)
        fixture = "Rss: 8192 kB\nPss: 4096 kB\nPrivate_Clean: 512 kB\nPrivate_Dirty: 1024 kB\nPrivate_Hugetlb: 2048 kB\nAnonHugePages: 4096 kB\n"
        result = subprocess.run(["awk", match.group(1)], input=fixture, capture_output=True, text=True, check=True)
        for field in ["Private_Clean: 512 kB", "Private_Dirty: 1024 kB", "Private_Hugetlb: 2048 kB"]:
            self.assertIn(field, result.stdout)


if __name__ == "__main__":
    unittest.main()
