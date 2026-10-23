// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { condition, emptyDocument, fromGroups, withSort, withWhere } from './document.ts';
import {
  anchorScopes,
  DEFAULT_MAX_RADIUS_METERS,
  holdsPosition,
  isRadius,
  isSpatialComplete,
  parsePolygon,
  polygonText,
  withAnchor,
  withoutAnchor,
} from './spatial.ts';
import { isComplete } from './preflight.ts';
import { caveRegistryDocument, readCaveRegistry } from './caveRegistry.ts';
import { decodeFilter, encodeFilter } from './url.ts';
import type { FilterDocument, FilterNode } from './types.ts';

const CAVE = '0b3f6a52-1d7e-4c5a-9a61-3f2d9c4e8b10';
const SAVED = '5c1e2d3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f';
const AREA = '7d2c9e14-6b3a-4f58-a1c2-0e9d8b7a6f54';
const RING = [[23, 45], [23.01, 45], [23.01, 45.01], [23, 45]];

const nearPoint = condition('position', 'within', [
  { type: 'number', value: 500 }, { type: 'point', longitude: 23.5, latitude: 45.25 },
]);
const nearCave = condition('position', 'within', [
  { type: 'number', value: 200 }, { type: 'object', world: 'feature', id: CAVE },
]);
const nearSaved = condition('position', 'within', [
  { type: 'number', value: 1000 }, { type: 'savedFilter', id: SAVED },
]);
const inPolygon = condition('position', 'inside', [{ type: 'polygon', coordinates: [RING] }]);
const inArea = condition('position', 'inside', [{ type: 'object', world: 'feature', id: AREA }]);

function everySpatialValue(): FilterDocument {
  const where: FilterNode = { node: 'anyOf', of: [nearPoint, nearCave, nearSaved, inPolygon, inArea] };
  return withAnchor(
    withSort(withWhere(emptyDocument(['feature']), 'feature', where), 'proximity', false),
    { anchor: 'point', longitude: 23.5, latitude: 45.25 },
  );
}

describe('a spatial row', () => {
  it('is finished only with a radius in range and something to measure from', () => {
    expect(isComplete(nearPoint)).toBe(true);
    expect(isComplete(nearCave)).toBe(true);
    expect(isComplete(nearSaved)).toBe(true);
    // A radius typed, nothing picked yet: the row is held back, not sent as a one-value "within".
    expect(isComplete(condition('position', 'within', [{ type: 'number', value: 500 }]))).toBe(false);
    // An anchor picked, the radius box emptied: held as an empty number in its place.
    expect(isComplete(condition('position', 'within', [
      { type: 'number', value: null as unknown as number }, { type: 'point', longitude: 1, latitude: 1 },
    ]))).toBe(false);
    expect(isSpatialComplete('within', [{ type: 'number', value: 0 }, { type: 'savedFilter', id: SAVED }])).toBe(false);
    // Past the ceiling is written, not unfinished: the server refuses it and names the limit.
    expect(isSpatialComplete('within', [{ type: 'number', value: 200_001 }, { type: 'savedFilter', id: SAVED }])).toBe(true);
  });

  it('reads the radius ceiling it is given, the shipped one by default', () => {
    expect(isRadius(DEFAULT_MAX_RADIUS_METERS)).toBe(true);
    expect(isRadius(DEFAULT_MAX_RADIUS_METERS + 1)).toBe(false);
    expect(isRadius(300_000, 500_000)).toBe(true);
    expect(isRadius(500_001, 500_000)).toBe(false);
    expect(isRadius(0.5, 500_000)).toBe(false);
  });

  it('inside takes exactly one outline: a polygon or an area', () => {
    expect(isComplete(inPolygon)).toBe(true);
    expect(isComplete(inArea)).toBe(true);
    expect(isComplete(condition('position', 'inside', []))).toBe(false);
    expect(isSpatialComplete('inside', [{ type: 'point', longitude: 1, latitude: 1 }])).toBe(false);
  });
});

describe('a pasted polygon', () => {
  it('reads a Polygon, a Feature holding one, and a collection of exactly one', () => {
    const geometry = { type: 'Polygon', coordinates: [RING] };
    expect(parsePolygon(JSON.stringify(geometry))).toEqual({ coordinates: [RING] });
    expect(parsePolygon(JSON.stringify({ type: 'Feature', properties: {}, geometry }))).toEqual({ coordinates: [RING] });
    expect(parsePolygon(JSON.stringify({
      type: 'FeatureCollection', features: [{ type: 'Feature', properties: {}, geometry }],
    }))).toEqual({ coordinates: [RING] });
  });

  it('drops a height, and writes back as the geometry it was read from', () => {
    const withHeights = RING.map(([x, y]) => [x, y, 812]);
    const read = parsePolygon(JSON.stringify({ type: 'Polygon', coordinates: [withHeights] }));
    expect(read).toEqual({ coordinates: [RING] });
    expect(parsePolygon(polygonText([RING]))).toEqual({ coordinates: [RING] });
  });

  it('says why it could not be read rather than guessing which shape was meant', () => {
    expect(parsePolygon('{not json')).toEqual({ problem: 'notJson' });
    expect(parsePolygon(JSON.stringify({ type: 'Point', coordinates: [1, 2] }))).toEqual({ problem: 'notPolygon' });
    expect(parsePolygon(JSON.stringify({ type: 'FeatureCollection', features: [] }))).toEqual({ problem: 'notPolygon' });
    expect(parsePolygon(JSON.stringify({ type: 'Polygon', coordinates: [RING.slice(0, 3)] }))).toEqual({ problem: 'notPolygon' });
    expect(parsePolygon(JSON.stringify({ type: 'Polygon', coordinates: [RING.slice(0, 3).concat([[23, 46]])] })))
      .toEqual({ problem: 'notClosed' });
    expect(parsePolygon(JSON.stringify({ type: 'Polygon', coordinates: [[[200, 45], ...RING.slice(1, 3), [200, 45]]] })))
      .toEqual({ problem: 'offGlobe' });
    const huge = Array.from({ length: 2001 }, (_, i) => [23 + i / 1e5, 45 + (i % 2) / 1e3]);
    huge.push(huge[0]);
    expect(parsePolygon(JSON.stringify({ type: 'Polygon', coordinates: [huge] }))).toEqual({ problem: 'tooLarge' });
  });
});

describe('what may be saved', () => {
  it('a typed point, a pasted outline or a nearest-first anchor is a position, and is never saved', () => {
    const empty = emptyDocument(['feature']);
    expect(holdsPosition(withWhere(empty, 'feature', nearPoint))).toBe(true);
    expect(holdsPosition(withWhere(empty, 'feature', { node: 'not', of: inPolygon }))).toBe(true);
    expect(holdsPosition(withAnchor(empty, { anchor: 'object', world: 'feature', id: CAVE }))).toBe(true);
    // Names only: an object, an area or another saved filter is looked up afresh on every run.
    expect(holdsPosition(withWhere(empty, 'feature', fromGroups([[nearCave, nearSaved, inArea]])))).toBe(false);
  });

  it('taking the anchor off leaves the document exactly as it was without one', () => {
    const empty = emptyDocument(['feature']);
    expect(withoutAnchor(withAnchor(empty, { anchor: 'point', longitude: 1, latitude: 2 }))).toEqual(empty);
    expect(withoutAnchor(empty)).toBe(empty);
    expect('anchor' in withAnchor(empty, null)).toBe(false);
  });
});

describe('a spatial filter as a link', () => {
  it('survives the round trip, anchor and all', () => {
    const document = everySpatialValue();
    expect(decodeFilter(encodeFilter(document))).toEqual(document);
  });

  it('survives the cave registry reading it back from its address', () => {
    // A spatial row is not one of the registry's own controls, so it is kept as written beside them.
    const reading = readCaveRegistry(caveRegistryDocument(
      { search: 'urs' },
      [nearCave, inPolygon],
    )!);
    expect(reading?.controls.search).toBe('urs');
    expect(reading?.extra).toEqual([nearCave, inPolygon]);
  });
});

describe('what an anchor is picked from', () => {
  it('offers caves, entrances and features in the feature world, and areas for an outline', () => {
    expect(anchorScopes('object').map((s) => s.id)).toEqual(['caves', 'entrances', 'features']);
    expect(anchorScopes('area').map((s) => s.id)).toEqual(['areas', 'features']);
    expect(anchorScopes('area')[0].where).toEqual({ field: 'category', value: 'area' });
    // Whichever world the row filters, what it measures from is a feature.
    expect([...anchorScopes('object'), ...anchorScopes('area')].every((s) => s.world === 'feature')).toBe(true);
  });
});

describe('the spatial wire shape', () => {
  it('spells every spatial value and the anchor exactly the way the server reads them', () => {
    // The third literal in the server's FilterWireContractTests. Each value says what kind it is
    // with a discriminator the generated contract cannot see, so it is pinned on both sides.
    expect(JSON.stringify(everySpatialValue())).toBe(
      '{"version":1,"scope":[{"world":"feature","where":{"node":"anyOf","of":['
      + '{"node":"condition","field":"position","op":"within","values":[{"type":"number","value":500},{"type":"point","longitude":23.5,"latitude":45.25}]},'
      + '{"node":"condition","field":"position","op":"within","values":[{"type":"number","value":200},{"type":"object","world":"feature","id":"0b3f6a52-1d7e-4c5a-9a61-3f2d9c4e8b10"}]},'
      + '{"node":"condition","field":"position","op":"within","values":[{"type":"number","value":1000},{"type":"savedFilter","id":"5c1e2d3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f"}]},'
      + '{"node":"condition","field":"position","op":"inside","values":[{"type":"polygon","coordinates":[[[23,45],[23.01,45],[23.01,45.01],[23,45]]]}]},'
      + '{"node":"condition","field":"position","op":"inside","values":[{"type":"object","world":"feature","id":"7d2c9e14-6b3a-4f58-a1c2-0e9d8b7a6f54"}]}'
      + ']}}],"sort":"proximity","descending":false,"anchor":{"anchor":"point","longitude":23.5,"latitude":45.25}}',
    );
  });
});
