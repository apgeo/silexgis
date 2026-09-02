#!/usr/bin/env python3
"""Generate invented, geotagged photographs for setting up and checking a photo library.

Why this exists
---------------
Immich and PhotoPrism are indexed against a folder of photographs, and every check that follows
asks a question of the form "how many photographs are inside this rectangle". That needs a set of
images whose coordinates are *known*, so a count that comes back wrong points at a cause instead of
at a mystery.

The images are **invented**: generated pixels, generated timestamps, and coordinates in rectangles
this script names. Real photographs are never used for this. A caving club's photographs are real
people in real places, and their positions are the thing this application exists to protect; a
prototype is the last place to put them. Invented fixtures are also simply the better instrument,
because their counts are known in advance.

The three rectangles, and why there are three
---------------------------------------------
  A   45.40..45.60 N, 22.60..22.90 E   40 files   the answer that must come back non-empty
  A'  22.60..22.90 N, 45.40..45.60 E    7 files   A with latitude and longitude swapped
  B   10.00..10.10 S, 30.00..30.10 W    0 files   nothing is generated near it

A' is the transposition witness. Asking for rectangle A and getting 7 means the request swapped
latitude and longitude somewhere; getting 47 means the far end ignored the rectangle and answered
with the whole library; getting 0 means something else entirely. Three distinguishable numbers
instead of one empty screen.

Runtime: Python 3 and Pillow. Nothing else -- no exiftool, no exiv2, no piexif.

Usage:
    python3 make-fixtures.py [--out DIR] [--seed N] [--bulk N] [--force]
"""

from __future__ import annotations

import argparse
import json
import random
import struct
import sys
from datetime import datetime, timedelta
from fractions import Fraction
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:  # pragma: no cover - environment problem, not a code path
    sys.exit("Pillow is required: python3 -m pip install Pillow")

# EXIF tag numbers. Spelled out because the names differ between libraries and the numbers do not.
TAG_EXIF_IFD = 0x8769
TAG_GPS_IFD = 0x8825
TAG_DATETIME_ORIGINAL = 0x9003
GPS_LATITUDE_REF, GPS_LATITUDE = 1, 2
GPS_LONGITUDE_REF, GPS_LONGITUDE = 3, 4
GPS_ALTITUDE_REF, GPS_ALTITUDE = 5, 6

IMAGE_SIZE = (640, 480)
JPEG_QUALITY = 85


class Rectangle:
    """A named box in degrees, and how many files belong inside it."""

    def __init__(self, key: str, label: str, lat_min: float, lat_max: float,
                 lon_min: float, lon_max: float, count: int, purpose: str) -> None:
        self.key, self.label, self.purpose = key, label, purpose
        self.lat_min, self.lat_max = lat_min, lat_max
        self.lon_min, self.lon_max = lon_min, lon_max
        self.count = count

    def pick(self, rng: random.Random) -> tuple[float, float]:
        # Inset from the edges so that a rectangle compared with > instead of >= still matches.
        pad_lat = (self.lat_max - self.lat_min) * 0.02
        pad_lon = (self.lon_max - self.lon_min) * 0.02
        return (rng.uniform(self.lat_min + pad_lat, self.lat_max - pad_lat),
                rng.uniform(self.lon_min + pad_lon, self.lon_max - pad_lon))

    def as_dict(self) -> dict:
        return {"key": self.key, "label": self.label, "purpose": self.purpose,
                "latMin": self.lat_min, "latMax": self.lat_max,
                "lonMin": self.lon_min, "lonMax": self.lon_max, "count": self.count}


def rectangles(bulk: int) -> list[Rectangle]:
    return [
        Rectangle("A", "fixture rectangle", 45.40, 45.60, 22.60, 22.90, 40 + bulk,
                  "the answer that must be non-empty"),
        Rectangle("Aprime", "transposition witness", 22.60, 22.90, 45.40, 45.60, 7,
                  "rectangle A with latitude and longitude swapped"),
        Rectangle("B", "empty rectangle", -10.10, -10.00, -30.10, -30.00, 0,
                  "nothing is generated within a thousand kilometres of it"),
    ]


def to_dms(value: float) -> tuple[Fraction, Fraction, Fraction]:
    """Degrees as the three rationals EXIF wants.

    Every one must be a Fraction. A (numerator, denominator) tuple -- the obvious shape, and the
    one most EXIF examples use -- fails at save() with "bad operand type for abs(): 'tuple'",
    raised from inside Pillow's rational handling, naming neither the tag nor GPS.
    """
    value = abs(value)
    degrees = int(value)
    minutes_float = (value - degrees) * 60
    minutes = int(minutes_float)
    seconds = (minutes_float - minutes) * 60
    return (Fraction(degrees, 1), Fraction(minutes, 1),
            Fraction(round(seconds * 10000), 10000))


def draw_image(index: int, rect_key: str, lat: float, lon: float) -> Image.Image:
    """A synthetic image whose bytes are unique.

    Uniqueness is load-bearing rather than decorative: both products key a derivative on a content
    hash and both de-duplicate, so a set of byte-identical images collapses to a single photograph
    and every count below becomes 1. Drawing the index guarantees difference and makes two pins
    distinguishable when a popup opens.
    """
    hue = (index * 37) % 360
    red = (1 + (hue % 120)) % 256
    green = (40 + (hue * 3) % 200) % 256
    blue = (90 + (hue * 7) % 160) % 256
    image = Image.new("RGB", IMAGE_SIZE, (red, green, blue))
    draw = ImageDraw.Draw(image)
    draw.rectangle([20, 20, IMAGE_SIZE[0] - 20, IMAGE_SIZE[1] - 20],
                   outline=(255, 255, 255), width=4)
    draw.text((40, 60), f"FIXTURE {index:04d}", fill=(255, 255, 255))
    draw.text((40, 90), f"rect {rect_key}", fill=(255, 255, 255))
    draw.text((40, 120), f"{lat:.5f}, {lon:.5f}", fill=(255, 255, 255))
    draw.text((40, 150), "invented - not a photograph of anything", fill=(255, 255, 255))
    for step in range(0, IMAGE_SIZE[0], 40):
        offset = (index + step) % 255
        draw.line([step, 200, step, 200 + (offset % 120)], fill=(255, 255, 255), width=2)
    return image


def write_one(path: Path, index: int, rect_key: str, lat: float, lon: float,
              taken: datetime) -> None:
    image = draw_image(index, rect_key, lat, lon)
    exif = image.getexif()
    exif[0x010F] = "SilexGIS"           # Make
    exif[0x0110] = "Fixture Generator"  # Model

    sub_ifd = exif.get_ifd(TAG_EXIF_IFD)
    # DateTimeOriginal, not DateTime. Indexers read capture time from this tag; IFD0's DateTime
    # is a different field and is not a substitute for it.
    sub_ifd[TAG_DATETIME_ORIGINAL] = taken.strftime("%Y:%m:%d %H:%M:%S")

    gps = exif.get_ifd(TAG_GPS_IFD)
    gps[GPS_LATITUDE_REF] = "N" if lat >= 0 else "S"
    gps[GPS_LATITUDE] = to_dms(lat)
    gps[GPS_LONGITUDE_REF] = "E" if lon >= 0 else "W"
    gps[GPS_LONGITUDE] = to_dms(lon)
    gps[GPS_ALTITUDE_REF] = 0
    gps[GPS_ALTITUDE] = Fraction(round(600 + (index % 400)), 1)

    image.save(path, "JPEG", quality=JPEG_QUALITY, exif=exif)


def read_back_gps(path: Path) -> tuple[float, float]:
    """Re-read the coordinate straight out of the file's bytes.

    Deliberately parses the APP1/TIFF structure with struct rather than asking Pillow again: a
    round trip through the library that wrote the file proves only that the library agrees with
    itself. This proves the bytes are well-formed EXIF.

    This is a self-check on the generator, not an acceptance check on either product. Whether an
    indexer accepts these files is a question only the indexer can answer.
    """
    data = path.read_bytes()
    if data[:2] != b"\xff\xd8":
        raise ValueError(f"{path.name}: not a JPEG")

    offset = 2
    app1 = None
    while offset < len(data) - 1:
        if data[offset] != 0xFF:
            raise ValueError(f"{path.name}: lost marker alignment at {offset}")
        marker = data[offset + 1]
        if marker in (0xD8, 0xD9):
            offset += 2
            continue
        (length,) = struct.unpack(">H", data[offset + 2:offset + 4])
        if marker == 0xE1 and data[offset + 4:offset + 10] == b"Exif\x00\x00":
            app1 = data[offset + 10:offset + 2 + length]
            break
        offset += 2 + length
    if app1 is None:
        raise ValueError(f"{path.name}: no APP1/Exif segment")

    endian = "<" if app1[:2] == b"II" else ">"
    (ifd0_offset,) = struct.unpack(endian + "I", app1[4:8])

    def read_ifd(at: int) -> dict[int, tuple[int, int, int]]:
        (count,) = struct.unpack(endian + "H", app1[at:at + 2])
        entries = {}
        for i in range(count):
            base = at + 2 + i * 12
            tag, typ, num = struct.unpack(endian + "HHI", app1[base:base + 8])
            (value,) = struct.unpack(endian + "I", app1[base + 8:base + 12])
            entries[tag] = (typ, num, value)
        return entries

    ifd0 = read_ifd(ifd0_offset)
    if TAG_GPS_IFD not in ifd0:
        raise ValueError(f"{path.name}: no GPS IFD pointer in IFD0")
    gps = read_ifd(ifd0[TAG_GPS_IFD][2])

    for tag in (GPS_LATITUDE_REF, GPS_LATITUDE, GPS_LONGITUDE_REF, GPS_LONGITUDE):
        if tag not in gps:
            raise ValueError(f"{path.name}: GPS tag {tag} missing")

    def rationals(at: int) -> float:
        total = 0.0
        for i in range(3):
            numerator, denominator = struct.unpack(endian + "II", app1[at + i * 8:at + i * 8 + 8])
            total += (numerator / denominator) / (60 ** i)
        return total

    lat = rationals(gps[GPS_LATITUDE][2])
    lon = rationals(gps[GPS_LONGITUDE][2])
    if chr(gps[GPS_LATITUDE_REF][2] >> 24) == "S":
        lat = -lat
    if chr(gps[GPS_LONGITUDE_REF][2] >> 24) == "W":
        lon = -lon
    return lat, lon


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", type=Path,
                        default=Path("/srv/data/silexgis/photo-fixtures"),
                        help="where the images go (never inside the checkout)")
    parser.add_argument("--seed", type=int, default=20260903,
                        help="same seed, same files, same counts")
    parser.add_argument("--bulk", type=int, default=0, metavar="N",
                        help="add N more files inside rectangle A, for an indexing measurement")
    parser.add_argument("--force", action="store_true",
                        help="write into a directory that already holds fixtures")
    args = parser.parse_args()

    out = args.out
    if out.exists() and any(out.iterdir()) and not args.force:
        print(f"refusing to write into non-empty {out} (use --force)", file=sys.stderr)
        return 2
    out.mkdir(parents=True, exist_ok=True)

    rng = random.Random(args.seed)
    boxes = rectangles(args.bulk)
    base_time = datetime(2026, 6, 1, 9, 0, 0)

    index = 0
    written: list[dict] = []
    for box in boxes:
        for _ in range(box.count):
            lat, lon = box.pick(rng)
            taken = base_time + timedelta(minutes=17 * index)
            name = f"fixture-{box.key}-{index:04d}.jpg"
            write_one(out / name, index, box.key, lat, lon, taken)
            written.append({"file": name, "rect": box.key, "lat": round(lat, 6),
                            "lon": round(lon, 6), "takenAt": taken.isoformat()})
            index += 1

    # Verify before claiming success. A generator that writes files an indexer cannot read is
    # worse than none, because the failure surfaces later as an empty map.
    checked = 0
    for record in written:
        lat, lon = read_back_gps(out / record["file"])
        if abs(lat - record["lat"]) > 1e-4 or abs(lon - record["lon"]) > 1e-4:
            print(f"GPS mismatch in {record['file']}: wrote "
                  f"{record['lat']},{record['lon']} read {lat:.6f},{lon:.6f}", file=sys.stderr)
            return 1
        checked += 1

    manifest = {
        "generator": "make-fixtures.py",
        "generatedAt": datetime.now().replace(microsecond=0).isoformat(),
        "seed": args.seed,
        "invented": True,
        "note": "Synthetic images with invented coordinates. No real photograph, place or person.",
        "imageCount": len(written),
        "rectangles": [box.as_dict() for box in boxes],
        "files": written,
    }
    (out / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    total_bytes = sum((out / r["file"]).stat().st_size for r in written)
    print(f"wrote {len(written)} images to {out}")
    for box in boxes:
        print(f"  rect {box.key:6s} {box.label:24s} {box.count:4d} files")
    print(f"  GPS verified by independent read: {checked}/{len(written)}")
    print(f"  total {total_bytes / 1024:.1f} kB, manifest.json beside them")
    return 0


if __name__ == "__main__":
    sys.exit(main())
