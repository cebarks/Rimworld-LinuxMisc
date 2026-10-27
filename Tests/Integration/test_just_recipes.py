"""Exercise generated shell syntax and the THP measurement program safely."""

import shlex
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class JustRecipeTests(unittest.TestCase):
    def recipe(self):
        result = subprocess.run(["just", "--dry-run", "verify-thp"], cwd=ROOT, capture_output=True, text=True, check=True)
        return result.stderr

    def test_verify_thp_generated_shell_is_valid(self):
        result = subprocess.run(["bash", "-n"], input=self.recipe(), capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_verify_thp_sums_anon_huge_pages_not_mapping_size(self):
        tokens = shlex.split(self.recipe())
        program = tokens[tokens.index("awk") + 1]
        maps = "Size: 999999 kB\nAnonHugePages: 2048 kB\nSize: 8000 kB\nAnonHugePages: 1024 kB\n"
        result = subprocess.run(["awk", program], input=maps, capture_output=True, text=True, check=True)
        self.assertIn("3072 kB (3.0 MiB)", result.stdout)


if __name__ == "__main__":
    unittest.main()
