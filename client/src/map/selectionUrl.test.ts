// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
import type { FeatureEnvelope } from '../api/hooks.ts';
import {
  parseSelectionParam,
  selectionFromEnvelope,
  selectionRefOf,
  writeSelectionToUrl,
} from './selectionUrl.ts';

const ID = '0f8fad5b-d9cb-469f-a165-70867728950e';
const CAVE_ID = '7c9e6679-7425-40de-944b-e07fc1f90ae7';

function envelope(kind: FeatureEnvelope['kind'], extra: Partial<FeatureEnvelope> = {}): FeatureEnvelope {
  return { kind, feature: { id: ID }, cave: null, entrance: null, centerline: null, ...extra } as unknown as FeatureEnvelope;
}

describe('the selection in the address', () => {
  afterEach(() => window.history.replaceState(null, '', '/'));

  it('names a feature, a cave or an entrance, and nothing else', () => {
    expect(selectionRefOf({ kind: 'feature', featureId: ID })).toEqual({ kind: 'feature', id: ID });
    expect(selectionRefOf({ kind: 'cave', caveId: ID })).toEqual({ kind: 'cave', id: ID });
    expect(selectionRefOf({ kind: 'entrance', entranceId: ID, caveId: CAVE_ID })).toEqual({ kind: 'entrance', id: ID });
    expect(selectionRefOf({ kind: 'cluster', lon: 1, lat: 2, count: 3, zoom: 8, cell: [0.5, 1.5, 1.5, 2.5] })).toBeNull();
    expect(selectionRefOf(null)).toBeNull();
  });

  it('reads only what it writes', () => {
    expect(parseSelectionParam(`cave:${ID}`)).toEqual({ kind: 'cave', id: ID });
    expect(parseSelectionParam(`trip:${ID}`)).toBeNull();
    expect(parseSelectionParam('cave:not-an-id')).toBeNull();
    expect(parseSelectionParam(ID)).toBeNull();
    expect(parseSelectionParam(null)).toBeNull();
  });

  it('keeps the camera in the hash and the other parameters when it writes', () => {
    window.history.replaceState(null, '', '/map?area=7#12/45.5/25.4');

    writeSelectionToUrl({ kind: 'cave', id: ID });
    expect(window.location.search).toBe(`?area=7&select=cave%3A${ID}`);
    expect(window.location.hash).toBe('#12/45.5/25.4');

    writeSelectionToUrl(null);
    expect(window.location.search).toBe('?area=7');
    expect(window.location.hash).toBe('#12/45.5/25.4');
  });
});

describe('selectionFromEnvelope', () => {
  it('selects what the fetched object is, as the panel selects it', () => {
    expect(selectionFromEnvelope({ kind: 'feature', id: ID }, envelope('generic'))).toEqual({ kind: 'feature', featureId: ID });
    expect(selectionFromEnvelope({ kind: 'feature', id: ID }, envelope('cave'))).toEqual({ kind: 'cave', caveId: ID });
    expect(
      selectionFromEnvelope(
        { kind: 'entrance', id: ID },
        envelope('caveEntrance', { entrance: { caveFeatureId: CAVE_ID } as FeatureEnvelope['entrance'] }),
      ),
    ).toEqual({ kind: 'entrance', entranceId: ID, caveId: CAVE_ID });
  });

  it('selects nothing when the object is not the kind the address named', () => {
    expect(selectionFromEnvelope({ kind: 'cave', id: ID }, envelope('generic'))).toBeNull();
    expect(selectionFromEnvelope({ kind: 'entrance', id: ID }, envelope('cave'))).toBeNull();
  });
});
