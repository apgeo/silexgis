// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { MapLayerInfo, TripLogInfo } from '../../api/hooks.ts';
import { documentBasemap, tripReportMapContent, type CaveAsRead } from './tripReportMap.ts';

const OPEN = '11111111-1111-1111-1111-111111111111';
const WITHHELD = '22222222-2222-2222-2222-222222222222';
const COARSE = '33333333-3333-3333-3333-333333333333';
const ELSEWHERE = '44444444-4444-4444-4444-444444444444';
const UNANSWERED = '55555555-5555-5555-5555-555555555555';

const sketch = { type: 'LineString', coordinates: [[25.4, 45.5], [25.41, 45.51]] };
const meeting = { type: 'Point', coordinates: [25.38, 45.47] };

function trip(overrides: Partial<Pick<TripLogInfo, 'geom' | 'meetingGeom' | 'caveIds'>> = {}) {
  return { geom: null, meetingGeom: null, caveIds: [], ...overrides } as Pick<
    TripLogInfo,
    'geom' | 'meetingGeom' | 'caveIds'
  >;
}

function cave(overrides: Partial<CaveAsRead>): CaveAsRead {
  return { name: 'Peștera Mare', geom: null, approximateLocation: false, ...overrides };
}

/**
 * The picture is drawn by the reader's own browser, so what it may show is whatever the server
 * already told that reader and not a position more. These cases hold the function that decides
 * what is drawn to exactly that: it reads the answers it is handed and adds nothing to them.
 */
describe('what a write-up’s map shows', () => {
  it('draws the trip’s own shapes exactly as the trip’s answer carried them', () => {
    const content = tripReportMapContent(
      trip({ geom: sketch as never, meetingGeom: meeting as never }),
      new Map(),
    );

    expect(content?.shapes).toEqual([
      { kind: 'sketch', geometry: sketch },
      { kind: 'meeting', geometry: meeting },
    ]);
    // The very objects, not copies worked over on the way: nothing between the answer and the
    // picture rounds, shifts or rebuilds a position.
    expect(content?.shapes[0].geometry).toBe(sketch);
    expect(content?.shapes[1].geometry).toBe(meeting);
  });

  it('draws a cave the trip names where that cave’s own answer says it is, under its name', () => {
    const position = { type: 'Point', coordinates: [25.43917, 45.52064] };
    const content = tripReportMapContent(
      trip({ caveIds: [OPEN] }),
      new Map([[OPEN, cave({ name: 'Avenul din Grind', geom: position })]]),
    );

    expect(content?.shapes).toEqual([{ kind: 'cave', geometry: position, label: 'Avenul din Grind' }]);
    expect(content?.shapes[0].geometry).toBe(position);
  });

  /**
   * The three ways a position can be missing from what this reader holds, each next to a cave
   * that is drawn — so that drawing nothing for them is a rule, and not a function that had
   * stopped drawing caves.
   */
  it('draws nothing for a position that was withheld, coarsened or never answered', () => {
    const position = { type: 'Point', coordinates: [25.43917, 45.52064] };
    const content = tripReportMapContent(
      trip({ caveIds: [OPEN, WITHHELD, COARSE, UNANSWERED] }),
      new Map<string, CaveAsRead | undefined>([
        [OPEN, cave({ name: 'Drawn', geom: position })],
        // Readable, and its position kept back: the answer carries none.
        [WITHHELD, cave({ name: 'Withheld', geom: null })],
        // A position snapped to the protection grid. Beside the trip's exact sketch it would be
        // a precise-looking mark in the wrong place.
        [COARSE, cave({ name: 'Coarse', geom: { type: 'Point', coordinates: [25.5, 45.5] }, approximateLocation: true })],
        // The page asked and has no answer.
        [UNANSWERED, undefined],
      ]),
    );

    expect(content?.shapes.map((shape) => shape.label)).toEqual(['Drawn']);
  });

  /**
   * The trip's own list is the only thing walked. The page may well hold an answer for another
   * cave — the reader opened it a minute ago — and that cave is not on this trip's map: which
   * caves a trip names, for this reader, is the server's list and nobody else's.
   */
  it('never draws a cave the trip’s own answer did not name, however much is held about it', () => {
    const content = tripReportMapContent(
      trip({ geom: sketch as never, caveIds: [] }),
      new Map([[ELSEWHERE, cave({ name: 'Not on this trip', geom: { type: 'Point', coordinates: [25.6, 45.6] } })]]),
    );

    expect(content?.shapes).toEqual([{ kind: 'sketch', geometry: sketch }]);
  });

  it('draws nothing for a cave whose position is not a position', () => {
    const content = tripReportMapContent(
      trip({ caveIds: [OPEN, WITHHELD, COARSE] }),
      new Map([
        [OPEN, cave({ geom: { type: 'Point', coordinates: [] } })],
        [WITHHELD, cave({ geom: { type: 'Point', coordinates: [Number.NaN, 45.5] } })],
        [COARSE, cave({ geom: { type: 'LineString', coordinates: [25.4, 45.5] } })],
      ]),
    );

    expect(content).toBeNull();
  });

  it('has nothing to show for a trip that places nothing', () => {
    expect(tripReportMapContent(trip(), new Map())).toBeNull();
    // A shape with no positions in it is no shape.
    expect(
      tripReportMapContent(trip({ geom: { type: 'LineString', coordinates: [] } as never }), new Map()),
    ).toBeNull();
  });
});

function layer(overrides: Partial<MapLayerInfo> & { id: number }): MapLayerInfo {
  return {
    name: `Layer ${overrides.id}`,
    layerKind: 'xyz',
    urlTemplate: `https://tiles.example.invalid/${overrides.id}/{z}/{x}/{y}.png`,
    options: null,
    attribution: '© Somebody',
    groupName: null,
    minZoom: 0,
    maxZoom: 19,
    isBase: true,
    isDefault: false,
    sortOrder: overrides.id,
    inDocuments: false,
    ...overrides,
  };
}

/**
 * Which background a document's map may be drawn over is the catalogue's own statement, source
 * by source. Nothing here guesses from an address or a name.
 */
describe('the background a document’s map is drawn over', () => {
  it('is the default background when the catalogue lets a document copy it', () => {
    const catalog = [
      layer({ id: 1, inDocuments: true }),
      layer({ id: 2, inDocuments: true, isDefault: true }),
    ];
    expect(documentBasemap(catalog)?.id).toBe(2);
  });

  it('is the first copyable one in the catalogue’s order when the default may not be copied', () => {
    const catalog = [
      layer({ id: 5, sortOrder: 30, inDocuments: true }),
      layer({ id: 9, sortOrder: 10, isDefault: true }),
      layer({ id: 7, sortOrder: 20, inDocuments: true }),
    ];
    expect(documentBasemap(catalog)?.id).toBe(7);
  });

  /**
   * The cases that must never be chosen, each with a copyable-looking property the others lack.
   * A source the catalogue did not mark is the one that matters most: it is every source whose
   * terms nobody read, and the ones whose terms forbid it.
   */
  it('is never a source the catalogue did not mark, an overlay, an uncredited one or another kind', () => {
    expect(
      documentBasemap([
        layer({ id: 1, isDefault: true }),
        layer({ id: 2, inDocuments: true, isBase: false }),
        layer({ id: 3, inDocuments: true, attribution: null }),
        layer({ id: 4, inDocuments: true, attribution: '   ' }),
        layer({ id: 5, inDocuments: true, layerKind: 'wms' }),
      ]),
    ).toBeNull();
  });

  it('is nothing when there is no catalogue to choose from', () => {
    expect(documentBasemap(undefined)).toBeNull();
    expect(documentBasemap([])).toBeNull();
  });
});
