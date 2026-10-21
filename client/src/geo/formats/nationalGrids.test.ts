// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import path from 'node:path';
import proj4 from 'proj4';
import { describe, expect, it } from 'vitest';
import { fromGrid, GRIDS, STEREO70, SWISS_LV03, SWISS_LV95, toGrid } from './nationalGrids.ts';

/**
 * The grid's one definition and the points that pin it, in a file at the root of the working tree
 * that the server's test of its coordinate-system registry reads as well: the registry answers
 * EPSG:3844 and EPSG:31700 with this definition, and the 3D viewer projects surveys with that
 * answer. Each side is held to the file, so neither can drift from the other unnoticed. The
 * points are invented positions over Romania with the easting and northing PostGIS gives them
 * with ST_Transform into EPSG:3844.
 */
interface Stereo70Contract {
  codes: number[];
  definition: string;
  referencePoints: { lon: number; lat: number; easting: number; northing: number }[];
  toleranceMetres: number;
}
const STEREO70_CONTRACT = JSON.parse(
  readFileSync(path.resolve(process.cwd(), '..', 'contract', 'crs', 'stereo70.json'), 'utf8'),
) as Stereo70Contract;
/** The pinned values are rounded to the millimetre; proj4 matches PROJ to well under a centimetre. */
const STEREO70_AGREEMENT_METRES = 0.01;

function dms(degrees: number, minutes: number, seconds: number): number {
  return degrees + minutes / 60 + seconds / 3600;
}

describe('Stereo 70', () => {
  it('puts the projection origin at 500 000 / 500 000 on its own ellipsoid', () => {
    // The projection alone, without the datum shift: the point the grid is defined around.
    const projection = GRIDS.stereo70.definition.replace(/\+towgs84=\S+ /, '');
    const [easting, northing] = proj4('+proj=longlat +ellps=krass +no_defs', projection, [25, 46]);
    expect(easting).toBeCloseTo(500000, 3);
    expect(northing).toBeCloseTo(500000, 3);
  });

  it('is the one definition the server registry answers for EPSG:3844 and EPSG:31700', () => {
    expect(STEREO70_CONTRACT.codes).toEqual([3844, 31700]);
    expect(GRIDS.stereo70.definition).toBe(STEREO70_CONTRACT.definition);
  });

  it('agrees with PROJ, as the database transforms into EPSG:3844, to the millimetre', () => {
    for (const { lon, lat, easting, northing } of STEREO70_CONTRACT.referencePoints) {
      const grid = toGrid(GRIDS.stereo70, lon, lat)!;
      expect(Math.hypot(grid[0] - easting, grid[1] - northing)).toBeLessThan(STEREO70_AGREEMENT_METRES);
    }
  });

  it('places a grid position where the reference says, as the 3D viewer does with the registry answer', () => {
    // The viewer is handed the registry's string and turns survey stations from the grid into
    // longitude and latitude with proj4 — the inverse of what the format writes.
    const viewer = proj4(STEREO70_CONTRACT.definition, 'EPSG:4326');
    const metresPerDegree = (6_371_000 * Math.PI) / 180;
    for (const { lon, lat, easting, northing } of STEREO70_CONTRACT.referencePoints) {
      const [placedLon, placedLat] = viewer.forward([easting, northing]);
      const east = (placedLon - lon) * Math.cos((lat * Math.PI) / 180) * metresPerDegree;
      const north = (placedLat - lat) * metresPerDegree;
      expect(Math.hypot(east, north)).toBeLessThan(STEREO70_AGREEMENT_METRES);
      const [formatLon, formatLat] = fromGrid(GRIDS.stereo70, easting, northing)!;
      expect(Math.hypot(formatLon - placedLon, formatLat - placedLat)).toBeLessThan(1e-9);
    }
  });

  it('writes, reads back and offers the Romanian axis order', () => {
    const settings = STEREO70.defaults;
    const text = STEREO70.format(25.44721, 45.53127, settings)!;
    expect(text).toMatch(/^E \d{6} N \d{6}$/);
    expect(STEREO70.format(25.44721, 45.53127, { ...settings, axes: 'xy' })).toMatch(/^X \d{6} Y \d{6}$/);
    const outcome = STEREO70.parse(text);
    expect(outcome.kind).toBe('parsed');
    if (outcome.kind !== 'parsed') return;
    expect(outcome.candidates).toHaveLength(1);
    expect(outcome.candidates[0].lat).toBeCloseTo(45.53127, 4);
    expect(outcome.candidates[0].lon).toBeCloseTo(25.44721, 4);
  });

  it('reads the forms people write it in', () => {
    const [easting, northing] = toGrid(GRIDS.stereo70, 25.44721, 45.53127)!.map(Math.round);
    for (const text of [
      `X=${northing}; Y=${easting}`,
      `Stereo 70: ${northing}N ${easting}E`,
      `${easting},50 ${northing},25`,
    ]) {
      const outcome = STEREO70.parse(text);
      expect(outcome.kind === 'parsed' && outcome.candidates[0].format).toBe('stereo70');
    }
    // Two bare numbers name the order by neither label nor magnitude: both orders that land in
    // Romania are offered, and the reader is told the order was assumed.
    const bare = STEREO70.parse('480000 520000');
    expect(bare.kind === 'parsed' && bare.candidates.map((c) => c.assumptions)).toEqual([
      ['gridAxisOrder'],
      ['gridAxisOrder'],
    ]);
  });

  it('is not available outside Romania, and says why when it was asked for by name', () => {
    expect(STEREO70.format(7.44, 46.95, STEREO70.defaults)).toBeNull();
    expect(STEREO70.parse('Stereo70 E 5000000 N 5000000')).toEqual({ kind: 'invalid', error: 'gridOutsideArea' });
    expect(STEREO70.parse('378752 5043143')).toEqual({ kind: 'notThisFormat' });
    expect(STEREO70.parse('LV95 2600000 1200000')).toEqual({ kind: 'notThisFormat' });
  });
});

describe('the Swiss grids', () => {
  // swisstopo's worked example for its transformation formulas: this WGS 84 position is
  // LV95 2 700 000 / 1 100 000 and LV03 700 000 / 100 000.
  const lon = dms(8, 43, 49.79);
  const lat = dms(46, 2, 38.87);

  it.each([
    ['LV95', GRIDS.lv95, 2700000, 1100000],
    ['LV03', GRIDS.lv03, 700000, 100000],
  ])('meets the published reference point in %s', (_name, grid, easting, northing) => {
    // The three-parameter shift is good to about a metre; here it lands within centimetres.
    const [e, n] = toGrid(grid, lon, lat)!;
    expect(Math.hypot(e - easting, n - northing)).toBeLessThan(0.5);
    const [backLon, backLat] = fromGrid(grid, easting, northing)!;
    expect(backLon).toBeCloseTo(lon, 4);
    expect(backLat).toBeCloseTo(lat, 4);
  });

  it('writes and reads both, telling them apart by magnitude', () => {
    // The grids' origin near Bern: swisstopo's worked example above lies across the border, on the
    // Italian shore of Lake Maggiore, where neither grid is offered.
    const [bernLon, bernLat] = fromGrid(GRIDS.lv95, 2600000, 1200000)!;
    expect(SWISS_LV95.format(bernLon + 1e-6, bernLat + 1e-6, { precisionMetres: 100 })).toBe('E 2600000 N 1200000');
    for (const text of ["2'600'000 / 1'200'000", 'LV95 E 2600000 N 1200000', 'N 1200000 E 2600000']) {
      const outcome = SWISS_LV95.parse(text);
      expect(outcome.kind === 'parsed' && outcome.candidates[0].lat).toBeCloseTo(bernLat, 4);
      expect(SWISS_LV03.parse(text).kind).not.toBe('parsed');
    }
    const old = SWISS_LV03.parse('600000 200000');
    expect(old.kind === 'parsed' && old.candidates[0].lon).toBeCloseTo(bernLon, 4);
    expect(SWISS_LV95.parse('600000 200000').kind).not.toBe('parsed');
  });

  it.each([
    ['Bern', 7.44, 46.95],
    ['Geneva', 6.145, 46.204],
    ['Basel', 7.588, 47.558],
    ['Lugano', 8.95, 46.0],
    ['Val Müstair', 10.448, 46.63],
    ['Vaduz, Liechtenstein', 9.52, 47.14],
  ])('is offered in %s', (_place, atLon, atLat) => {
    expect(SWISS_LV95.format(atLon, atLat, SWISS_LV95.defaults)).toMatch(/^E 2\d{6} N 1\d{6}$/);
    expect(SWISS_LV03.format(atLon, atLat, SWISS_LV03.defaults)).toMatch(/^E \d{6} N \d{5,6}$/);
  });

  it.each([
    ['Romania', 25.44721, 45.53127],
    // The four below lie inside the rectangle the Swiss area of use fits in, but not in it.
    ['France, near Annecy', 6.13, 45.9],
    ['Italy, by Lake Como', 9.39, 45.95],
    ['Austria, in Vorarlberg', 9.9, 47.4],
    ['Germany, in the southern Black Forest', 8.0, 47.8],
    ['Italy, on Lake Maggiore', lon, lat],
  ])('is not available in %s', (_place, atLon, atLat) => {
    expect(SWISS_LV95.format(atLon, atLat, SWISS_LV95.defaults)).toBeNull();
    expect(SWISS_LV03.format(atLon, atLat, SWISS_LV03.defaults)).toBeNull();
    expect(SWISS_LV95.formatProtected(atLon, atLat, SWISS_LV95.defaults, 5000)).toBeNull();
    const [e, n] = toGrid(GRIDS.lv95, atLon, atLat) ?? [0, 0];
    if (e !== 0) {
      // A reference typed for such a position is read as outside the area, not as a position there.
      expect(SWISS_LV95.parse(`LV95 E ${Math.round(e)} N ${Math.round(n)}`)).toEqual({
        kind: 'invalid',
        error: 'gridOutsideArea',
      });
    }
  });

  it('is never taken for Stereo 70', () => {
    expect(STEREO70.parse('700000 100000').kind).not.toBe('parsed');
  });
});

describe('a protected position in a national grid', () => {
  it('is written to the kilometre whatever the precision setting', () => {
    expect(STEREO70.formatProtected(25.4472, 45.5313, STEREO70.defaults, 5000)).toMatch(/^E \d{3}000 N \d{3}000$/);
    expect(SWISS_LV95.formatProtected(7.44, 46.95, SWISS_LV95.defaults, 5000)).toMatch(/^E \d{4}000 N \d{4}000$/);
  });
});
