// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { pickPayload, selectionFromPick } from './selection3d.ts';

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
});
