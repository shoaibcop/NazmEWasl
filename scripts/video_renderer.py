#!/usr/bin/env python3
"""
video_renderer.py — Assembles verse card PNGs into a full video with the original
audio track using MoviePy 2.x. Each card is shown for the duration specified in
verses.json (start_ms / end_ms). The final clip is extended if needed so the video
matches the full audio length.

Requirements:
    pip install moviepy

Usage:
    python video_renderer.py --song-id <guid> --audio <path>
"""

import argparse
import glob
import json
import os
import sys

try:
    from moviepy import AudioFileClip, ImageClip, ColorClip, CompositeVideoClip
except ImportError:
    print("ERROR: moviepy is not installed. Run: pip install moviepy", file=sys.stderr)
    sys.exit(1)


def main():
    parser = argparse.ArgumentParser(description="Video renderer with audio sync (MoviePy 2.x)")
    parser.add_argument("--song-id", required=True)
    parser.add_argument("--audio", required=True)
    parser.add_argument("--fps", type=int, default=24)
    parser.add_argument("--width", type=int, default=1080)
    parser.add_argument("--height", type=int, default=1080)
    parser.add_argument("--end-card-duration", type=float, default=5.0)
    args = parser.parse_args()

    song_id = args.song_id
    scripts_dir = os.path.dirname(os.path.abspath(__file__))
    songs_root = os.path.join(scripts_dir, "..", "songs")
    inputs_dir = os.path.abspath(os.path.join(songs_root, song_id, "inputs"))
    cards_dir = os.path.abspath(os.path.join(songs_root, song_id, "outputs", "cards"))
    video_dir = os.path.abspath(os.path.join(songs_root, song_id, "outputs", "video"))
    output_path = os.path.join(video_dir, "full_video.mp4")
    verses_path = os.path.join(inputs_dir, "verses.json")

    if not os.path.exists(args.audio):
        print(f"ERROR: audio file not found at {args.audio}", file=sys.stderr)
        sys.exit(1)

    # Load verse metadata for timestamps
    if not os.path.exists(verses_path):
        print(f"ERROR: verses.json not found at {verses_path}", file=sys.stderr)
        sys.exit(1)

    with open(verses_path, "r", encoding="utf-8") as f:
        payload = json.load(f)

    # Support both flat list (legacy) and {verses: [...]} envelope
    verse_list = payload.get("verses", payload) if isinstance(payload, dict) else payload

    # Build a lookup: verse_number -> (start_ms, end_ms)
    timestamp_map: dict[int, tuple[int, int]] = {}
    for v in verse_list:
        n = v.get("verse_number")
        start_ms = v.get("start_ms")
        end_ms = v.get("end_ms")
        if n is not None and start_ms is not None and end_ms is not None:
            timestamp_map[n] = (int(start_ms), int(end_ms))

    # Discover card files sorted by verse number
    card_files = sorted(glob.glob(os.path.join(cards_dir, "verse_*.png")))
    if not card_files:
        print(f"ERROR: No verse_*.png files found in {cards_dir}", file=sys.stderr)
        sys.exit(1)

    os.makedirs(video_dir, exist_ok=True)

    # Load audio to know total duration
    audio_clip = AudioFileClip(args.audio)
    audio_duration = audio_clip.duration  # seconds

    # Build a verse_number -> card_path map
    card_map = {}
    for card_path in card_files:
        basename = os.path.basename(card_path)
        try:
            card_map[int(basename.replace("verse_", "").replace(".png", ""))] = card_path
        except ValueError:
            pass

    # Sort verses by absolute start time
    verse_items = sorted(
        [(vn, s, e) for vn, (s, e) in timestamp_map.items() if vn in card_map],
        key=lambda x: x[1]
    )

    # Fallback: equal distribution if no timestamps
    if not verse_items:
        dur = audio_duration / len(card_files)
        clips = [ImageClip(p).with_duration(dur).with_start(i * dur) for i, p in enumerate(card_files)]
    else:
        clips = []
        for i, (vn, start_ms, end_ms) in enumerate(verse_items):
            start_s = start_ms / 1000.0
            # Last verse holds until end of audio
            end_s = audio_duration if i == len(verse_items) - 1 else end_ms / 1000.0
            dur_s = max(end_s - start_s, 0.5)
            clips.append(ImageClip(card_map[vn]).with_duration(dur_s).with_start(start_s))

    # Append end card after audio, if rendered
    end_card_path = os.path.join(cards_dir, "end_card.png")
    end_card_duration = 0.0
    if os.path.exists(end_card_path):
        end_card_duration = args.end_card_duration
        clips.append(
            ImageClip(end_card_path).with_duration(end_card_duration).with_start(audio_duration)
        )

    total_duration = audio_duration + end_card_duration

    # Background covers full duration
    bg = ColorClip(size=(args.width, args.height), color=(0, 0, 0), duration=total_duration)
    video = CompositeVideoClip([bg] + clips)

    # Attach audio track (MoviePy 2.x API)
    video = video.with_audio(audio_clip)

    print(f"Writing video: {len(clips)} clip(s), {video.duration:.1f}s total …")
    video.write_videofile(output_path, fps=args.fps, logger=None)

    audio_clip.close()
    print(f"Wrote {output_path}")
    sys.exit(0)


if __name__ == "__main__":
    main()
