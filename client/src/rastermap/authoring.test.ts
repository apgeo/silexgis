// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import type { ImageRegion } from '../imagelink/regions.ts';
import {
  mapDeclarationBody,
  markerHit,
  pinCreateBody,
  placementPlan,
  rewritePin,
} from './authoring.ts';
import type { MapPin, MapStationMarker } from './mapPoints.ts';

const pin = (overrides: Partial<MapPin> = {}): MapPin => ({
  linkId: 'link-1',
  memberId: 'member-1',
  station: 'p.g.7',
  region: { shape: 'point', x: 0.25, y: 0.75 } as ImageRegion,
  anchorFileId: 'file-1',
  createdAt: '2026-09-01T10:00:00Z',
  mayEdit: true,
  ...overrides,
});

describe('pinCreateBody', () => {
  it('writes the designed pair in one create: point on the pinned file, station on the model', () => {
    const body = pinCreateBody(7, 'doc-1', 'file-1', { x: 0.25, y: 0.75 }, 'model-1', 'p.g.7');

    expect(body.relationTypeId).toBe(7);
    expect(body.members).toEqual([
      {
        targetType: 'document',
        targetId: 'doc-1',
        isMain: true,
        sortOrder: 0,
        note: null,
        anchorKind: 'imageRegion',
        anchor: { shape: 'point', x: 0.25, y: 0.75 },
        anchorFileId: 'file-1',
      },
      {
        targetType: 'surveyModel',
        targetId: 'model-1',
        isMain: false,
        sortOrder: 1,
        note: null,
        anchorKind: 'modelStation',
        anchor: { station: 'p.g.7' },
        anchorFileId: null,
      },
    ]);
  });

  it('marks the document member as main, so map-document editors may correct the pin', () => {
    const body = pinCreateBody(7, 'doc-1', 'file-1', { x: 0.1, y: 0.2 }, 'model-1', 's');
    const mains = body.members.filter((member) => member.isMain);
    expect(mains).toHaveLength(1);
    expect(mains[0].targetType).toBe('document');
  });
});

describe('mapDeclarationBody', () => {
  it('declares document (main, whole) onto model (whole)', () => {
    const body = mapDeclarationBody(3, 'doc-1', 'model-1');
    expect(body.relationTypeId).toBe(3);
    expect(body.members.map((m) => [m.targetType, m.anchorKind, m.isMain])).toEqual([
      ['document', 'whole', true],
      ['surveyModel', 'whole', false],
    ]);
  });
});

describe('placementPlan', () => {
  it('creates when the station holds no pin on this map', () => {
    const pins = [pin({ station: 'p.g.8' })];
    expect(placementPlan(pins, { station: 'p.g.7', replaceLinkId: null })).toEqual({
      kind: 'create',
    });
  });

  it('warns before writing a duplicate, naming the standing pin', () => {
    const standing = pin();
    expect(placementPlan([standing], { station: 'p.g.7', replaceLinkId: null })).toEqual({
      kind: 'occupied',
      pin: standing,
    });
  });

  it('treats a pin stranded on an older scan as the standing pin, not as absence', () => {
    // The re-place case reached through plain arming: fractions of another file draw no
    // marker, but a second link for the same station would still be the duplicate.
    const stranded = pin({ anchorFileId: 'file-0' });
    expect(placementPlan([stranded], { station: 'p.g.7', replaceLinkId: null })).toEqual({
      kind: 'occupied',
      pin: stranded,
    });
  });

  it('replaces without questions when the arm itself named the link to correct', () => {
    // Even with a standing pin in the list: the correction flows (re-place, move-here)
    // already decided which link the click rewrites.
    expect(placementPlan([pin()], { station: 'p.g.7', replaceLinkId: 'link-1' })).toEqual({
      kind: 'replace',
      oldLinkId: 'link-1',
    });
  });

  it('region-shaped links for the station do not count as its pin', () => {
    const area = pin({ region: { shape: 'rect', x: 0.1, y: 0.1, w: 0.2, h: 0.2 } });
    expect(placementPlan([area], { station: 'p.g.7', replaceLinkId: null })).toEqual({
      kind: 'create',
    });
  });
});

describe('rewritePin', () => {
  const body = pinCreateBody(7, 'doc-1', 'file-1', { x: 0.5, y: 0.5 }, 'model-1', 's');

  it('creates the corrected link before deleting the old one', async () => {
    const order: string[] = [];
    const create = vi.fn(async () => order.push('create'));
    const remove = vi.fn(async () => order.push('remove'));

    await rewritePin(create, remove, body, 'old-link');

    expect(order).toEqual(['create', 'remove']);
    expect(create).toHaveBeenCalledWith(body);
    expect(remove).toHaveBeenCalledWith('old-link');
  });

  it('deletes nothing when the create fails, so the station never loses its pin', async () => {
    const create = vi.fn(async () => {
      throw new Error('refused');
    });
    const remove = vi.fn(async () => undefined);

    await expect(rewritePin(create, remove, body, 'old-link')).rejects.toThrow('refused');
    expect(remove).not.toHaveBeenCalled();
  });
});

describe('markerHit', () => {
  const SIZE = { width: 4000, height: 1000 };
  const marker = (overrides: Partial<MapStationMarker> = {}): MapStationMarker => ({
    station: 'p.g.7',
    x: 0.25,
    y: 0.25,
    linkId: 'link-1',
    memberId: 'member-1',
    mayEdit: true,
    ...overrides,
  });

  it('hits the marker within the tolerance radius, in the drawn frame', () => {
    // The marker draws at [1000, 750] (the flip the drawing itself applies).
    expect(markerHit([marker()], SIZE, [1004, 753], 1, 6)?.memberId).toBe('member-1');
  });

  it('misses outside the radius, and scales the radius with the zoom', () => {
    expect(markerHit([marker()], SIZE, [1010, 750], 1, 6)).toBeNull();
    // Zoomed out (coarser resolution), the same on-screen tolerance reaches further.
    expect(markerHit([marker()], SIZE, [1010, 750], 2, 6)?.memberId).toBe('member-1');
  });

  it('answers the nearest of overlapping markers', () => {
    const near = marker({ memberId: 'near', x: 0.25, y: 0.25 });
    const far = marker({ memberId: 'far', x: 0.251, y: 0.25 });
    expect(markerHit([far, near], SIZE, [1000, 750], 1, 12)?.memberId).toBe('near');
  });
});
