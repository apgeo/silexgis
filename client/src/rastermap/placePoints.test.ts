// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { ResLink, ResLinkMember } from '../api/hooks.ts';
import { earlierPicturePins, offPictureCount, placeMarkers, placePinsFromLinks } from './placePoints.ts';

const DOC = 'doc-1';
const FILE = 'file-1';

function member(overrides: Partial<ResLinkMember>): ResLinkMember {
  return {
    id: 'm',
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

function placeLink(
  id: string,
  options: Partial<{
    code: string;
    anchor: unknown;
    fileId: string | null;
    state: 'exact' | 'degraded';
    name: string | null;
    places: number;
    mayEdit: boolean;
  }> = {},
): ResLink {
  const {
    code = 'map-place-point',
    anchor = { shape: 'point', x: 0.25, y: 0.5, px: 16, py: 24 },
    fileId = FILE,
    state = 'exact',
    name = 'Sala Mare',
    places = 1,
    mayEdit = true,
  } = options;
  return {
    id,
    shortCode: 'ABCDEFGH',
    relationType: { id: 1, code, name: 'x', directed: true, inverseName: 'x' },
    description: null,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    mayEdit,
    members: [
      member({
        id: `${id}-picture`,
        isMain: true,
        anchorKind: 'imageRegion',
        anchor: anchor as ResLinkMember['anchor'],
        anchorFileId: fileId,
        anchorState: state,
      }),
      ...Array.from({ length: places }, (_, i) =>
        member({
          id: `${id}-place-${i}`,
          targetType: 'feature',
          targetId: `place-${id}-${i}`,
          display: name === null ? null : { title: name, subtitle: null, route: null, thumbnailUrl: null },
        }),
      ),
    ],
  } as unknown as ResLink;
}

describe('the places phones pinned on a picture', () => {
  it('draws a place pin on the file it was measured on, named for the place', () => {
    const pins = placePinsFromLinks([placeLink('a')], DOC);
    expect(placeMarkers(pins, FILE, 'unnamed')).toEqual([
      { key: 'a', label: 'Sala Mare', x: 0.25, y: 0.5, place: true, linkId: 'a', placeId: 'place-a-0', mayEdit: true },
    ]);
  });

  it('names a place without a name, or one this reader is not told, plainly', () => {
    const pins = placePinsFromLinks([placeLink('a', { name: null })], DOC);
    expect(placeMarkers(pins, FILE, 'unnamed')[0].label).toBe('unnamed');
  });

  it('takes only the whole shape: the code, the picture as the main point, and one place', () => {
    const links = [
      placeLink('station', { code: 'map-station-point' }),
      placeLink('two', { places: 2 }),
      placeLink('none', { places: 0 }),
    ];
    expect(placePinsFromLinks(links, DOC)).toEqual([]);
    expect(placePinsFromLinks([placeLink('a')], 'another-document')).toEqual([]);
  });

  it('counts, and never draws, a point measured on an earlier picture', () => {
    const pins = placePinsFromLinks(
      [
        placeLink('older', { fileId: 'file-0', state: 'degraded' }),
        // A reader kept out of the document's history is not told which file; the state says it.
        placeLink('hidden', { fileId: null, state: 'degraded' }),
        placeLink('now'),
      ],
      DOC,
    );
    expect(placeMarkers(pins, FILE, 'u').map((m) => m.linkId)).toEqual(['now']);
    expect(earlierPicturePins(pins, FILE).map((p) => p.linkId)).toEqual(['older', 'hidden']);
  });

  it('counts, and never draws, a pixel that is not on the picture as drawn', () => {
    const pins = placePinsFromLinks([placeLink('off', { anchor: { shape: 'point', px: 4000, py: 3 } })], DOC);
    expect(placeMarkers(pins, FILE, 'u')).toEqual([]);
    expect(offPictureCount(pins, FILE)).toBe(1);
    expect(earlierPicturePins(pins, FILE)).toEqual([]);
  });
});
