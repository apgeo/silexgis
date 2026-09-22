// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { ResLink, ResLinkMember } from '../api/hooks.ts';
import { mapPinsFromLinks, stationMarkers, supersededPointCount } from './mapPoints.ts';

const MODEL = 'model-1';
const DOC = 'doc-1';
const FILE = 'file-1';

function member(overrides: Partial<ResLinkMember>): ResLinkMember {
  return {
    id: 'member-1',
    targetType: 'document',
    targetId: DOC,
    isMain: false,
    sortOrder: 0,
    note: null,
    anchorKind: 'whole',
    anchor: null,
    anchorFileId: null,
    anchorState: 'exact',
    display: null,
    ...overrides,
  } as ResLinkMember;
}

let linkCounter = 0;

function link(
  members: ResLinkMember[],
  relationCode: string | null = 'map-station-point',
  overrides: Partial<{ id: string; createdAt: string }> = {},
): ResLink {
  linkCounter += 1;
  return {
    id: overrides.id ?? `link-${linkCounter}`,
    shortCode: 'ABCD1234',
    relationType:
      relationCode === null
        ? null
        : { id: 2, code: relationCode, name: relationCode, directed: true, inverseName: 'x' },
    description: null,
    createdAt: overrides.createdAt ?? '2026-09-01T10:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    mayEdit: true,
    members,
  } as unknown as ResLink;
}

const pointMember = (
  x = 0.25,
  y = 0.75,
  overrides: Partial<ResLinkMember> = {},
): ResLinkMember =>
  member({
    id: `point-${x}-${y}`,
    anchorKind: 'imageRegion',
    anchor: { shape: 'point', x, y } as unknown as ResLinkMember['anchor'],
    anchorFileId: FILE,
    ...overrides,
  });

const stationMember = (station: string) =>
  member({
    id: `station-${station}`,
    targetType: 'surveyModel',
    targetId: MODEL,
    anchorKind: 'modelStation',
    anchor: { station } as unknown as ResLinkMember['anchor'],
  });

describe('mapPinsFromLinks', () => {
  it('reads a pin out of a pin-coded link pairing a region of this document with a station', () => {
    const pins = mapPinsFromLinks([link([pointMember(), stationMember('p.g.7')])], MODEL, DOC);

    expect(pins).toHaveLength(1);
    expect(pins[0]).toMatchObject({
      station: 'p.g.7',
      region: { shape: 'point', x: 0.25, y: 0.75 },
      anchorFileId: FILE,
    });
  });

  it('requires the code: the same pair under another relation stays an annotation', () => {
    const pair = [pointMember(), stationMember('p.g.7')];

    // Positive twin: under the pin code this exact pair IS a pin…
    expect(mapPinsFromLinks([link(pair, 'map-station-point')], MODEL, DOC)).toHaveLength(1);

    // …and under a generic relation, a map-of code, or no relation, it is not: structure
    // alone must not promote a casual region↔station link into a position claim.
    for (const code of ['same-object', 'documents', 'map-plan-of', null]) {
      expect(mapPinsFromLinks([link(pair, code)], MODEL, DOC)).toHaveLength(0);
    }
  });

  it('requires the structural pair: the code alone must not make a marker host', () => {
    // Positive twin: code plus the pair is a pin.
    expect(
      mapPinsFromLinks([link([pointMember(), stationMember('p.g.7')])], MODEL, DOC),
    ).toHaveLength(1);

    // The code with no station member folds to nothing…
    expect(mapPinsFromLinks([link([pointMember()])], MODEL, DOC)).toHaveLength(0);
    // …as does the code with no region member…
    expect(mapPinsFromLinks([link([stationMember('p.g.7')])], MODEL, DOC)).toHaveLength(0);
    // …and a whole-document member instead of a region.
    expect(
      mapPinsFromLinks([link([member({}), stationMember('p.g.7')])], MODEL, DOC),
    ).toHaveLength(0);
  });

  it('requires the file pin: a region without one names no coordinate space', () => {
    expect(
      mapPinsFromLinks(
        [link([pointMember(0.25, 0.75, { anchorFileId: null }), stationMember('p.g.7')])],
        MODEL,
        DOC,
      ),
    ).toHaveLength(0);
  });

  it('folds only this document and this model', () => {
    const links = [link([pointMember(), stationMember('p.g.7')])];

    // Positive twin: the very same links fold to a pin for the pair they name.
    expect(mapPinsFromLinks(links, MODEL, DOC)).toHaveLength(1);
    // Another document's fold does not see this pin, nor does another model's.
    expect(mapPinsFromLinks(links, MODEL, 'doc-2')).toHaveLength(0);
    expect(mapPinsFromLinks(links, 'model-2', DOC)).toHaveLength(0);
  });

  it('drops a pin whose region payload is withheld or unreadable, never guessing', () => {
    expect(
      mapPinsFromLinks(
        [
          link([
            member({ anchorKind: 'imageRegion', anchor: null, anchorFileId: FILE }),
            stationMember('p.g.7'),
          ]),
        ],
        MODEL,
        DOC,
      ),
    ).toHaveLength(0);
  });

  it('keeps region-shaped pins in the fold — they are pins, just never markers', () => {
    const rect = member({
      id: 'rect-member',
      anchorKind: 'imageRegion',
      anchor: { shape: 'rect', x: 0.1, y: 0.1, w: 0.2, h: 0.2 } as unknown as ResLinkMember['anchor'],
      anchorFileId: FILE,
    });
    const pins = mapPinsFromLinks([link([rect, stationMember('p.g.7')])], MODEL, DOC);

    expect(pins).toHaveLength(1);
    expect(pins[0].region.shape).toBe('rect');
  });
});

describe('stationMarkers', () => {
  it('draws a point pin measured against the file on screen', () => {
    const pins = mapPinsFromLinks([link([pointMember(), stationMember('p.g.7')])], MODEL, DOC);

    expect(stationMarkers(pins, FILE)).toEqual([
      {
        station: 'p.g.7',
        x: 0.25,
        y: 0.75,
        linkId: pins[0].linkId,
        memberId: pins[0].memberId,
      },
    ]);
  });

  it('draws only point-shaped pins: a region hosts no marker', () => {
    const rect = member({
      id: 'rect-member',
      anchorKind: 'imageRegion',
      anchor: { shape: 'rect', x: 0.1, y: 0.1, w: 0.2, h: 0.2 } as unknown as ResLinkMember['anchor'],
      anchorFileId: FILE,
    });
    const pins = mapPinsFromLinks(
      [link([rect, stationMember('p.g.8')]), link([pointMember(), stationMember('p.g.7')])],
      MODEL,
      DOC,
    );

    // The rect pin exists in the fold (its positive twin) and hosts no marker (the absence).
    expect(pins).toHaveLength(2);
    expect(stationMarkers(pins, FILE).map((m) => m.station)).toEqual(['p.g.7']);
  });

  it('draws only pins measured against the file on screen', () => {
    const pins = mapPinsFromLinks(
      [
        link([pointMember(0.2, 0.2), stationMember('p.g.7')]),
        link([pointMember(0.6, 0.6, { id: 'stale', anchorFileId: 'file-0' }), stationMember('p.g.8')]),
      ],
      MODEL,
      DOC,
    );

    // The current file draws its own pin; the superseded-scan pin is a count, not a marker
    // in a right-looking place it was never measured at.
    expect(stationMarkers(pins, FILE).map((m) => m.station)).toEqual(['p.g.7']);
    expect(supersededPointCount(pins, FILE)).toBe(1);

    // And the same pin IS drawable on the file it was measured against — the twin that
    // proves the filter selects by file rather than dropping the pin outright.
    expect(stationMarkers(pins, 'file-0').map((m) => m.station)).toEqual(['p.g.8']);
  });

  it('gives a twice-pinned station its newest pin', () => {
    const pins = mapPinsFromLinks(
      [
        link([pointMember(0.1, 0.1), stationMember('p.g.7')], 'map-station-point', {
          id: 'older',
          createdAt: '2026-09-01T10:00:00Z',
        }),
        link([pointMember(0.9, 0.9), stationMember('p.g.7')], 'map-station-point', {
          id: 'newer',
          createdAt: '2026-09-02T10:00:00Z',
        }),
      ],
      MODEL,
      DOC,
    );

    const markers = stationMarkers(pins, FILE);
    expect(markers).toHaveLength(1);
    expect(markers[0]).toMatchObject({ linkId: 'newer', x: 0.9, y: 0.9 });
  });

  it('breaks a created-at tie deterministically, whatever order the pages arrived in', () => {
    const at = '2026-09-01T10:00:00Z';
    const a = link([pointMember(0.1, 0.1), stationMember('p.g.7')], 'map-station-point', {
      id: 'link-a',
      createdAt: at,
    });
    const b = link([pointMember(0.9, 0.9), stationMember('p.g.7')], 'map-station-point', {
      id: 'link-b',
      createdAt: at,
    });

    const oneWay = stationMarkers(mapPinsFromLinks([a, b], MODEL, DOC), FILE);
    const otherWay = stationMarkers(mapPinsFromLinks([b, a], MODEL, DOC), FILE);

    expect(oneWay).toEqual(otherWay);
    expect(oneWay[0].linkId).toBe('link-b');
  });
});

describe('supersededPointCount', () => {
  it('counts nothing when every point was measured against the file on screen', () => {
    const pins = mapPinsFromLinks([link([pointMember(), stationMember('p.g.7')])], MODEL, DOC);

    expect(supersededPointCount(pins, FILE)).toBe(0);
  });

  it('counts points only: a superseded rect region is not a mislocated point', () => {
    const staleRect = member({
      id: 'stale-rect',
      anchorKind: 'imageRegion',
      anchor: { shape: 'rect', x: 0.1, y: 0.1, w: 0.2, h: 0.2 } as unknown as ResLinkMember['anchor'],
      anchorFileId: 'file-0',
    });
    const pins = mapPinsFromLinks(
      [
        link([staleRect, stationMember('p.g.8')]),
        link([pointMember(0.6, 0.6, { id: 'stale-point', anchorFileId: 'file-0' }), stationMember('p.g.9')]),
      ],
      MODEL,
      DOC,
    );

    expect(supersededPointCount(pins, FILE)).toBe(1);
  });
});
