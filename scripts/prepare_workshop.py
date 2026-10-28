"""Prepare a SteamCMD manifest from an already verified, extracted mod archive."""

import argparse
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path


def prepare(content_root: Path, output: Path) -> None:
    content_root = content_root.resolve()
    about = content_root / "About"
    preview = about / "Preview.png"
    if not preview.is_file():
        raise ValueError("missing Workshop preview: About/Preview.png")

    item_id = "0"
    saved_id = about / "PublishedFileId.txt"
    if saved_id.exists():
        item_id = saved_id.read_text().strip()
        if not re.fullmatch(r"[1-9][0-9]{0,19}", item_id) or int(item_id) >= 2**64:
            raise ValueError("invalid Workshop item ID; refusing to create a duplicate")

    metadata = ET.parse(about / "About.xml").getroot()
    title = metadata.findtext("name", "").strip()
    description = metadata.findtext("description", "").strip()
    if not title or not description:
        raise ValueError("Workshop title and description must not be empty")

    fields = {
        "appid": "294100",
        "publishedfileid": item_id,
        "contentfolder": str(content_root),
        "previewfile": str(preview),
        "visibility": "0",
        "title": title,
        "description": description,
    }
    lines = ['"workshopitem"', "{"]
    lines.extend(f'    "{key}" {json.dumps(value, ensure_ascii=False)}' for key, value in fields.items())
    lines.append("}")
    output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Prepared Workshop item {item_id}: {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("content_root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    prepare(args.content_root, args.output)
