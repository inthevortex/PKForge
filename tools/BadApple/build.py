"""Builds the About screen easter egg's data from the Bad Apple!! PV.

    python3 tools/BadApple/build.py <video file>

Writes src/PKForge.App/Resources/BadApple/frames.bin, audio.m4a (AAC, for Android's MediaPlayer) and
audio.ogg (Vorbis, for the desktop head's SDL_mixer). Needs ffmpeg built with libvorbis.

frames.bin: "BAPL", then width, height, fps and frame count as little-endian uint16, then a
gzip stream of every frame as 1-bit rows (MSB first, 1 = white), each XORed with the frame
before it so still parts compress to nothing.
"""
import gzip
import pathlib
import struct
import subprocess
import sys

import numpy as np

WIDTH, HEIGHT, FPS = 128, 96, 30
OUT = pathlib.Path(__file__).resolve().parents[2] / "src/PKForge.App/Resources/BadApple"


def main(video: str) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    raw = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", video, "-vf", f"fps={FPS},scale={WIDTH}:{HEIGHT}:flags=area,format=gray",
         "-f", "rawvideo", "-"], check=True, capture_output=True).stdout
    frames = np.frombuffer(raw, np.uint8).reshape(-1, HEIGHT, WIDTH) >= 128
    packed = np.packbits(frames.reshape(len(frames), -1), axis=1)
    delta = packed.copy()
    delta[1:] ^= packed[:-1]
    header = b"BAPL" + struct.pack("<4H", WIDTH, HEIGHT, FPS, len(frames))
    (OUT / "frames.bin").write_bytes(header + gzip.compress(delta.tobytes(), 9, mtime=0))
    for name, codec in (("audio.m4a", ["-c:a", "aac", "-b:a", "64k"]), ("audio.ogg", ["-c:a", "libvorbis", "-b:a", "96k"])):
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", video, "-vn", "-ac", "1", *codec,
                        "-map_metadata", "-1", str(OUT / name)], check=True)
    sizes = " + ".join(str((OUT / name).stat().st_size) for name in ("frames.bin", "audio.m4a", "audio.ogg"))
    print(f"{len(frames)} frames, {sizes} bytes")


if __name__ == "__main__":
    main(sys.argv[1])
