// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { PublicTripRasterMap } from '../api/hooks.ts';
import { restampPublishedSheets, sheetsFromEnvelope } from './publishedSheets.ts';

const sheet = (over: Partial<PublicTripRasterMap> = {}): PublicTripRasterMap => ({
  title: 'North branch',
  viewKind: 'plan',
  imageUrl: '/api/v1/files/f1/thumbnail?size=1200&token=first',
  points: [{ station: 'cave.upper.2', x: 0.25, y: 0.75 }],
  ...over,
});

/**
 * The envelope's sheets, shaped for the panes — and nothing decided along the way.
 *
 * The server resolved everything a signed-in fold resolves (which rendering, which points on
 * it, duplicates); what these hold down is that the shaping neither filters nor invents: every
 * sheet sent is a sheet shaped, every point sent is a marker, and the identities synthesized
 * for the drawing layer say only what a public page can truthfully say.
 */
describe('sheetsFromEnvelope', () => {
  it('shapes each sheet with its points as read-only markers', () => {
    const sheets = sheetsFromEnvelope([sheet()]);

    expect(sheets).toHaveLength(1);
    expect(sheets[0].title).toBe('North branch');
    expect(sheets[0].viewKind).toBe('plan');
    expect(sheets[0].imageUrl).toBe('/api/v1/files/f1/thumbnail?size=1200&token=first');
    expect(sheets[0].markers).toEqual([
      // The envelope names no links, so the marker's identities are the station itself and
      // an editability that is the truth for a visitor: none.
      { station: 'cave.upper.2', x: 0.25, y: 0.75, linkId: '', memberId: 'cave.upper.2', mayEdit: false },
    ]);
  });

  it('answers an empty envelope with an empty list — the ordinary trip has no sheets', () => {
    expect(sheetsFromEnvelope([])).toEqual([]);
  });

  it('keys a sheet by rendering and view, ignoring the signature that expires', () => {
    const [first] = sheetsFromEnvelope([sheet()]);
    const [resigned] = sheetsFromEnvelope([
      sheet({ imageUrl: '/api/v1/files/f1/thumbnail?size=1200&token=second' }),
    ]);
    const [otherView] = sheetsFromEnvelope([sheet({ viewKind: 'profile' })]);

    expect(resigned.key).toBe(first.key);
    expect(otherView.key).not.toBe(first.key);
  });

  it('reads an unknown view-kind spelling as other, never as a crash', () => {
    const [sent] = sheetsFromEnvelope([
      sheet({ viewKind: 'extended-elevation' as PublicTripRasterMap['viewKind'] }),
    ]);
    expect(sent.viewKind).toBe('other');
  });
});

/**
 * The restamp: the pictures' answer to the same problem, applied to sheets. A published page
 * re-signs every URL on every poll; while a read is the same sheets, the held array keeps its
 * identity and only the signatures move, so nothing downstream rebuilds for a poll that
 * changed nothing — and a tab not yet opened reads the freshest signature when it mounts.
 */
describe('restampPublishedSheets', () => {
  it('keeps the held array and restamps the image URL when only the signature changed', () => {
    const held = sheetsFromEnvelope([sheet()]);
    const fresh = sheetsFromEnvelope([
      sheet({ imageUrl: '/api/v1/files/f1/thumbnail?size=1200&token=second' }),
    ]);

    const answer = restampPublishedSheets(held, fresh);

    expect(answer).toBe(held);
    expect(answer[0].imageUrl).toBe('/api/v1/files/f1/thumbnail?size=1200&token=second');
  });

  it('hands over a fresh array when the points moved — that is a change a reader is owed', () => {
    const held = sheetsFromEnvelope([sheet()]);
    const fresh = sheetsFromEnvelope([
      sheet({ points: [{ station: 'cave.upper.2', x: 0.5, y: 0.75 }] }),
    ]);

    expect(restampPublishedSheets(held, fresh)).toBe(fresh);
  });

  it('hands over a fresh array when a sheet arrived, left, or changed its rendering', () => {
    const held = sheetsFromEnvelope([sheet()]);

    const grown = sheetsFromEnvelope([sheet(), sheet({ viewKind: 'profile' })]);
    expect(restampPublishedSheets(held, grown)).toBe(grown);

    const gone = sheetsFromEnvelope([]);
    expect(restampPublishedSheets(held, gone)).toBe(gone);

    // A new current file is a different picture: the pane must reload it, so the identity
    // must break — restamping it in place would draw old points over a new scan.
    const rescanned = sheetsFromEnvelope([
      sheet({ imageUrl: '/api/v1/files/f2/thumbnail?size=1200&token=first', points: [] }),
    ]);
    expect(restampPublishedSheets(held, rescanned)).toBe(rescanned);
  });

  it('adopts the first derivation whole', () => {
    const fresh = sheetsFromEnvelope([sheet()]);
    expect(restampPublishedSheets(undefined, fresh)).toBe(fresh);
  });
});
