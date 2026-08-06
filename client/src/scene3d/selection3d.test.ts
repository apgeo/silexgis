// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  pickMatchesSelection,
  pickPayload,
  samePickTarget,
  selectionFromPick,
} from './selection3d.ts';

describe('selectionFromPick', () => {
  it('selects the entrance that was clicked, and the cave it belongs to', () => {
    expect(
      selectionFromPick({ id: { kind: 'entrance', entranceId: 'e1', caveId: 'c1' } }),
    ).toEqual({ kind: 'entrance', entranceId: 'e1', caveId: 'c1' });
  });

  it('selects the feature that was clicked', () => {
    expect(selectionFromPick({ id: { kind: 'feature', featureId: 'f1' } })).toEqual({
      kind: 'feature',
      featureId: 'f1',
    });
  });

  it('turns a survey line into its cave, which is what a viewer clicking one means', () => {
    // There is no selection kind for a survey line and there should not be: the detail panel has
    // nothing to say about one line of a cave that it does not say better about the cave.
    expect(
      selectionFromPick({ id: { kind: 'centerline', caveId: 'c1', centerlineId: 'l1' } }),
    ).toEqual({ kind: 'cave', caveId: 'c1' });
  });

  it('carries a cluster with the zoom it was summed at, so its members can be listed', () => {
    expect(
      selectionFromPick({ id: { kind: 'cluster', lon: 25.3, lat: 45.5, count: 7, zoom: 7 } }),
    ).toEqual({ kind: 'cluster', lon: 25.3, lat: 45.5, count: 7, zoom: 7 });
  });

  it('clears the selection for a click that found nothing, or only bare ground', () => {
    expect(selectionFromPick(null)).toBeNull();
    expect(
      selectionFromPick({ id: undefined, position: { longitude: 25, latitude: 45, height: 700 } }),
    ).toBeNull();
  });

  it('drops nothing of its own into the store beyond the references it was given', () => {
    // The store's contract is bare references. Anything a payload carries for the scene's own use
    // has to be left behind here rather than ending up persisted in a selection.
    const selection = selectionFromPick({
      id: { kind: 'entrance', entranceId: 'e1', caveId: 'c1', label: 'Main entrance' },
    });

    expect(Object.keys(selection!).sort()).toEqual(['caveId', 'entranceId', 'kind']);
  });
});

describe('pickPayload', () => {
  it('reads nothing from something that is not one of ours', () => {
    expect(pickPayload({ id: 'a string' })).toBeUndefined();
    expect(pickPayload({ id: { kind: 'model', modelId: 'm1' } })).toBeUndefined();
    expect(pickPayload({ id: null })).toBeUndefined();
    expect(pickPayload(null)).toBeUndefined();
  });

  it('refuses a payload of the right kind carrying the wrong fields', () => {
    // Anything at all can end up in a scene, and a click handler that trusted the discriminator
    // alone would put a half-built selection into the store rather than ignoring the click.
    expect(pickPayload({ id: { kind: 'entrance', entranceId: 'e1' } })).toBeUndefined();
    expect(pickPayload({ id: { kind: 'centerline', caveId: 'c1' } })).toBeUndefined();
    expect(
      pickPayload({ id: { kind: 'cluster', lon: 25, lat: 45, count: 7 } }),
    ).toBeUndefined();
  });

  it('carries the name and the place chrome over the scene is drawn from', () => {
    expect(
      pickPayload({
        id: {
          kind: 'entrance',
          entranceId: 'e1',
          caveId: 'c1',
          label: 'Intrarea Mică',
          anchor: { longitude: 25, latitude: 45, height: -3 },
        },
      }),
    ).toEqual({
      kind: 'entrance',
      entranceId: 'e1',
      caveId: 'c1',
      label: 'Intrarea Mică',
      anchor: { longitude: 25, latitude: 45, height: -3 },
    });
  });

  it('drops a name or a place that is not one, rather than putting it on the screen', () => {
    // Everything here is checked rather than trusted for the same reason the ids are: whatever a
    // later batch attaches to an item ends up in this function, and a half-built one must read as
    // a pick with nothing to say — not as a label reading "undefined" pinned to nowhere.
    const payload = pickPayload({
      id: {
        kind: 'feature',
        featureId: 'f1',
        label: 42,
        anchor: { longitude: 25, latitude: 'north' },
      },
    });

    expect(payload).toEqual({ kind: 'feature', featureId: 'f1' });
  });

  it('treats an empty name as no name', () => {
    expect(pickPayload({ id: { kind: 'feature', featureId: 'f1', label: '' } })).toEqual({
      kind: 'feature',
      featureId: 'f1',
    });
  });
});

describe('pickMatchesSelection', () => {
  it('agrees when the selection is still what the pick found', () => {
    expect(
      pickMatchesSelection(
        { kind: 'entrance', entranceId: 'e1', caveId: 'c1' },
        { kind: 'entrance', entranceId: 'e1', caveId: 'c1' },
      ),
    ).toBe(true);
    expect(
      pickMatchesSelection(
        { kind: 'feature', featureId: 'f1' },
        { kind: 'feature', featureId: 'f1' },
      ),
    ).toBe(true);
  });

  it('agrees that a survey line and the cave it belongs to are the same thing', () => {
    // The two are one selection and two different picks, which is exactly why this is not a
    // comparison of what was clicked.
    expect(
      pickMatchesSelection(
        { kind: 'centerline', caveId: 'c1', centerlineId: 'line-1' },
        { kind: 'cave', caveId: 'c1' },
      ),
    ).toBe(true);
    expect(
      pickMatchesSelection(
        { kind: 'centerline', caveId: 'c1', centerlineId: 'line-1' },
        { kind: 'cave', caveId: 'c2' },
      ),
    ).toBe(false);
  });

  it('holds a cluster only while the same summed patch of ground is selected', () => {
    const cluster = { kind: 'cluster', lon: 25, lat: 45, count: 7, zoom: 9 } as const;
    expect(pickMatchesSelection(cluster, { ...cluster })).toBe(true);
    // The zoom fixed the size of the cell that was summed, so the same coordinates at another
    // zoom are a different set of caves.
    expect(pickMatchesSelection(cluster, { ...cluster, zoom: 10 })).toBe(false);
  });

  it('disagrees when there is nothing selected, or nothing was picked', () => {
    expect(pickMatchesSelection({ kind: 'feature', featureId: 'f1' }, null)).toBe(false);
    expect(pickMatchesSelection(undefined, { kind: 'feature', featureId: 'f1' })).toBe(false);
  });

  it('disagrees across kinds that happen to share an identifier', () => {
    expect(
      pickMatchesSelection({ kind: 'feature', featureId: 'x' }, { kind: 'cave', caveId: 'x' }),
    ).toBe(false);
  });
});

describe('samePickTarget', () => {
  it('recognises the same thing through a name and a place that have both changed', () => {
    // What a reload does to every payload: the name is composed again from whatever the server now
    // says, and the anchor comes from wherever the thing now is. Neither can be part of the test,
    // or a label would lose its own thing at exactly the moment it needs to find it again.
    expect(
      samePickTarget(
        { kind: 'feature', featureId: 'f1', label: 'Doline veche', anchor: at(25, 45) },
        { kind: 'feature', featureId: 'f1', label: 'Doline nouă', anchor: at(26, 46) },
      ),
    ).toBe(true);
  });

  it('tells two of a kind apart', () => {
    expect(
      samePickTarget(
        { kind: 'entrance', entranceId: 'e1', caveId: 'c1' },
        { kind: 'entrance', entranceId: 'e2', caveId: 'c1' },
      ),
    ).toBe(false);
  });

  it('holds a survey line to its own line rather than to its cave', () => {
    // A cave is drawn as many lines and each is its own pick; a label on one of them belongs to
    // that one, however many others name the same cave.
    const line = { kind: 'centerline', caveId: 'c1', centerlineId: 'line-1' } as const;
    expect(samePickTarget(line, { ...line })).toBe(true);
    expect(samePickTarget(line, { ...line, centerlineId: 'line-2' })).toBe(false);
  });

  it('holds a cluster to the patch of ground and the zoom that summed it', () => {
    const cluster = { kind: 'cluster', lon: 25, lat: 45, count: 7, zoom: 9 } as const;
    // The count is not part of it: a cluster whose count changed is still the same cell.
    expect(samePickTarget(cluster, { ...cluster, count: 8 })).toBe(true);
    expect(samePickTarget(cluster, { ...cluster, zoom: 10 })).toBe(false);
    expect(samePickTarget(cluster, { ...cluster, lat: 45.1 })).toBe(false);
  });

  it('disagrees across kinds that happen to share an identifier', () => {
    expect(
      samePickTarget(
        { kind: 'feature', featureId: 'x' },
        { kind: 'centerline', caveId: 'x', centerlineId: 'x' },
      ),
    ).toBe(false);
  });
});

function at(longitude: number, latitude: number) {
  return { longitude, latitude, height: 0 };
}
