import json
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class WorkshopReleaseTests(unittest.TestCase):
    def stage(self, root):
        about = root / "mod/About"
        about.mkdir(parents=True)
        (about / "About.xml").write_text(
            '<ModMetaData><name>Linux &quot;Optimizations&quot;</name>'
            '<description>Line one\nLine two: café</description></ModMetaData>'
        )
        (about / "Preview.png").write_bytes(b"fixture")
        return about

    def prepare(self, root):
        return subprocess.run(
            ["python3", str(ROOT / "scripts/prepare_workshop.py"), str(root / "mod"), str(root / "item.vdf")],
            capture_output=True, text=True,
        )

    def test_workflow_auth_and_manifest_are_explicit(self):
        workflow = (ROOT / ".github/workflows/release.yml").read_text()
        self.assertIn("shared_secret: ${{ secrets.STEAM_SHARED_SECRET }}", workflow)
        self.assertIn("workshop_item: steam-upload/workshop_item.vdf", workflow)
        self.assertIn("python3 scripts/prepare_workshop.py", workflow)
        self.assertIn("actions/upload-artifact@v4", workflow)
        self.assertIn("path: steam-upload/workshop_item.vdf", workflow)

    def test_new_item_manifest_has_metadata_preview_and_absolute_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            about = self.stage(root)
            result = self.prepare(root)
            self.assertEqual(result.returncode, 0, result.stderr)
            manifest = (root / "item.vdf").read_text()
            for key, value in {
                "appid": "294100", "publishedfileid": "0", "visibility": "0",
                "title": 'Linux "Optimizations"', "description": "Line one\nLine two: café",
                "contentfolder": str((root / "mod").resolve()),
                "previewfile": str((about / "Preview.png").resolve()),
            }.items():
                self.assertIn(f'"{key}" {json.dumps(value, ensure_ascii=False)}', manifest)

    def test_saved_item_id_is_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (self.stage(root) / "PublishedFileId.txt").write_text("123456789\n")
            result = self.prepare(root)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn('"publishedfileid" "123456789"', (root / "item.vdf").read_text())

    def test_invalid_saved_id_fails_instead_of_creating_duplicate(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (self.stage(root) / "PublishedFileId.txt").write_text("not-an-id")
            result = self.prepare(root)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("invalid Workshop item ID", result.stderr)
            self.assertFalse((root / "item.vdf").exists())

    def test_missing_preview_fails_before_upload(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (self.stage(root) / "Preview.png").unlink()
            result = self.prepare(root)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("missing Workshop preview", result.stderr)
            self.assertFalse((root / "item.vdf").exists())


if __name__ == "__main__":
    unittest.main()
