# Noto Sans — the label font of vector map backgrounds

The map draws a vector background's labels with web fonts, not with the glyph files a MapLibre style
names. Every style's fonts are mapped onto these four (regular, medium, bold, italic), so a vector
background never makes the reader's browser fetch a font from anywhere but this installation.

- Font: Noto Sans, © 2022 The Noto Project Authors — SIL Open Font License 1.1, see `OFL.txt`.
- Files: taken unmodified from the npm package `@fontsource/noto-sans` 5.3.0 (its `files/`
  directory), the Latin and Latin Extended subsets of weights 400, 400 italic, 500 and 700. The CSS
  files here are cut down from the package's own to those two subsets and the `woff2` format.
  Characters of other scripts fall back to the browser's own fonts.
- Package checksum: sha256 of `fontsource-noto-sans-5.3.0.tgz` is
  `f62f8ed40e378f15134e32bed2fd5718d81e9ec7a5603d1022d7b385e2297abc`.
