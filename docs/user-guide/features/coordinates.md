# Coordinates and the converter

🇬🇧 **English** · 🇷🇴 [Română](../ro/features/coordinates.md)

[← Feature reference](README.md) · Related: [Caves and entrances](caves-and-entrances.md) ·
[Your account and settings](account-and-settings.md)

---

Every position the application shows or copies is written the way you chose to read it, and
every place you type a position accepts it in any of those ways. Positions are always stored as
WGS84 longitude and latitude; the formats are only how they are written and read.

## The formats

| Format | Example (the summit of Moldoveanu) | Its settings |
|---|---|---|
| Decimal degrees | `45.59944°N 24.73617°E` | decimal places; with N/S/E/W, or signed with latitude first |
| Degrees, decimal minutes | `45°35.966′N 24°44.170′E` | places of the minutes |
| Degrees, minutes, seconds | `45°35′58.0″N 24°44′10.2″E` | places of the seconds |
| UTM | `35T 323446 E 5052036 N` | zone (each position in its own, or one zone kept); 1 m, 10 m or 100 m |
| MGRS | `35T LL 23447 52037` | 1 m to 10 km; with or without spaces |
| Stereo 70 (Romania) | `E 479541 N 455555` | 1 m to 100 m; E/N or the surveyors' X (north) / Y (east) |
| Swiss grid LV95 and LV03 | `E 2600000 N 1200000` (Bern) | precision |
| Plus Code | `8GQ6HPXP+QF` | code length (8 to 13) |

**Settings › Coordinates** chooses the format positions are shown in, the other formats a
position shown in detail is also listed in, and each format's settings, with a preview on the
summit of Moldoveanu. The settings follow your account to every browser you sign in on.

- **Stereo 70** is converted from WGS84 with the usual seven-parameter datum shift. It is good to
  a few metres — enough to find an entrance, not enough for a survey benchmark. It is offered only
  inside Romania.
- **The Swiss grids** are good to about a metre, and offered only in Switzerland and
  Liechtenstein.
- **UTM with a kept zone**: a team working across a zone edge can keep one zone, so every
  position near the edge is written on one grid. Far from that zone, the position's own zone is
  used.
- **Plus Codes** are always the full (global) code; a short code needs a nearby town to be read
  back, and the application has no list of towns.

A format that has no reference for a position — a Swiss grid in Romania, UTM near a pole — says
**Not available here**.

## Other formats

Where one position is shown in detail — a selected entrance on the map, the entrances of a cave
page — **Other formats** opens it in every format you chose to list, each with its own copy
button, and **Open in the coordinate converter** hands it to the converter.

## Protected positions

A protected cave's entrance is shown, to someone who may not see it exactly, at a position moved
to the protection grid. It is written no finer than that grid **in every format**: decimal
degrees to the hundredth, whole minutes, UTM and the national grids to the kilometre, MGRS by its
10 km square, Plus Codes to six characters. Copying it copies exactly what is shown. Such a
position is not handed to the converter.

## Typing a position

The entrance editor and the point picker have a **Type or paste a position** field. It reads any
of the formats above, and is tolerant of how positions are really written: `°` or `º`, `'` or `′`,
`"` or `″`, N/S/E/W before or after the number, a comma as the decimal mark, UTM with or without
its band letter, MGRS with or without spaces. It says what it read the text as, and anything it
had to assume (two bare numbers are read as latitude, then longitude). A text that can be read
more than one way offers each reading; the format can also be named outright. **Use** fills the
longitude and latitude, which are what is saved.

The field is disabled, and shows nothing, while the position beside it is one you may only see
approximately.

## The coordinate converter

**Tools › Coordinate converter** (also reached from the links above) writes a position typed in
any format in every format at once, each with copy. **Show on a map** puts a temporary marker on a
small map of its own — nothing is added to your map or saved — and **Open this place on the main
map** centres the workspace map there.

**Many positions** converts a pasted list at once: one position per line, or a table (CSV) with
the column that holds the position chosen — or two columns, latitude and longitude. Each line is
read on its own and a line that cannot be read says why. **Download CSV** gives every line in
every format, with latitude and longitude in decimal degrees. At most 5000 lines are converted at
once.

Everything in the converter happens in your browser: nothing typed or pasted there is sent to the
server.
