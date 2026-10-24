// SPDX-License-Identifier: AGPL-3.0-or-later
import proj4 from 'proj4';
import { normaliseCoordinateText } from './angles.ts';
import { protectedGridStepMetres } from './protection.ts';
import { FORMAT_SETTINGS, type Stereo70Settings, type SwissGridSettings } from './settings.ts';
import { invalid, NOT_THIS_FORMAT, type FormatDefinition, type ParsedPosition, type ParseOutcome } from './types.ts';

/*
 * National grids, projected with proj4 from definitions written out here rather than fetched: a
 * position has to be shown on a page that is offline or has no signed-in account, and a definition
 * exported without its datum shift (as a PROJ.4 string exported from a modern PROJ database often
 * is, since PROJ now picks the transformation separately) would silently move every point by the
 * whole shift — around 100 m in Romania and 400 m in Switzerland.
 *
 * Each definition carries the published Helmert parameters from WGS 84 to the grid's own datum.
 * They are what limits the accuracy, not the projection arithmetic:
 *
 * - Stereo 70 (EPSG:3844, Pulkovo 1942(58) / Stereo70) uses the seven-parameter transformation the
 *   EPSG register holds for Romania, good to a few metres. The register states its rotations in
 *   the coordinate-frame convention and proj4 reads them in the position-vector one, so they are
 *   written here with their signs turned — the string usually copied about (the same numbers with
 *   the register's signs) puts every point about 38 m from where PROJ, and the database behind
 *   this application, put it. The official transformation is a grid (TransDatRO); a surveyor's
 *   coordinate needs it, a cave entrance found with a handheld receiver does not.
 * - LV95 (EPSG:2056) and LV03 (EPSG:21781) use the three-parameter set swisstopo publishes, good to
 *   about 1 m; LV03 also differs from LV95 by local distortions of up to about 1.5 m that only
 *   swisstopo's own grid removes.
 */

const WGS84 = 'EPSG:4326';

/** West, south, east, north in degrees: where a grid is defined, as its EPSG area of use states. */
type Area = readonly [number, number, number, number];

/** A ring of [longitude, latitude] vertices, closed implicitly. */
type Outline = readonly (readonly [number, number])[];

interface GridSpec {
  id: 'stereo70' | 'lv95' | 'lv03';
  definition: string;
  /** The rectangle the area of use fits in: the projection is asked nothing outside it. */
  area: Area;
  /**
   * The area of use itself, where the rectangle would take in neighbouring countries whose
   * people do not use the grid: a position outside it has no reference in the grid.
   */
  outline?: Outline;
  /** Names a reader may put in front of the numbers, as the normalised text spells them. */
  names: RegExp;
  /**
   * The order of two numbers given without labels: Swiss practice writes the easting first,
   * Romanian practice mostly writes X (the northing) first but not always, so both orders are
   * tried and the area decides.
   */
  bareOrder: 'en' | 'either';
}

const STEREO_70: GridSpec = {
  id: 'stereo70',
  definition:
    '+proj=sterea +lat_0=46 +lon_0=25 +k=0.99975 +x_0=500000 +y_0=500000 +ellps=krass ' +
    '+towgs84=2.329,-147.042,-92.08,-0.309,0.325,0.497,5.69 +units=m +no_defs',
  // Romania, onshore and offshore. The rectangle also takes in strips of its neighbours, where
  // the grid still projects correctly but is not what anyone there uses.
  area: [20.26, 43.44, 31.41, 48.27],
  // EPSG:31700 is the register's withdrawn name for the same grid on a datum that was never
  // realised; survey files still carry it and mean the grid as used, so a text that names it is
  // read as this grid — as the server's coordinate-system registry answers both codes with this
  // very definition, which the 3D viewer then projects surveys with. A test on each side holds
  // both to one shared copy of the string and of the points that pin it.
  names: /^(?:STEREO\s*-?\s*70|S\s*70|EPSG\s*:?\s*(?:3844|31700))\b/,
  bareOrder: 'either',
};

const SWISS_AREA: Area = [5.96, 45.82, 10.49, 47.81];

/*
 * Switzerland and Liechtenstein, simplified to about seventy vertices traced from the national
 * border, clockwise from the western tip beyond Geneva. It is good to a kilometre or two — enough
 * that Annecy, Lake Como, Vorarlberg and the Black Forest, all inside the rectangle, are told the
 * Swiss grids are not available there, while a position a few hundred metres either side of the
 * border may be taken for the other side. Lakes the border crosses (Geneva, Maggiore, Lugano,
 * Constance) are followed roughly through the water. Büsingen and Campione, the two foreign
 * enclaves, are not cut out.
 */
const SWISS_OUTLINE: Outline = [
  [5.96, 46.14],
  [6.13, 46.14],
  [6.24, 46.2],
  [6.24, 46.31],
  [6.52, 46.45],
  [6.8, 46.39],
  [6.78, 46.15],
  [6.87, 46.05],
  [7.04, 45.92],
  [7.2, 45.86],
  [7.55, 45.98],
  [7.87, 45.92],
  [8.03, 46.07],
  [8.15, 46.15],
  [8.08, 46.26],
  [8.3, 46.42],
  [8.45, 46.46],
  [8.45, 46.25],
  [8.6, 46.14],
  [8.7, 46.1],
  [8.8, 46.08],
  [8.8, 45.99],
  [8.92, 45.84],
  [9.04, 45.82],
  [9.09, 45.9],
  [9.05, 46.03],
  [9.16, 46.17],
  [9.28, 46.5],
  [9.45, 46.5],
  [9.47, 46.37],
  [9.6, 46.29],
  [9.9, 46.35],
  [10.15, 46.22],
  [10.25, 46.35],
  [10.05, 46.45],
  [10.05, 46.6],
  [10.29, 46.64],
  [10.45, 46.53],
  [10.49, 46.62],
  [10.47, 46.86],
  [10.4, 47.0],
  [10.14, 46.85],
  [9.87, 46.92],
  [9.6, 47.06],
  [9.63, 47.13],
  [9.56, 47.27],
  [9.63, 47.4],
  [9.56, 47.53],
  [9.25, 47.66],
  [8.87, 47.7],
  [8.71, 47.76],
  [8.61, 47.81],
  [8.4, 47.68],
  [8.54, 47.62],
  [8.42, 47.57],
  [8.23, 47.61],
  [8.06, 47.56],
  [7.79, 47.55],
  [7.69, 47.6],
  [7.59, 47.59],
  [7.56, 47.55],
  [7.45, 47.46],
  [7.2, 47.49],
  [7.0, 47.5],
  [6.9, 47.38],
  [7.03, 47.33],
  [6.95, 47.24],
  [6.7, 47.04],
  [6.43, 46.85],
  [6.13, 46.59],
  [6.07, 46.46],
  [6.13, 46.31],
  [6.05, 46.2],
];
const SWISS_PROJECTION =
  '+proj=somerc +lat_0=46.9524055555556 +lon_0=7.43958333333333 +k_0=1 +ellps=bessel ' +
  '+towgs84=674.374,15.056,405.346,0,0,0,0 +units=m +no_defs';

const LV95: GridSpec & { id: 'lv95' } = {
  id: 'lv95',
  definition: `${SWISS_PROJECTION} +x_0=2600000 +y_0=1200000`,
  area: SWISS_AREA,
  outline: SWISS_OUTLINE,
  names: /^(?:LV\s*95|MN\s*95|CH1903\+|EPSG\s*:?\s*2056)(?![\d+])/,
  bareOrder: 'en',
};

const LV03: GridSpec & { id: 'lv03' } = {
  id: 'lv03',
  definition: `${SWISS_PROJECTION} +x_0=600000 +y_0=200000`,
  area: SWISS_AREA,
  outline: SWISS_OUTLINE,
  names: /^(?:LV\s*03|MN\s*03|CH1903(?!\+)|EPSG\s*:?\s*21781)(?!\d)/,
  bareOrder: 'en',
};

const ALL_NAMES = [STEREO_70.names, LV95.names, LV03.names];

const converters = new Map<string, proj4.Converter>();

function converter(spec: GridSpec): proj4.Converter {
  let found = converters.get(spec.id);
  if (!found) {
    found = proj4(WGS84, spec.definition);
    converters.set(spec.id, found);
  }
  return found;
}

export function insideArea(area: Area, lon: number, lat: number): boolean {
  return lon >= area[0] && lon <= area[2] && lat >= area[1] && lat <= area[3];
}

/** Even-odd ray casting: whether the position lies inside the ring. */
function insideOutline(outline: Outline, lon: number, lat: number): boolean {
  let inside = false;
  for (let i = 0, j = outline.length - 1; i < outline.length; j = i++) {
    const [xi, yi] = outline[i];
    const [xj, yj] = outline[j];
    if (yi > lat !== yj > lat && lon < ((xj - xi) * (lat - yi)) / (yj - yi) + xi) {
      inside = !inside;
    }
  }
  return inside;
}

/** Whether the grid is the one used at a position: inside its rectangle and, where it has one, its outline. */
export function offeredAt(spec: GridSpec, lon: number, lat: number): boolean {
  return insideArea(spec.area, lon, lat) && (spec.outline === undefined || insideOutline(spec.outline, lon, lat));
}

/**
 * A position as [easting, northing] in a grid, or null outside the rectangle its area of use fits
 * in. This is the projection alone: whether the grid is offered at the position is `offeredAt`.
 */
export function toGrid(spec: GridSpec, lon: number, lat: number): [number, number] | null {
  if (!Number.isFinite(lon) || !Number.isFinite(lat) || !insideArea(spec.area, lon, lat)) {
    return null;
  }
  const [easting, northing] = converter(spec).forward([lon, lat]);
  return Number.isFinite(easting) && Number.isFinite(northing) ? [easting, northing] : null;
}

export function fromGrid(spec: GridSpec, easting: number, northing: number): [number, number] | null {
  const [lon, lat] = converter(spec).inverse([easting, northing]);
  return Number.isFinite(lon) && Number.isFinite(lat) ? [lon, lat] : null;
}

/** Truncated, as a grid reference is: the printed value names the square the point lies in. */
function truncated(value: number, step: number): string {
  return String(Math.floor(value / step) * step);
}

function write(
  spec: GridSpec,
  lon: number,
  lat: number,
  step: number,
  axes: 'xy' | 'en' = 'en',
): string | null {
  const grid = offeredAt(spec, lon, lat) ? toGrid(spec, lon, lat) : null;
  if (!grid) {
    return null;
  }
  const easting = truncated(grid[0], step);
  const northing = truncated(grid[1], step);
  // X is the northing and Y the easting in Romanian (and Swiss) surveying, the reverse of the
  // mathematical habit — which is why the default form carries E and N instead.
  return axes === 'xy' ? `X ${northing} Y ${easting}` : `E ${easting} N ${northing}`;
}

type Labelled = { value: number; axis: 'E' | 'N' | null };

/**
 * The numbers of a grid text and the axes they are labelled with: E or Y for the easting, N or X
 * for the northing, in front of or behind the number, with or without `=` or `:`. Swiss
 * thousands marks (`2'600'000`) are dropped, and a comma after five or more digits is a decimal
 * comma (`465123,45`), since no grid value is written with a comma thousands mark that early.
 */
function numbersOf(rest: string): Labelled[] | null {
  const text = rest
    .replace(/(\d)′(?=\d{3})/g, '$1')
    .replace(/(\d{5,}),(\d{1,3})(?=$|[\s;/])/g, '$1.$2')
    .replace(/[=:]/g, ' ');
  const tokens = text.split(/[\s,;/]+/).filter((token) => token !== '');
  const axisOf = (letter: string | undefined): 'E' | 'N' | null =>
    letter === undefined ? null : letter === 'E' || letter === 'Y' ? 'E' : 'N';
  const labelsLead = tokens.length > 0 && /^[ENXY]$/.test(tokens[0]);
  let pending: 'E' | 'N' | null = null;
  const numbers: Labelled[] = [];
  for (const token of tokens) {
    if (/^[ENXY]$/.test(token)) {
      if (labelsLead) {
        pending = axisOf(token);
      } else if (numbers.length > 0 && numbers[numbers.length - 1].axis === null) {
        numbers[numbers.length - 1].axis = axisOf(token);
      } else {
        return null;
      }
      continue;
    }
    const match = /^([ENXY])?(\d+(?:\.\d+)?)(?:M)?([ENXY])?$/.exec(token);
    if (!match) {
      return null;
    }
    numbers.push({ value: Number(match[2]), axis: axisOf(match[1] ?? match[3]) ?? pending });
    pending = null;
  }
  return numbers;
}

function parseGrid(spec: GridSpec, raw: string): ParseOutcome {
  let text = normaliseCoordinateText(raw);
  const named = spec.names.exec(text);
  if (named) {
    text = text.slice(named[0].length);
  } else if (ALL_NAMES.some((names) => names.test(text))) {
    return NOT_THIS_FORMAT;
  }
  const numbers = numbersOf(text);
  if (!numbers || numbers.length !== 2) {
    return named && numbers ? invalid('componentCount') : NOT_THIS_FORMAT;
  }
  const [first, second] = numbers;
  if (first.axis !== null && first.axis === second.axis) {
    return named ? invalid('componentCount') : NOT_THIS_FORMAT;
  }

  const labelled = first.axis !== null || second.axis !== null;
  const orders: [number, number][] = [];
  if (labelled) {
    const firstIsNorthing = first.axis === 'N' || second.axis === 'E';
    orders.push(firstIsNorthing ? [second.value, first.value] : [first.value, second.value]);
  } else {
    orders.push([first.value, second.value]);
    if (spec.bareOrder === 'either' && first.value !== second.value) {
      orders.push([second.value, first.value]);
    }
  }

  const candidates: ParsedPosition[] = [];
  for (const [easting, northing] of orders) {
    const position = fromGrid(spec, easting, northing);
    if (position && offeredAt(spec, position[0], position[1])) {
      candidates.push({
        lon: position[0],
        lat: position[1],
        format: spec.id,
        assumptions: labelled || orders.length === 1 ? [] : ['gridAxisOrder'],
      });
    }
  }
  if (candidates.length > 0) {
    return { kind: 'parsed', candidates };
  }
  // Two bare numbers that land outside the grid's area are most likely not this grid at all; two
  // numbers the reader said were this grid are this grid, and outside it.
  return named ? invalid('gridOutsideArea') : NOT_THIS_FORMAT;
}

export type { Stereo70Settings, SwissGridSettings } from './settings.ts';

/** Romania's national grid. Outside Romania's area of use it is "not available here". */
export const STEREO70: FormatDefinition<Stereo70Settings> = {
  id: 'stereo70',
  ...FORMAT_SETTINGS.stereo70,
  format: (lon, lat, s) => write(STEREO_70, lon, lat, s.precisionMetres, s.axes),
  formatProtected: (lon, lat, s, gridMetres) =>
    write(STEREO_70, lon, lat, Math.max(s.precisionMetres, protectedGridStepMetres(gridMetres)), s.axes),
  parse: (text) => parseGrid(STEREO_70, text),
};

function swiss(spec: GridSpec & { id: 'lv95' | 'lv03' }): FormatDefinition<SwissGridSettings> {
  return {
    id: spec.id,
    ...FORMAT_SETTINGS[spec.id],
    format: (lon, lat, s) => write(spec, lon, lat, s.precisionMetres),
    formatProtected: (lon, lat, s, gridMetres) =>
      write(spec, lon, lat, Math.max(s.precisionMetres, protectedGridStepMetres(gridMetres))),
    parse: (text) => parseGrid(spec, text),
  };
}

/** The Swiss grid since 1995, `E 2600000 N 1200000`; only inside Switzerland and Liechtenstein. */
export const SWISS_LV95 = swiss(LV95);

/** The older Swiss grid, `E 600000 N 200000`; only inside Switzerland and Liechtenstein. */
export const SWISS_LV03 = swiss(LV03);

/** The grids themselves, for the tests and for a caller that needs the raw numbers. */
export const GRIDS = { stereo70: STEREO_70, lv95: LV95, lv03: LV03 } as const;

/** Longitude/latitude to and from a coordinate system, either way null where it has no answer. */
export interface DefinedProjection {
  forward(lon: number, lat: number): [number, number] | null;
  inverse(x: number, y: number): [number, number] | null;
}

/**
 * Longitude/latitude to and from a coordinate system given by its PROJ.4 definition: a raster the
 * layer catalogue declares in its own system arrives with the definition the server resolved for
 * it, datum shift included, and is placed by that rather than by anything written here. Here
 * because this module is the one seam to the projection library. Throws when proj4 cannot read
 * the definition.
 */
export function definedProjection(definition: string): DefinedProjection {
  const projection = proj4(WGS84, definition);
  const finite = (convert: () => number[]): [number, number] | null => {
    try {
      const [a, b] = convert();
      return Number.isFinite(a) && Number.isFinite(b) ? [a!, b!] : null;
    } catch {
      // proj4 throws for some positions outside a projection's domain rather than answering NaN.
      return null;
    }
  };
  return {
    forward: (lon, lat) => finite(() => projection.forward([lon, lat])),
    inverse: (x, y) => finite(() => projection.inverse([x, y])),
  };
}
