// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import { emptyDocument, withWhere } from '../filters/document.ts';
import { decodeFilter, encodeFilter } from '../filters/url.ts';
import {
  handleMapFilterError,
  mapFilterParams,
  onMapFilterRefusal,
  readMapFilterAddress,
  sameMapFilter,
  tagChipFilter,
  tagOfMapFilter,
  writeMapFilterAddress,
  type MapFilter,
} from './mapFilters.ts';

const savedFilterId = '0e6f2c4a-5b1d-4c39-9b7e-2f8a1d3c4e5f';
const lengthDocument = withWhere(emptyDocument(['feature']), 'feature', {
  node: 'condition',
  field: 'surveyedLength',
  op: 'greaterThan',
  values: [{ type: 'number', value: 300 }],
});

describe('the tag chooser as a filter document', () => {
  it('writes one tag condition about features, and reads the tag back out', () => {
    const filter = tagChipFilter('winter-2026');
    expect(filter).toEqual({
      kind: 'document',
      document: withWhere(emptyDocument(['feature']), 'feature', {
        node: 'condition',
        field: 'tag',
        op: 'equals',
        values: [{ type: 'id', value: 'winter-2026' }],
      }),
    });
    expect(tagOfMapFilter(filter)).toBe('winter-2026');
  });

  it('is no filter when no tag is chosen', () => {
    expect(tagChipFilter(null)).toBeNull();
    expect(tagChipFilter('  ')).toBeNull();
  });

  it('does not read a richer filter as a tag, so the chooser never claims to show it', () => {
    expect(tagOfMapFilter({ kind: 'document', document: lengthDocument })).toBeNull();
    expect(tagOfMapFilter({ kind: 'saved', savedFilterId })).toBeNull();
    const tagAndLength = withWhere(emptyDocument(['feature']), 'feature', {
      node: 'allOf',
      of: [
        { node: 'condition', field: 'tag', op: 'equals', values: [{ type: 'id', value: 'leads' }] },
        { node: 'condition', field: 'surveyedLength', op: 'greaterThan', values: [{ type: 'number', value: 1 }] },
      ],
    });
    expect(tagOfMapFilter({ kind: 'document', document: tagAndLength })).toBeNull();
  });
});

describe('what the map routes are sent', () => {
  it('sends a document as its token and a saved filter by id, never both', () => {
    const document = mapFilterParams({ kind: 'document', document: lengthDocument });
    expect(Object.keys(document)).toEqual(['filter']);
    expect(decodeFilter(document.filter)).toEqual(lengthDocument);

    expect(mapFilterParams({ kind: 'saved', savedFilterId })).toEqual({ savedFilterId });
    expect(mapFilterParams(null)).toEqual({});
  });
});

describe('the map filter in the address', () => {
  const roundTrip = (filter: MapFilter | null) =>
    readMapFilterAddress(writeMapFilterAddress(new URLSearchParams(), filter));

  it('round-trips a document, a saved filter and none', () => {
    const document: MapFilter = { kind: 'document', document: lengthDocument };
    expect(roundTrip(document)).toEqual({ filter: document, unreadableLink: false });
    expect(sameMapFilter(roundTrip(document).filter, document)).toBe(true);

    const saved: MapFilter = { kind: 'saved', savedFilterId };
    expect(roundTrip(saved)).toEqual({ filter: saved, unreadableLink: false });
    expect(roundTrip(null)).toEqual({ filter: null, unreadableLink: false });
  });

  it('keeps every other parameter, and replaces one form with the other', () => {
    const start = new URLSearchParams({ view: 'v1', trips: '1', saved: savedFilterId });
    const written = writeMapFilterAddress(start, { kind: 'document', document: lengthDocument });
    expect(written.get('view')).toBe('v1');
    expect(written.get('trips')).toBe('1');
    expect(written.get('saved')).toBeNull();
    expect(written.get('filter')).toBe(encodeFilter(lengthDocument));

    const cleared = writeMapFilterAddress(written, null);
    expect(cleared.get('filter')).toBeNull();
    expect(cleared.get('view')).toBe('v1');
    // A copy: the parameters handed in are not edited under their owner.
    expect(start.get('saved')).toBe(savedFilterId);
  });

  it('opens unfiltered and says so for a filter it cannot read', () => {
    expect(readMapFilterAddress(new URLSearchParams({ filter: 'eyJ2ZXJzaW9uIjox' }))).toEqual({
      filter: null,
      unreadableLink: true,
    });
    expect(readMapFilterAddress(new URLSearchParams({ saved: 'not-an-id' }))).toEqual({
      filter: null,
      unreadableLink: true,
    });
  });

  it('takes the document when a link carries both forms', () => {
    const params = new URLSearchParams({ filter: encodeFilter(lengthDocument), saved: savedFilterId });
    expect(readMapFilterAddress(params).filter).toEqual({ kind: 'document', document: lengthDocument });
  });
});

describe('a refused filter', () => {
  it('empties the layer and tells the page, and leaves it alone for any other error', () => {
    const listener = vi.fn();
    const detach = onMapFilterRefusal(listener);
    const clear = vi.fn();

    handleMapFilterError({ status: 404, code: 'saved_filter.not_found' }, clear);
    expect(clear).toHaveBeenCalledTimes(1);
    expect(listener).toHaveBeenCalledWith('saved_filter.not_found');

    handleMapFilterError({ status: 400, code: 'map.invalid_bbox' }, clear);
    handleMapFilterError(new TypeError('Failed to fetch'), clear);
    expect(clear).toHaveBeenCalledTimes(1);
    expect(listener).toHaveBeenCalledTimes(1);

    detach();
    handleMapFilterError({ status: 400, code: 'filter.invalid' }, clear);
    expect(listener).toHaveBeenCalledTimes(1);
  });
});
