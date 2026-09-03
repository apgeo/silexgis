// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { ResLink, ResLinkMember } from '../api/hooks.ts';
import { regionHighlightsFrom } from './regionHighlights.ts';

const DOCUMENT = '11111111-1111-4111-8111-111111111111';
const FILE = '22222222-2222-4222-8222-222222222222';
const OTHER_FILE = '33333333-3333-4333-8333-333333333333';

function member(overrides: Partial<ResLinkMember> & { id: string }): ResLinkMember {
  return {
    targetType: 'document',
    targetId: DOCUMENT,
    isMain: false,
    sortOrder: 0,
    note: null,
    anchorKind: 'imageRegion',
    anchor: { shape: 'rect', x: 0.1, y: 0.1, w: 0.2, h: 0.2 },
    anchorFileId: FILE,
    anchorState: 'exact',
    display: null,
    ...overrides,
  } as ResLinkMember;
}

function link(members: ResLinkMember[], id = 'link-1'): ResLink {
  return {
    id,
    shortCode: 'abcd1234',
    relationType: null,
    description: null,
    createdBy: null,
    createdAt: '2026-09-03T00:00:00Z',
    updatedAt: '2026-09-03T00:00:00Z',
    mayEdit: true,
    members,
  } as ResLink;
}

const noLabel = () => null;

describe('regionHighlightsFrom', () => {
  it('draws a region of this picture, and carries the link’s other members as its targets', () => {
    const highlights = regionHighlightsFrom(
      [
        link([
          member({ id: 'm1' }),
          member({ id: 'm2', targetType: 'feature', targetId: 'f1', anchorKind: 'whole', anchor: null, anchorFileId: null }),
        ]),
      ],
      DOCUMENT,
      FILE,
      noLabel,
    );

    expect(highlights).toHaveLength(1);
    expect(highlights[0].memberId).toBe('m1');
    expect(highlights[0].region).toEqual({ shape: 'rect', x: 0.1, y: 0.1, w: 0.2, h: 0.2 });
    expect(highlights[0].targets.map((target) => target.memberId)).toEqual(['m2']);
  });

  it('leaves out a region measured against a different file of the same document', () => {
    // The rule the pin exists for. A document goes on having versions uploaded, and a region
    // drawn on last year's scan would otherwise be painted over this year's — in a plausible
    // place, at full confidence, with nothing anywhere saying it was never measured there.
    const highlights = regionHighlightsFrom(
      [link([member({ id: 'm1', anchorFileId: OTHER_FILE }), member({ id: 'm2' })])],
      DOCUMENT,
      FILE,
      noLabel,
    );

    expect(highlights.map((highlight) => highlight.memberId)).toEqual(['m2']);
  });

  it('leaves out members that are not regions of this document at all', () => {
    const highlights = regionHighlightsFrom(
      [
        link([
          member({ id: 'text', anchorKind: 'textRange', anchor: { quote: 'x', start: 0, end: 1 } }),
          member({ id: 'elsewhere', targetId: 'another-document' }),
          member({ id: 'notADocument', targetType: 'geofile' }),
        ]),
      ],
      DOCUMENT,
      FILE,
      noLabel,
    );

    expect(highlights).toEqual([]);
  });

  it('draws nothing for a payload it cannot place, rather than guessing a position', () => {
    // A withheld payload and one written in pixels reach here the same way. Neither may be
    // drawn: a shape at a guessed position is worse than no shape, because it looks measured.
    const highlights = regionHighlightsFrom(
      [
        link([
          member({ id: 'withheld', anchor: null }),
          member({ id: 'pixels', anchor: { shape: 'rect', x: 10, y: 10, w: 200, h: 100 } }),
        ]),
      ],
      DOCUMENT,
      FILE,
      noLabel,
    );

    expect(highlights).toEqual([]);
  });

  it('orders the largest first, so a region inside another is painted on top of it', () => {
    const highlights = regionHighlightsFrom(
      [
        link([
          member({ id: 'small', anchor: { shape: 'rect', x: 0.4, y: 0.4, w: 0.1, h: 0.1 } }),
          member({ id: 'large', anchor: { shape: 'rect', x: 0, y: 0, w: 0.9, h: 0.9 } }),
          member({ id: 'point', anchor: { shape: 'point', x: 0.45, y: 0.45 } }),
        ]),
      ],
      DOCUMENT,
      FILE,
      noLabel,
    );

    expect(highlights.map((highlight) => highlight.memberId)).toEqual(['large', 'small', 'point']);
  });
});
