"""Build a portable item browser from a compact FsBank JSON export (stdlib only)."""

import argparse
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
TEMPLATE = Path(__file__).with_name("item-browser.template.html")


def generate(source: Path, destination: Path) -> int:
    if source.resolve() == destination.resolve():
        raise ValueError("Input and output paths must be different.")
    if destination.resolve() in (TEMPLATE.resolve(), Path(__file__).resolve()):
        raise ValueError("Output must not overwrite the generator or template.")
    if destination.suffix.lower() != ".html":
        raise ValueError("Output must have an .html extension.")
    data = json.loads(source.read_text(encoding="utf-8-sig"))
    if not isinstance(data, list) or any(not isinstance(item, dict) for item in data):
        raise ValueError("Expected a compact items.json export: an array of item objects.")
    # Escape HTML-significant characters, including closing script tags in item names.
    payload = json.dumps(data, ensure_ascii=False, separators=(",", ":"), allow_nan=False)
    payload = payload.replace("&", "\\u0026").replace("<", "\\u003c").replace(">", "\\u003e")
    template = TEMPLATE.read_text(encoding="utf-8")
    if template.count("__ITEM_DATA__") != 1:
        raise ValueError("Template must contain exactly one data placeholder.")
    html = template.replace("__ITEM_DATA__", payload)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(html, encoding="utf-8")
    return len(data)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", nargs="?", type=Path, default=ROOT / "docs" / "items.json")
    parser.add_argument("-o", "--output", type=Path, default=ROOT / "docs" / "item-browser.html")
    args = parser.parse_args()
    try:
        count = generate(args.input, args.output)
    except (OSError, ValueError) as error:
        parser.exit(1, f"Error: {error}\n")
    print(f"Generated {args.output.resolve()} ({count} items)")


if __name__ == "__main__":
    main()
