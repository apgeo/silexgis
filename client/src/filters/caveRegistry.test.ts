// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  caveRegistryDocument,
  caveRegistryToken,
  readCaveRegistry,
  readCaveRegistryAddress,
  sameQuestion,
  writeCaveRegistryAddress,
  type CaveRegistryControls,
} from './caveRegistry.ts';
import { describeCaveRegistry } from './describe.ts';
import { encodeFilter } from './url.ts';
import type { FilterDocument, FilterNode } from './types.ts';

const EVERY_CONTROL: CaveRegistryControls = {
  search: 'urs',
  caveTypeId: 3,
  region: 'Bihor',
  minLength: 300,
  tag: 'bats',
};

const odd: FilterNode = {
  node: 'condition',
  field: 'depth',
  op: 'greaterThan',
  values: [{ type: 'number', value: 50 }],
};

describe('the cave registry controls as one filter document', () => {
  it('compose the conditions the server reads, always about caves', () => {
    const document = caveRegistryDocument(EVERY_CONTROL);

    expect(document).toEqual({
      version: 1,
      scope: [
        {
          world: 'feature',
          where: {
            node: 'allOf',
            of: [
              { node: 'condition', field: 'kind', op: 'equals', values: [{ type: 'id', value: 'cave' }] },
              {
                node: 'anyOf',
                of: [
                  { node: 'condition', field: 'name', op: 'contains', values: [{ type: 'text', value: 'urs' }] },
                  {
                    node: 'condition',
                    field: 'otherToponyms',
                    op: 'contains',
                    values: [{ type: 'text', value: 'urs' }],
                  },
                ],
              },
              { node: 'condition', field: 'caveTypeId', op: 'equals', values: [{ type: 'id', value: '3' }] },
              { node: 'condition', field: 'region', op: 'contains', values: [{ type: 'text', value: 'Bihor' }] },
              {
                node: 'anyOf',
                of: [
                  {
                    node: 'condition',
                    field: 'surveyedLength',
                    op: 'greaterThan',
                    values: [{ type: 'number', value: 300 }],
                  },
                  {
                    node: 'condition',
                    field: 'surveyedLength',
                    op: 'equals',
                    values: [{ type: 'number', value: 300 }],
                  },
                ],
              },
              { node: 'condition', field: 'tag', op: 'equals', values: [{ type: 'id', value: 'bats' }] },
            ],
          },
        },
      ],
      sort: 'updated',
      descending: true,
    });
  });

  it('narrow nothing, and so write no document, when no control is set', () => {
    expect(caveRegistryDocument({})).toBeNull();
    expect(caveRegistryDocument({ search: '   ', region: '' })).toBeNull();
    expect(caveRegistryToken({ controls: {}, extra: [] })).toBeUndefined();
  });

  it('read back into the same controls', () => {
    const reading = readCaveRegistry(caveRegistryDocument(EVERY_CONTROL)!);

    expect(reading).toEqual({ controls: EVERY_CONTROL, extra: [] });
  });

  it('keep every condition the controls cannot show, and send it on', () => {
    const secondRegion: FilterNode = {
      node: 'condition',
      field: 'region',
      op: 'contains',
      values: [{ type: 'text', value: 'Nord' }],
    };
    const document = caveRegistryDocument({ region: 'Bihor' }, [odd, secondRegion])!;

    const reading = readCaveRegistry(document)!;

    // A second region is a narrowing the one region box cannot show, so it stays where it keeps
    // narrowing rather than overwriting the box or being dropped.
    expect(reading.controls).toEqual({ region: 'Bihor' });
    expect(reading.extra).toEqual([odd, secondRegion]);
    expect(caveRegistryDocument(reading.controls, reading.extra)).toEqual(document);
  });

  it('read a single condition that is not in a group', () => {
    const document: FilterDocument = {
      version: 1,
      scope: [{ world: 'feature', where: { node: 'condition', field: 'tag', op: 'equals', values: [{ type: 'id', value: 'bats' }] } }],
      sort: 'updated',
      descending: true,
    };

    expect(readCaveRegistry(document)).toEqual({ controls: { tag: 'bats' }, extra: [] });
  });

  it('refuse a document that is not about the feature world', () => {
    const aboutTrips: FilterDocument = { version: 1, scope: [{ world: 'tripLog', where: null }], sort: 'updated', descending: true };

    expect(readCaveRegistry(aboutTrips)).toBeNull();
  });

  it('tell a changed question from the same one', () => {
    const saved = { controls: { region: 'Bihor' }, extra: [] };

    expect(sameQuestion(saved, { controls: { region: 'Bihor' }, extra: [] })).toBe(true);
    expect(sameQuestion(saved, { controls: { region: 'Bihor', tag: 'bats' }, extra: [] })).toBe(false);
  });
});

describe('the cave registry address', () => {
  it('round-trips the controls, the saved filter, the sort and the page', () => {
    const state = {
      controls: EVERY_CONTROL,
      extra: [odd],
      savedFilterId: 'f-1',
      sort: '-surveyedLength',
      page: 3,
      pageSize: 50,
    };

    const address = readCaveRegistryAddress(writeCaveRegistryAddress(state));

    expect(address).toEqual({ ...state, unreadableLink: false });
  });

  it('leaves defaults out, so an unfiltered list is a bare address', () => {
    expect(writeCaveRegistryAddress({ controls: {}, extra: [], page: 1, pageSize: 20 }).toString()).toBe('');
  });

  it('opens unfiltered, and says so, when its filter cannot be read', () => {
    for (const token of ['eyJ2ZXJzaW9uIjox', encodeFilter({ version: 1, scope: [{ world: 'tripLog', where: null }], sort: 'updated', descending: true })]) {
      const address = readCaveRegistryAddress(new URLSearchParams({ filter: token }));

      expect(address.unreadableLink).toBe(true);
      expect(address.controls).toEqual({});
      expect(address.extra).toEqual([]);
    }
  });

  it('is not unreadable when it simply has no filter', () => {
    expect(readCaveRegistryAddress(new URLSearchParams('page=2')).unreadableLink).toBe(false);
  });
});

describe('a saved cave filter put into words', () => {
  it('names each control by its part and counts the rest', () => {
    const parts = describeCaveRegistry(caveRegistryDocument({ caveTypeId: 3, tag: 'bats' }, [odd])!);

    expect(parts).toEqual([
      { kind: 'caveType', id: '3' },
      { kind: 'tag', slug: 'bats' },
      { kind: 'more', count: 1 },
    ]);
  });
});
