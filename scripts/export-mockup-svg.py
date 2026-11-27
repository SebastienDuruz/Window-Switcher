#!/usr/bin/env python3
"""Export views of the HTML/CSS mock-up as SVG images for the README.

GitHub strips CSS from README files, so the README cannot show the mock-up directly.
Each export takes one part of docs/mock-up/index.html, styles it with
docs/mock-up/styles.css, and wraps both in an SVG <foreignObject>; GitHub then shows
the SVG as an image. The mock-up stays the single source: run this script again after
changing it.

Heights are measured in a headless Chromium-based browser (Chromium, Chrome, Brave or
Edge), so each image fits its content. Set MOCKUP_BROWSER to choose the browser binary.
Without a usable headless browser, measure by hand: write the measure page with
--measure-page, open it in any browser, save the JSON it shows, then pass it with --heights.

Usage:
  python3 scripts/export-mockup-svg.py                    (or: make mockup-svg)
  python3 scripts/export-mockup-svg.py --measure-page artifacts/mockup-measure.html
  python3 scripts/export-mockup-svg.py --heights artifacts/mockup-heights.json
"""

from __future__ import annotations

import argparse
import base64
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
MOCKUP_DIR = REPO_ROOT / "docs" / "mock-up"
OUTPUT_DIR = REPO_ROOT / "docs" / "readme"
LOGO_PATH = REPO_ROOT / "src" / "WindowSwitcher" / "Assets" / "WS_logo.png"
LOGO_SOURCE = "../../src/WindowSwitcher/Assets/WS_logo.png"

STAGE_MARKER = '<div class="stage">'
SETTINGS_MARKER = '<div class="window settings-shell">'

# Shows a whole settings page instead of the scrollable 428 px viewport of the window.
FULL_SETTINGS_PAGE_CSS = (
    ".settings-layout { height: auto; } "
    ".settings-shell .content { overflow: visible; }"
)


@dataclass(frozen=True)
class Export:
    """One SVG image: a part of a mock-up section, at a fixed width."""

    name: str
    section_id: str
    marker: str
    width: int
    settings_page: str | None = None
    extra_css: str = ""
    wrap_in_stage: bool = False


EXPORTS = [
    Export("main-window", "main", STAGE_MARKER, 1030),
    Export("floating-previews", "previews", STAGE_MARKER, 818),
    Export(
        "settings-general",
        "settings",
        SETTINGS_MARKER,
        698,
        settings_page="general",
        extra_css=FULL_SETTINGS_PAGE_CSS,
        wrap_in_stage=True,
    ),
    Export(
        "settings-previews",
        "settings",
        SETTINGS_MARKER,
        698,
        settings_page="previews",
        extra_css=FULL_SETTINGS_PAGE_CSS,
        wrap_in_stage=True,
    ),
    Export(
        "settings-filters",
        "settings",
        SETTINGS_MARKER,
        698,
        settings_page="filters",
        extra_css=FULL_SETTINGS_PAGE_CSS,
        wrap_in_stage=True,
    ),
    Export(
        "settings-keybinds",
        "settings",
        SETTINGS_MARKER,
        698,
        settings_page="keybinds",
        extra_css=FULL_SETTINGS_PAGE_CSS,
        wrap_in_stage=True,
    ),
]

# Page-level text styles that the mock-up sets on <body>, which an export has not.
ROOT_CSS = (
    ".export-root { font-family: \"Segoe UI\", \"Inter\", \"Noto Sans\", system-ui, sans-serif; "
    "color: var(--page-text); font-size: 14px; line-height: 1.5; }"
)

VOID_TAG = re.compile(r"<(img|input|br|hr|meta|link)\b([^>]*?)\s*/?>")
BOOLEAN_ATTRIBUTE = re.compile(r"\s(checked|disabled|selected)(?=[\s/>])")
DIV_TAG = re.compile(r"<div\b|</div>")
HTML_COMMENT = re.compile(r"<!--.*?-->", re.DOTALL)


def fail(message: str) -> None:
    print(f"error: {message}", file=sys.stderr)
    sys.exit(1)


def extract_block(html: str, export: Export) -> str:
    """Returns the first <div> starting with the marker inside the section, tags balanced."""
    section_start = html.find(f'id="{export.section_id}"')
    if section_start < 0:
        fail(f'section id="{export.section_id}" not found in index.html')
    section_end = html.find("</section>", section_start)
    start = html.find(export.marker, section_start, section_end)
    if start < 0:
        fail(f"'{export.marker}' not found in section '{export.section_id}'")

    depth = 0
    for match in DIV_TAG.finditer(html, start, section_end):
        depth += 1 if match.group(0) == "<div" else -1
        if depth == 0:
            return html[start : match.end()]
    fail(f"unbalanced <div> tags in section '{export.section_id}'")
    return ""


def select_settings_page(block: str, page: str) -> str:
    """Checks the radio input of the requested settings page, and only that one."""
    target = f'id="nav-{page}"'
    if target not in block:
        fail(f"settings page '{page}' not found")
    block = re.sub(r'(id="nav-[a-z]+") checked', r"\1", block)
    return block.replace(target, f"{target} checked", 1)


def to_xhtml(block: str, logo_data_uri: str) -> str:
    """Turns mock-up HTML into well-formed XHTML, as <foreignObject> requires."""
    # XML forbids "--" inside comments, which the mock-up section separators use.
    block = HTML_COMMENT.sub("", block)
    block = block.replace(LOGO_SOURCE, logo_data_uri)
    block = BOOLEAN_ATTRIBUTE.sub(lambda m: f' {m.group(1)}="{m.group(1)}"', block)
    return VOID_TAG.sub(lambda m: f"<{m.group(1)}{m.group(2)} />", block)


def build_content(html: str, export: Export, logo_data_uri: str) -> str:
    block = extract_block(html, export)
    if export.settings_page:
        block = select_settings_page(block, export.settings_page)
    if export.wrap_in_stage:
        block = f"{STAGE_MARKER}{block}</div>"
    return to_xhtml(block, logo_data_uri)


def export_css(css: str, export: Export) -> str:
    return f"{css}\n{ROOT_CSS}\n{export.extra_css}"


def find_browser() -> str:
    candidates = [os.environ.get("MOCKUP_BROWSER")] + [
        "chromium",
        "chromium-browser",
        "google-chrome",
        "google-chrome-stable",
        "brave",
        "brave-browser",
        "microsoft-edge",
    ]
    for candidate in candidates:
        if candidate and shutil.which(candidate):
            return shutil.which(candidate) or candidate
    fail(
        "no Chromium-based browser found to measure the images; "
        "install Chromium or set MOCKUP_BROWSER to a Chrome, Brave or Edge binary"
    )
    return ""


def build_measure_page(css: str, contents: dict[str, str], exports: list[Export]) -> str:
    """Returns a page that renders every export at its width and shows the heights as JSON."""
    # Radio groups are per document: give each export its own group, or checking a page in
    # one export would uncheck it in the others.
    blocks = "\n".join(
        f'<div class="export-root" data-name="{e.name}" style="width: {e.width}px">'
        f"<style>{e.extra_css}</style>"
        + contents[e.name].replace('name="settings-nav"', f'name="settings-nav-{e.name}"')
        + "</div>"
        for e in exports
    )
    page = f"""<!doctype html>
<html><head><meta charset="utf-8" /><style>{css}\n{ROOT_CSS}
body {{ margin: 0; background: none; }} .export-root {{ margin-bottom: 40px; }}</style></head>
<body>{blocks}
<pre id="heights"></pre>
<script>
  // Each export applies its own extra CSS; measure them one at a time so they do not mix.
  const roots = [...document.querySelectorAll('.export-root')];
  const heights = {{}};
  for (const root of roots) {{
    roots.forEach(other => other.style.display = other === root ? 'block' : 'none');
    heights[root.dataset.name] = Math.ceil(root.getBoundingClientRect().height);
  }}
  roots.forEach(root => root.style.display = 'block');
  document.getElementById('heights').textContent = 'HEIGHTS:' + JSON.stringify(heights);
</script></body></html>"""
    return page


def measure_heights(browser: str, page: str) -> dict[str, int]:
    """Opens the measure page in a headless browser and returns the measured heights."""
    with tempfile.TemporaryDirectory() as work_dir:
        page_path = Path(work_dir) / "measure.html"
        page_path.write_text(page, encoding="utf-8")
        try:
            result = subprocess.run(
                [
                    browser,
                    "--headless=new",
                    "--disable-gpu",
                    "--no-first-run",
                    f"--user-data-dir={Path(work_dir) / 'profile'}",
                    "--virtual-time-budget=3000",
                    "--dump-dom",
                    page_path.as_uri(),
                ],
                capture_output=True,
                text=True,
                timeout=120,
                check=False,
            )
        except subprocess.TimeoutExpired:
            fail(
                f"{browser} did not answer within 120 s; measure by hand with "
                "--measure-page and --heights (see the script header)"
            )

    match = re.search(r"HEIGHTS:(\{.*?\})", result.stdout)
    if not match:
        fail(f"could not measure the images with {browser}:\n{result.stderr.strip()}")
    return json.loads(match.group(1))


def write_svg(export: Export, css: str, content: str, height: int) -> Path:
    width = export.width
    svg = f"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">
<!-- Generated by scripts/export-mockup-svg.py from docs/mock-up; do not edit by hand. -->
<foreignObject x="0" y="0" width="{width}" height="{height}">
<div xmlns="http://www.w3.org/1999/xhtml" class="export-root" style="width: {width}px">
<style><![CDATA[
{css}
]]></style>
{content}
</div>
</foreignObject>
</svg>
"""
    path = OUTPUT_DIR / f"{export.name}.svg"
    path.write_text(svg, encoding="utf-8")
    return path


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--measure-page",
        type=Path,
        help="write the measure page to this path and stop",
    )
    parser.add_argument(
        "--heights",
        type=Path,
        help="JSON file with the measured heights; skips the headless browser",
    )
    return parser.parse_args()


def read_heights(path: Path) -> dict[str, int]:
    text = path.read_text(encoding="utf-8").strip()
    if text.startswith("HEIGHTS:"):
        text = text[len("HEIGHTS:") :]
    try:
        return json.loads(text)
    except json.JSONDecodeError as error:
        fail(f"{path} is not valid JSON: {error}")
    return {}


def main() -> None:
    arguments = parse_arguments()
    html = (MOCKUP_DIR / "index.html").read_text(encoding="utf-8")
    css = (MOCKUP_DIR / "styles.css").read_text(encoding="utf-8")
    if "]]>" in css:
        fail("styles.css contains ']]>', which cannot be embedded in a CDATA section")
    logo = base64.b64encode(LOGO_PATH.read_bytes()).decode("ascii")
    logo_data_uri = f"data:image/png;base64,{logo}"

    contents = {e.name: build_content(html, e, logo_data_uri) for e in EXPORTS}
    page = build_measure_page(css, contents, EXPORTS)
    if arguments.measure_page:
        arguments.measure_page.parent.mkdir(parents=True, exist_ok=True)
        arguments.measure_page.write_text(page, encoding="utf-8")
        print(f"measure page written to {arguments.measure_page}")
        return

    heights = (
        read_heights(arguments.heights)
        if arguments.heights
        else measure_heights(find_browser(), page)
    )

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    for export in EXPORTS:
        if export.name not in heights or heights[export.name] <= 0:
            fail(f"no height measured for '{export.name}'")
        path = write_svg(
            export, export_css(css, export), contents[export.name], heights[export.name]
        )
        print(f"{path.relative_to(REPO_ROOT)}  {export.width}x{heights[export.name]}")


if __name__ == "__main__":
    main()
