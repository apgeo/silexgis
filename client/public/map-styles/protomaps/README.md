# The style of the installation's own basemap

`light.json` is the look of the vector basemap an installation serves itself (the catalogue entry
"Self-hosted basemap (light)"). It is drawn over tiles in the Protomaps basemap schema, version 4 —
the schema of the Protomaps daily planet builds a region is downloaded from.

- **Style**: generated once with `@protomaps/basemaps` 5.7.2 (BSD-3-Clause, © Protomaps LLC and Kelso Cartography; visual design CC0; see
  `LICENSE.md`): `layers('protomaps', namedFlavor('light'), { lang: 'en' })`, with the source
  named `protomaps` and the sprite below. The map reads the style's layers only: the data always
  comes from the catalogue entry's own address, which names the extract being served, and the
  source address written in the style is a placeholder nothing fetches. Its name labels are
  rewritten when it is drawn, to the reader's language with the plain name as fallback.
- **Sprite** (`sprites/light*`): the v4 light sprite sheets of
  [protomaps/basemaps-assets](https://github.com/protomaps/basemaps-assets) at commit
  `028c18f713baecad011301ff7a69acc39bcc2ae7`, unmodified. Derived from the MIT-licensed
  [tangrams/icons](https://github.com/tangrams/icons), © 2017 Mapzen — see `sprites/LICENSE.md`.
- **Fonts**: none are fetched from here. Labels are drawn in the Noto Sans web fonts this
  application serves under `/map-fonts/`.
- **Data**: the tiles are OpenStreetMap data (ODbL), credited "© OpenStreetMap contributors".

To regenerate (outside the repository): install `@protomaps/basemaps@5.7.2` in a scratch directory
and write `{ version: 8, sources: { protomaps: { type: 'vector', attribution: '© OpenStreetMap
contributors · Protomaps', url: '/basemap/tiles' } }, sprite: '/map-styles/protomaps/sprites/light',
layers: layers('protomaps', namedFlavor('light'), { lang: 'en' }) }` out as JSON.
