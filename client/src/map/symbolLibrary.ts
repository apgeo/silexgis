// SPDX-License-Identifier: AGPL-3.0-or-later

// The map's point symbols: what a symbol reference means, and the image each one is drawn with.
//
// A reference is either a file name from the set bundled beside the application
// (`public/feature_symbols`, e.g. `pit.png`, `osm_cave.svg`) or a shape this module draws itself
// (`shape:cave`, `shape:triangle:d46b08`), optionally in a colour of its own. The server checks
// only that a reference has one of those two shapes; which files exist is this module's catalogue,
// held to the folder by a test.
//
// It is free of any drawing library on purpose: the flat map hands the images to OpenLayers, the 3D
// scene hands the same images to its billboards, and the scene is a separate chunk that must not
// pull a mapping library in with it. Both renderers ask for the image of the same reference here,
// so the two can never draw the same entrance differently.
//
// Performance is the reason every image is cached by reference and colour: a map with hundreds of
// entrances draws one image hundreds of times, and both renderers share their decoded copy by the
// image's address — the flat map through its icon cache, the scene through its texture atlas — so
// the address must be the very same string every time, never rebuilt per feature.

/** The shapes drawn here rather than loaded from a file. The server lists the same names. */
export const SYMBOL_SHAPES = ['circle', 'dot', 'ring', 'square', 'triangle', 'diamond', 'star', 'cross', 'cave'] as const;

export type SymbolShape = (typeof SYMBOL_SHAPES)[number];

export type ParsedSymbol =
  | { kind: 'shape'; shape: SymbolShape; color?: string }
  | { kind: 'file'; file: string };

const FILE_PATTERN = /^[a-z0-9_-]+\.(png|svg)$/;
const SHAPE_PATTERN = /^shape:([a-z]+)(?::([0-9a-f]{6}))?$/;

/** Reads a reference, or `undefined` for anything that is not a well-formed one. */
export function parseSymbol(reference: string | null | undefined): ParsedSymbol | undefined {
  if (typeof reference !== 'string' || reference.length === 0 || reference.length > 100) {
    return undefined;
  }
  if (FILE_PATTERN.test(reference)) {
    return { kind: 'file', file: reference };
  }
  const match = SHAPE_PATTERN.exec(reference);
  if (match && (SYMBOL_SHAPES as readonly string[]).includes(match[1])) {
    return { kind: 'shape', shape: match[1] as SymbolShape, ...(match[2] ? { color: `#${match[2]}` } : {}) };
  }
  return undefined;
}

/** The reference for a drawn shape, in a colour of its own or in the colour of where it is used. */
export function shapeReference(shape: SymbolShape, color?: string): string {
  const hex = color?.replace(/^#/, '').toLowerCase();
  return hex && /^[0-9a-f]{6}$/.test(hex) ? `shape:${shape}:${hex}` : `shape:${shape}`;
}

/** Where a bundled symbol file is served from. */
export function symbolFileUrl(file: string): string {
  return `/feature_symbols/${encodeURIComponent(file)}`;
}

/**
 * An image a renderer can draw a marker with. `scale` is the factor it is drawn at: every image is
 * made at twice its size on screen and drawn at half, so it stays sharp on a high-density display.
 * `width` and `height` are the image's own size, given for drawn shapes and for the bundled SVG
 * files (normalised to 48 pixels) and left out for the legacy PNG files, which carry their own.
 */
export interface SymbolImage {
  src: string;
  scale: number;
  width?: number;
  height?: number;
}

export interface SymbolImageOptions {
  /** The colour a shape without one of its own is drawn in — the layer's own colour. */
  color: string;
  /** Drawn for a position shown only approximately: a shape's white casing becomes dashed. */
  approximate?: boolean;
}

const imageCache = new Map<string, SymbolImage>();

/**
 * The image for a reference, or `undefined` when it is not a well-formed reference or names a file
 * this build does not ship — the caller then draws its own fallback, exactly as for a kind with no
 * symbol at all.
 */
export function symbolImage(reference: string | null | undefined, options: SymbolImageOptions): SymbolImage | undefined {
  const parsed = parseSymbol(reference);
  if (!parsed) {
    return undefined;
  }
  if (parsed.kind === 'file') {
    // A file this build does not ship (removed since it was chosen) is drawn as the caller's
    // fallback rather than handed to the browser as an address that answers 404 and draws nothing.
    if (!shippedFiles.has(parsed.file)) {
      return undefined;
    }
    const key = `file|${parsed.file}`;
    let image = imageCache.get(key);
    if (!image) {
      // Half size for both: the legacy artwork is 64 pixels and lands at 32, as it always did; the
      // SVG files were normalised to 48 and land at 24.
      image = { src: symbolFileUrl(parsed.file), scale: 0.5 };
      imageCache.set(key, image);
    }
    return image;
  }
  const color = parsed.color ?? options.color;
  const approximate = options.approximate === true;
  const key = `shape|${parsed.shape}|${color}|${approximate ? 'a' : 'e'}`;
  let image = imageCache.get(key);
  if (!image) {
    image = drawShape(parsed.shape, color, approximate);
    imageCache.set(key, image);
  }
  return image;
}

/** A preview address for a picker or a table cell, at whatever size the element is given. */
export function symbolPreviewUrl(reference: string | null | undefined, color: string): string | undefined {
  return symbolImage(reference, { color })?.src;
}

// ---- drawn shapes -------------------------------------------------------------------------------

const CASING = '#ffffff';

/**
 * The cave entrance glyph: an arch over a filled mouth. The outline is OpenStreetMap Carto's
 * `natural/cave.svg` (CC0 1.0, dedicated to the public domain), on its own 14-unit grid, chosen
 * because it is the symbol most map readers already know for a cave entrance.
 */
const CAVE_PATH =
  'M3,10C3,9 5,6 6.976,6 9,6 11,9 11,10L7,10ZM7,3C4,3 0,7 0,10l1,0C1,7 5,4 7,4c2,0 6,3 6,6l1,0C14,7 10,3 7,3Z';

/** Size on screen, in CSS pixels, of each shape's square image (casing included). */
const SHAPE_SIZE: Record<SymbolShape, number> = {
  circle: 18,
  dot: 11,
  ring: 18,
  square: 18,
  triangle: 20,
  diamond: 20,
  star: 22,
  cross: 18,
  cave: 22,
};

function drawShape(shape: SymbolShape, color: string, approximate: boolean): SymbolImage {
  const size = SHAPE_SIZE[shape];
  const c = size / 2;
  const dash = approximate ? " stroke-dasharray='2 2'" : '';
  const filled = `fill='${color}' stroke='${CASING}' stroke-width='2'${dash}`;
  let body: string;
  switch (shape) {
    case 'circle':
      body = `<circle cx='${c}' cy='${c}' r='7' ${filled}/>`;
      break;
    case 'dot':
      body = `<circle cx='${c}' cy='${c}' r='4' fill='${color}' stroke='${CASING}' stroke-width='1.5'${dash}/>`;
      break;
    case 'ring':
      // White under the coloured ring, so a hollow marker still reads over a dark photograph.
      body =
        `<circle cx='${c}' cy='${c}' r='6' fill='none' stroke='${CASING}' stroke-width='5'${dash}/>` +
        `<circle cx='${c}' cy='${c}' r='6' fill='none' stroke='${color}' stroke-width='2.5'/>`;
      break;
    case 'square':
      body = `<rect x='${c - 6.5}' y='${c - 6.5}' width='13' height='13' ${filled}/>`;
      break;
    case 'triangle':
      body = `<polygon points='${c},2 ${size - 1.5},${size - 3} 1.5,${size - 3}' stroke-linejoin='round' ${filled}/>`;
      break;
    case 'diamond':
      body = `<polygon points='${c},1.5 ${size - 1.5},${c} ${c},${size - 1.5} 1.5,${c}' stroke-linejoin='round' ${filled}/>`;
      break;
    case 'star':
      body = `<polygon points='${starPoints(c, 9.5, 4.2)}' stroke-linejoin='round' ${filled}/>`;
      break;
    case 'cross':
      body =
        `<path d='M${c - 2.5},2h5v${c - 4.5}h${c - 4.5}v5h-${c - 4.5}v${c - 4.5}h-5v-${c - 4.5}h-${c - 4.5}v-5h${c - 4.5}z'` +
        ` stroke-linejoin='round' ${filled}/>`;
      break;
    case 'cave': {
      // The glyph is scaled from its 14-unit grid onto most of the image, the casing given in the
      // grid's own units so it lands about two pixels wide on screen. The near-transparent disc
      // behind it is there for the pointer only: hit detection reads the image's opaque pixels, and
      // without it a click in the gap between the arch and the mouth would land on nothing.
      const k = (size - 4) / 14;
      const offset = (size - 14 * k) / 2;
      const casing = (2.2 / k).toFixed(3);
      const dashes = approximate ? ` stroke-dasharray='${(2 / k).toFixed(3)} ${(2 / k).toFixed(3)}'` : '';
      body =
        `<circle cx='${c}' cy='${c}' r='${c - 1}' fill='#ffffff' fill-opacity='0.01'/>` +
        `<g transform='translate(${offset.toFixed(3)} ${(offset - k).toFixed(3)}) scale(${k.toFixed(4)})'>` +
        `<path d='${CAVE_PATH}' fill='${color}' stroke='${CASING}' stroke-width='${casing}'` +
        ` stroke-linejoin='round' paint-order='stroke'${dashes}/></g>`;
      break;
    }
  }
  const pixels = size * 2;
  const svg =
    `<svg xmlns='http://www.w3.org/2000/svg' width='${pixels}' height='${pixels}' viewBox='0 0 ${size} ${size}'>` +
    `${body}</svg>`;
  return { src: svgDataUrl(svg), scale: 0.5, width: pixels, height: pixels };
}

function starPoints(c: number, outer: number, inner: number): string {
  const points: string[] = [];
  for (let i = 0; i < 10; i++) {
    const radius = i % 2 === 0 ? outer : inner;
    const angle = -Math.PI / 2 + (i * Math.PI) / 5;
    points.push(`${(c + radius * Math.cos(angle)).toFixed(2)},${(c + radius * Math.sin(angle)).toFixed(2)}`);
  }
  return points.join(' ');
}

// Percent-encoded rather than base64: the documents are tiny and the encoded form stays readable.
function svgDataUrl(svg: string): string {
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

// ---- the catalogue offered to whoever chooses a symbol ------------------------------------------

/** Where a symbol comes from, which is also how the picker groups them. */
export type SymbolGroup = 'shapes' | 'speleo' | 'cartographic' | 'geology';

export interface SymbolEntry {
  reference: string;
  group: SymbolGroup;
  /** Translation key of the symbol's name. */
  nameKey: string;
}

const shape = (name: SymbolShape, nameKey: string): SymbolEntry => ({ reference: `shape:${name}`, group: 'shapes', nameKey });
const speleo = (file: string, nameKey: string): SymbolEntry => ({ reference: file, group: 'speleo', nameKey });
const carto = (file: string, nameKey: string): SymbolEntry => ({ reference: file, group: 'cartographic', nameKey });
const geology = (file: string, nameKey: string): SymbolEntry => ({ reference: file, group: 'geology', nameKey });

/**
 * Every symbol a person choosing one is offered, in the order offered. The files' origins and
 * licences are recorded beside them in `public/feature_symbols/PROVENANCE.md`:
 * `osm_*` from OpenStreetMap Carto (CC0 1.0), `qgis_*` from QGIS (GPL-2.0-or-later), `fgdc_*`
 * the U.S. federal standard for geologic map symbols (FGDC-STD-013-2006, a U.S. Government work).
 */
export const symbolCatalogue: readonly SymbolEntry[] = [
  shape('cave', 'mapSymbols.names.shape_cave'),
  shape('circle', 'mapSymbols.names.shape_circle'),
  shape('dot', 'mapSymbols.names.shape_dot'),
  shape('ring', 'mapSymbols.names.shape_ring'),
  shape('square', 'mapSymbols.names.shape_square'),
  shape('triangle', 'mapSymbols.names.shape_triangle'),
  shape('diamond', 'mapSymbols.names.shape_diamond'),
  shape('star', 'mapSymbols.names.shape_star'),
  shape('cross', 'mapSymbols.names.shape_cross'),

  speleo('sinkhole.png', 'mapSymbols.names.sinkhole'),
  speleo('pit.png', 'mapSymbols.names.pit'),
  speleo('pitch.png', 'mapSymbols.names.pitch'),
  speleo('chimney.png', 'mapSymbols.names.chimney'),
  speleo('tunnel.png', 'mapSymbols.names.tunnel'),
  speleo('continuation.png', 'mapSymbols.names.continuation'),
  speleo('desobstruction.png', 'mapSymbols.names.desobstruction'),
  speleo('exploration_point.png', 'mapSymbols.names.exploration_point'),
  speleo('calm.png', 'mapSymbols.names.calm'),
  speleo('dedritus.png', 'mapSymbols.names.dedritus'),
  speleo('fracture_line.png', 'mapSymbols.names.fracture_line'),
  speleo('lake.png', 'mapSymbols.names.lake'),
  speleo('water_flow.png', 'mapSymbols.names.water_flow'),
  speleo('peak.png', 'mapSymbols.names.peak'),
  speleo('bivouac.png', 'mapSymbols.names.bivouac'),
  speleo('driller.png', 'mapSymbols.names.driller'),
  speleo('flag.png', 'mapSymbols.names.flag'),
  speleo('arrows.png', 'mapSymbols.names.arrows'),
  speleo('generic_feature.png', 'mapSymbols.names.generic_feature'),

  carto('osm_cave.svg', 'mapSymbols.names.osm_cave'),
  carto('qgis_poi_cave.svg', 'mapSymbols.names.qgis_poi_cave'),
  carto('osm_spring.svg', 'mapSymbols.names.osm_spring'),
  carto('osm_waterfall.svg', 'mapSymbols.names.osm_waterfall'),
  carto('osm_peak.svg', 'mapSymbols.names.osm_peak'),
  carto('qgis_poi_peak.svg', 'mapSymbols.names.qgis_poi_peak'),
  carto('osm_saddle.svg', 'mapSymbols.names.osm_saddle'),
  carto('qgis_poi_mine.svg', 'mapSymbols.names.qgis_poi_mine'),
  carto('qgis_poi_mine_abandoned.svg', 'mapSymbols.names.qgis_poi_mine_abandoned'),
  carto('qgis_survey_point.svg', 'mapSymbols.names.qgis_survey_point'),
  carto('qgis_view_point.svg', 'mapSymbols.names.qgis_view_point'),
  carto('qgis_archaeological.svg', 'mapSymbols.names.qgis_archaeological'),
  carto('qgis_alpine_hut.svg', 'mapSymbols.names.qgis_alpine_hut'),
  carto('qgis_shelter.svg', 'mapSymbols.names.qgis_shelter'),
  carto('qgis_camping.svg', 'mapSymbols.names.qgis_camping'),
  carto('qgis_parking.svg', 'mapSymbols.names.qgis_parking'),
  carto('qgis_walker.svg', 'mapSymbols.names.qgis_walker'),
  carto('qgis_information.svg', 'mapSymbols.names.qgis_information'),
  carto('qgis_first_aid.svg', 'mapSymbols.names.qgis_first_aid'),
  carto('qgis_danger.svg', 'mapSymbols.names.qgis_danger'),
  carto('qgis_flag.svg', 'mapSymbols.names.qgis_flag'),
  carto('qgis_camera.svg', 'mapSymbols.names.qgis_camera'),
  carto('qgis_tree.svg', 'mapSymbols.names.qgis_tree'),
  carto('qgis_question.svg', 'mapSymbols.names.qgis_question'),

  geology('fgdc_spring.svg', 'mapSymbols.names.fgdc_spring'),
  geology('fgdc_thermal_spring.svg', 'mapSymbols.names.fgdc_thermal_spring'),
  geology('fgdc_well.svg', 'mapSymbols.names.fgdc_well'),
  geology('fgdc_kettle.svg', 'mapSymbols.names.fgdc_kettle'),
  geology('fgdc_fossil_locality.svg', 'mapSymbols.names.fgdc_fossil_locality'),
  geology('fgdc_bedding_horizontal.svg', 'mapSymbols.names.fgdc_bedding_horizontal'),
  geology('fgdc_bedding_inclined.svg', 'mapSymbols.names.fgdc_bedding_inclined'),
  geology('fgdc_bedding_vertical.svg', 'mapSymbols.names.fgdc_bedding_vertical'),
  geology('fgdc_joint_vertical.svg', 'mapSymbols.names.fgdc_joint_vertical'),
  geology('fgdc_dome.svg', 'mapSymbols.names.fgdc_dome'),
  geology('fgdc_basin.svg', 'mapSymbols.names.fgdc_basin'),
  geology('fgdc_prospect.svg', 'mapSymbols.names.fgdc_prospect'),
  geology('fgdc_open_pit.svg', 'mapSymbols.names.fgdc_open_pit'),
  geology('fgdc_adit.svg', 'mapSymbols.names.fgdc_adit'),
  geology('fgdc_mine_portal.svg', 'mapSymbols.names.fgdc_mine_portal'),
  geology('fgdc_shaft.svg', 'mapSymbols.names.fgdc_shaft'),
  geology('fgdc_survey_station.svg', 'mapSymbols.names.fgdc_survey_station'),
  geology('fgdc_control_point.svg', 'mapSymbols.names.fgdc_control_point'),
];

/** The groups in the order a picker shows them, with their headings' translation keys. */
export const symbolGroups: readonly { group: SymbolGroup; labelKey: string }[] = [
  { group: 'shapes', labelKey: 'mapSymbols.groups.shapes' },
  { group: 'speleo', labelKey: 'mapSymbols.groups.speleo' },
  { group: 'cartographic', labelKey: 'mapSymbols.groups.cartographic' },
  { group: 'geology', labelKey: 'mapSymbols.groups.geology' },
];

const shippedFiles = new Set(
  symbolCatalogue.filter((entry) => !entry.reference.startsWith('shape:')).map((entry) => entry.reference),
);

/** Every bundled symbol file name the catalogue offers. */
export const bundledSymbolFiles: readonly string[] = [...shippedFiles];

/** The catalogue entry a reference names, with a shape's colour set aside. */
export function catalogueEntryOf(reference: string | null | undefined): SymbolEntry | undefined {
  const parsed = parseSymbol(reference);
  if (!parsed) {
    return undefined;
  }
  const bare = parsed.kind === 'shape' ? `shape:${parsed.shape}` : parsed.file;
  return symbolCatalogue.find((entry) => entry.reference === bare);
}
