#!/usr/bin/env python3
"""
card_renderer.py — Renders verse cards as 1080x1080 PNGs using Playwright (headless
Chromium). Chromium handles Persian Nastaliq RTL text natively via CSS, so no PIL
text-layout workarounds are needed.

Requirements:
    pip install playwright jinja2
    playwright install chromium

    Optionally bundle scripts/fonts/Amiri-Regular.ttf for offline Persian rendering.
    Download from: https://github.com/aliftype/amiri/releases
    If the font file is absent, the card falls back to a system serif font.

Usage:
    python card_renderer.py --song-id <guid>
"""

import argparse
import asyncio
import base64
import json
import os
import re
import sys
import tempfile

try:
    from jinja2 import Environment, FileSystemLoader
except ImportError:
    print("ERROR: jinja2 is not installed. Run: pip install jinja2", file=sys.stderr)
    sys.exit(1)


SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))
TEMPLATES_DIR = os.path.join(SCRIPTS_DIR, "templates")
FONTS_DIR = os.path.join(SCRIPTS_DIR, "fonts")


def load_background_b64(inputs_dir: str) -> str | None:
    """Return base64-encoded background image, or None if not found."""
    for name in ("background.jpg", "background.jpeg", "background.png"):
        path = os.path.join(inputs_dir, name)
        if os.path.exists(path):
            with open(path, "rb") as f:
                data = base64.b64encode(f.read()).decode("ascii")
            ext = os.path.splitext(name)[1].lstrip(".")
            mime = "jpeg" if ext in ("jpg", "jpeg") else "png"
            return f"data:image/{mime};base64,{data}"
    return None


def load_verse_image_b64(inputs_dir: str, verse_number: int) -> str | None:
    """Return base64-encoded per-verse image if present in inputs/images/, else None."""
    images_dir = os.path.join(inputs_dir, "images")
    for ext in ("png", "jpg", "jpeg"):
        path = os.path.join(images_dir, f"verse_{verse_number}.{ext}")
        if os.path.exists(path):
            with open(path, "rb") as f:
                data = base64.b64encode(f.read()).decode("ascii")
            mime = "jpeg" if ext in ("jpg", "jpeg") else "png"
            return f"data:image/{mime};base64,{data}"
    return None


def inject_keyword_spans(roman_urdu: str, keywords: list[dict]) -> str:
    """Wrap keyword words in roman_urdu text with <span class='keyword'>."""
    if not keywords:
        return roman_urdu
    result = roman_urdu
    for kw in keywords:
        word = kw.get("word", "")
        if not word:
            continue
        # Word-boundary aware replacement, case-insensitive
        pattern = re.compile(r"\b" + re.escape(word) + r"\b", re.IGNORECASE)
        result = pattern.sub(f'<span class="keyword">{word}</span>', result, count=1)
    return result


def load_render_settings(settings_json_path: str | None) -> dict:
    """Load optional render settings from JSON file, returning defaults if absent."""
    defaults = {
        "persian_font_size": "56",
        "roman_font_size": "34",
        "english_font_size": "26",
        "hindi_font_size": "26",
        "font_family": "Amiri",
        "overlay_opacity": "0.72",
    }
    if settings_json_path and os.path.exists(settings_json_path):
        with open(settings_json_path, "r", encoding="utf-8") as f:
            loaded = json.load(f)
        defaults.update(loaded)
    return defaults


async def render_cards_async(
    song_meta: dict,
    verses: list[dict],
    inputs_dir: str,
    cards_dir: str,
    settings: dict,
    workers: int = 4,
) -> None:
    try:
        from playwright.async_api import async_playwright
    except ImportError:
        print(
            "ERROR: playwright is not installed. Run: pip install playwright && playwright install chromium",
            file=sys.stderr,
        )
        sys.exit(1)

    persian_font_size = settings.get("persian_font_size", "56")
    roman_font_size   = settings.get("roman_font_size", "34")
    english_font_size = settings.get("english_font_size", "26")
    hindi_font_size   = settings.get("hindi_font_size", "26")
    font_family       = settings.get("font_family", "Amiri")
    overlay_opacity   = settings.get("overlay_opacity", "0.72")

    env = Environment(loader=FileSystemLoader(TEMPLATES_DIR))
    verse_template = env.get_template("verse_card.html")

    end_card_template = None
    end_card_path_tpl = os.path.join(TEMPLATES_DIR, "end_card.html")
    if os.path.exists(end_card_path_tpl):
        end_card_template = env.get_template("end_card.html")

    global_bg_b64 = load_background_b64(inputs_dir)
    fonts_dir_uri = FONTS_DIR.replace("\\", "/")

    os.makedirs(cards_dir, exist_ok=True)

    # Pre-render all HTML (Jinja2 is sync; do this before entering async context)
    items: list[tuple[str, str]] = []
    for verse in verses:
        n = verse["verse_number"]
        keywords = verse.get("keywords") or []
        verse_bg_b64 = load_verse_image_b64(inputs_dir, n) or global_bg_b64 or ""
        roman_urdu_html = inject_keyword_spans(verse.get("roman_urdu", ""), keywords)
        html = verse_template.render(
            title=song_meta["title"],
            artist=song_meta["artist"],
            verse_number=n,
            total_verses=song_meta["total_verses"],
            persian_text=verse["persian_text"],
            roman_urdu_html=roman_urdu_html,
            english_text=verse.get("english_text") or "",
            hindi_text=verse.get("hindi_text") or "",
            keywords=keywords,
            background_b64=verse_bg_b64,
            fonts_dir=fonts_dir_uri,
            persian_font_size=persian_font_size,
            roman_font_size=roman_font_size,
            english_font_size=english_font_size,
            hindi_font_size=hindi_font_size,
            font_family=font_family,
            overlay_opacity=overlay_opacity,
        )
        items.append((html, os.path.join(cards_dir, f"verse_{n:02d}.png")))

    if end_card_template is not None:
        end_html = end_card_template.render(
            title=song_meta["title"],
            artist=song_meta["artist"],
            background_b64=global_bg_b64 or "",
            fonts_dir=fonts_dir_uri,
        )
        items.append((end_html, os.path.join(cards_dir, "end_card.png")))

    async with async_playwright() as p:
        browser = await p.chromium.launch()
        sem = asyncio.Semaphore(workers)

        async def render_one(html: str, out_path: str) -> None:
            async with sem:
                with tempfile.NamedTemporaryFile(
                    mode="w", suffix=".html", encoding="utf-8", delete=False
                ) as tmp:
                    tmp.write(html)
                    tmp_path = tmp.name
                page = await browser.new_page(viewport={"width": 1080, "height": 1080})
                try:
                    await page.goto(f"file:///{tmp_path.replace(chr(92), '/')}")
                    await page.wait_for_load_state("load")
                    await page.screenshot(
                        path=out_path,
                        clip={"x": 0, "y": 0, "width": 1080, "height": 1080},
                    )
                    print(f"  Saved {out_path}")
                finally:
                    await page.close()
                    os.remove(tmp_path)

        await asyncio.gather(*[render_one(html, out_path) for html, out_path in items])
        await browser.close()


def render_cards(
    song_meta: dict,
    verses: list[dict],
    inputs_dir: str,
    cards_dir: str,
    settings: dict | None = None,
    workers: int = 4,
) -> None:
    asyncio.run(
        render_cards_async(song_meta, verses, inputs_dir, cards_dir, settings or {}, workers=workers)
    )


def main():
    parser = argparse.ArgumentParser(description="Verse card renderer (Playwright)")
    parser.add_argument("--song-id", required=True)
    parser.add_argument("--settings-json", required=False, default=None)
    parser.add_argument("--workers", type=int, default=4)
    args = parser.parse_args()

    song_id = args.song_id
    songs_root = os.path.join(SCRIPTS_DIR, "..", "songs")
    inputs_dir = os.path.abspath(os.path.join(songs_root, song_id, "inputs"))
    cards_dir = os.path.abspath(os.path.join(songs_root, song_id, "outputs", "cards"))
    verses_path = os.path.join(inputs_dir, "verses.json")

    if not os.path.exists(verses_path):
        print(f"ERROR: verses.json not found at {verses_path}", file=sys.stderr)
        sys.exit(1)

    with open(verses_path, "r", encoding="utf-8") as f:
        payload = json.load(f)

    song_meta = {
        "title": payload.get("title", "Unknown Title"),
        "artist": payload.get("artist", "Unknown Artist"),
        "total_verses": payload.get("total_verses", len(payload.get("verses", []))),
    }
    verses = payload.get("verses", [])

    if not verses:
        print("ERROR: verses.json contains no verses", file=sys.stderr)
        sys.exit(1)

    settings = load_render_settings(args.settings_json)
    print(f"Rendering {len(verses)} card(s) for '{song_meta['title']}' by {song_meta['artist']} …")
    render_cards(song_meta, verses, inputs_dir, cards_dir, settings=settings, workers=args.workers)
    print(f"Done. {len(verses)} card(s) written to {cards_dir}")
    sys.exit(0)


if __name__ == "__main__":
    main()
