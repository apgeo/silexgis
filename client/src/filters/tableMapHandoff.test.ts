// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { caveRegistryDocument, caveRegistryToken, readCaveRegistryAddress, writeCaveRegistryAddress } from './caveRegistry.ts';
import { featureListDocument } from './featureList.ts';
import { mapAddressForList, readWindow, registryAddressForMapView } from './tableMapHandoff.ts';
import { readMapFilterAddress } from '../map/mapFilters.ts';
import { decodeFilter, encodeFilter } from './url.ts';
import { emptyDocument, withWhere } from './document.ts';
import type { FilterDocument } from './types.ts';

function query(address: string): URLSearchParams {
  return new URLSearchParams(address.slice(address.indexOf('?') + 1));
}

const tagDocument: FilterDocument = withWhere(emptyDocument(['feature']), 'feature', {
  node: 'condition',
  field: 'tag',
  op: 'equals',
  values: [{ type: 'id', value: 'bats' }],
});

describe('the list opens the map on its own question', () => {
  it('hands the cave registry document to the map unchanged, so the map runs what the list ran', () => {
    const token = caveRegistryToken({ controls: { region: 'Bihor', tag: 'bats' }, extra: [] });
    const address = mapAddressForList(token);

    expect(address.startsWith('/map?')).toBe(true);
    const arrived = readMapFilterAddress(query(address));
    expect(arrived.unreadableLink).toBe(false);
    expect(arrived.filter).toEqual({
      kind: 'document',
      document: caveRegistryDocument({ region: 'Bihor', tag: 'bats' }, []),
    });
  });

  it('hands the feature list document over the same way', () => {
    const document = featureListDocument({ kind: 'generic', search: 'doline' }, []);
    expect(document).not.toBeNull();
    const arrived = readMapFilterAddress(query(mapAddressForList(encodeFilter(document!))));
    expect(arrived.filter).toEqual({ kind: 'document', document });
  });

  it('opens the plain map when the list is not narrowed', () => {
    expect(mapAddressForList(undefined)).toBe('/map');
    expect(mapAddressForList('')).toBe('/map');
  });
});

describe('the map opens the registry on what is in view', () => {
  const extent = [22.1, 46.3, 22.9, 46.8] as const;

  it("carries the map's document and its window into the registry's address", () => {
    const address = registryAddressForMapView(extent, tagDocument);
    expect(address.startsWith('/caves?')).toBe(true);

    const registry = readCaveRegistryAddress(query(address));
    expect(registry.unreadableLink).toBe(false);
    expect(registry.unreadableWindow).toBeFalsy();
    expect(registry.controls).toEqual({ tag: 'bats' });
    expect(registry.extra).toEqual([]);
    expect(registry.bbox).toBe('22.10000,46.30000,22.90000,46.80000');
  });

  it('names the saved filter beside its document, so the registry shows it as the one applied', () => {
    const id = '0f8fad5b-d9cb-469f-a165-70867728950e';
    const registry = readCaveRegistryAddress(query(registryAddressForMapView(extent, tagDocument, id)));
    expect(registry.savedFilterId).toBe(id);
    expect(registry.controls).toEqual({ tag: 'bats' });
  });

  it('carries only the window when the map has no filter', () => {
    const params = query(registryAddressForMapView(extent, null));
    expect(params.get('filter')).toBeNull();
    expect(params.get('saved')).toBeNull();
    expect(readCaveRegistryAddress(params).bbox).toBe('22.10000,46.30000,22.90000,46.80000');
  });

  it('brings a view wider than the world back inside it rather than sending a box the server refuses', () => {
    const params = query(registryAddressForMapView([-400, -95, 400, 95], null));
    expect(params.get('bbox')).toBe('-180.00000,-90.00000,180.00000,90.00000');
  });

  it('keeps the window through every write the registry makes and drops it when cleared', () => {
    const state = readCaveRegistryAddress(query(registryAddressForMapView(extent, tagDocument)));
    const rewritten = writeCaveRegistryAddress({ ...state, page: 3 });
    expect(rewritten.get('bbox')).toBe(state.bbox);
    expect(decodeFilter(rewritten.get('filter'))).toEqual(caveRegistryDocument({ tag: 'bats' }, []));

    const cleared = writeCaveRegistryAddress({ ...state, bbox: undefined });
    expect(cleared.has('bbox')).toBe(false);
  });
});

describe('a window read back out of an address', () => {
  it('takes a well-formed box', () => {
    expect(readWindow('22.1,46.3,22.9,46.8')).toEqual({ bbox: '22.1,46.3,22.9,46.8', unreadable: false });
  });

  it('is no window at all when there is none', () => {
    expect(readWindow(null)).toEqual({ unreadable: false });
    expect(readWindow('')).toEqual({ unreadable: false });
  });

  it.each([
    ['three numbers', '22.1,46.3,22.9'],
    ['words', 'a,b,c,d'],
    ['west past east', '22.9,46.3,22.1,46.8'],
    ['south past north', '22.1,46.8,22.9,46.3'],
    ['outside the globe', '-200,46.3,22.9,46.8'],
  ])('is unreadable, never the whole registry, for %s', (_label, raw) => {
    expect(readWindow(raw)).toEqual({ unreadable: true });
    const registry = readCaveRegistryAddress(new URLSearchParams({ bbox: raw }));
    expect(registry.bbox).toBeUndefined();
    expect(registry.unreadableWindow).toBe(true);
  });
});
