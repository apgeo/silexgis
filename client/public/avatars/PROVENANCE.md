# Preset avatars — provenance

The eight SVGs in this directory are the built-in avatars a user can pick instead of uploading
a photo (bat, helmet, lamp, rope, karst, stalactite, compass, sump).

## Origin

Original works, drawn for this project as plain geometric SVG paths — no third-party clip art,
no icon-font extracts, no traced source images. Each is a single flat glyph on a coloured disc,
sized for a 64px avatar.

## License status

Original works of the SilexGIS project, distributed under the project license
(AGPL-3.0-or-later). Unlike `feature_symbols/`, there is no inherited artwork here and so no
outstanding provenance question.

## Adding one

The file name is the wire value: `preset-<id>.svg` where `<id>` is the identifier stored on the
account. A new preset needs the file, the id in the server's preset list (which is what the API
publishes and validates against), and its name in both locale files.
