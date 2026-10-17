// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  CAVE_REGISTRY_SCOPE,
  caveRegistryDocument,
  caveRegistryEditorDocument,
  caveRegistryEditorQuestion,
  caveRegistryListToken,
  caveRegistryOrderAfterEdit,
  caveRegistryToken,
  readCaveRegistry,
  readCaveRegistryEditor,
  readCaveRegistryAddress,
  sameQuestion,
  writeCaveRegistryAddress,
  type CaveRegistryControls,
} from './caveRegistry.ts';
import { describeCaveRegistry } from './describe.ts';
import { emptyDocument, whereOf, withWhere } from './document.ts';
import { decodeFilter, encodeFilter } from './url.ts';
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
                node: 'condition',
                field: 'surveyedLength',
                op: 'greaterOrEqual',
                values: [{ type: 'number', value: 300 }],
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

  it('show a length the box cannot say as an extra condition rather than as its own', () => {
    // The box means "at least"; "more than" leaves out the cave exactly at the length, so reading
    // it into the box would change the question the next time the controls wrote it out.
    const moreThan: FilterNode = {
      node: 'condition',
      field: 'surveyedLength',
      op: 'greaterThan',
      values: [{ type: 'number', value: 300 }],
    };

    const reading = readCaveRegistry(caveRegistryDocument({}, [moreThan])!)!;

    expect(reading.controls).toEqual({});
    expect(reading.extra).toEqual([moreThan]);
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

describe('the registry as the advanced filter sees it', () => {
  it('opens on the controls and the extra conditions in one all-of, without the fixed caves condition', () => {
    const odd: FilterNode = { node: 'condition', field: 'depth', op: 'lessOrEqual', values: [{ type: 'number', value: 5 }] };
    const document = caveRegistryEditorDocument({ controls: { search: 'urs' }, extra: [odd] });

    const where = whereOf(document, 'feature');
    expect(where?.node).toBe('allOf');
    expect(where?.node === 'allOf' && where.of.length).toBe(2);
    expect(JSON.stringify(where)).not.toContain('"kind"');
  });

  it('reads an edited document back into the same controls and extra conditions', () => {
    const odd: FilterNode = { node: 'condition', field: 'depth', op: 'lessOrEqual', values: [{ type: 'number', value: 5 }] };
    const reading = { controls: { region: 'Bihor', minLength: 100, tag: 'bats' }, extra: [odd] };

    expect(readCaveRegistryEditor(caveRegistryEditorDocument(reading))).toEqual(reading);
  });

  it('keeps an any-of written at the top as one extra condition, still about caves once composed', () => {
    const either: FilterNode = {
      node: 'anyOf',
      of: [
        { node: 'condition', field: 'surveyedLength', op: 'greaterOrEqual', values: [{ type: 'number', value: 1000 }] },
        { node: 'condition', field: 'depth', op: 'greaterOrEqual', values: [{ type: 'number', value: 100 }] },
      ],
    };
    const reading = readCaveRegistryEditor(withWhere(emptyDocument(['feature']), 'feature', either));

    expect(reading).toEqual({ controls: {}, extra: [either] });
    const composed = whereOf(caveRegistryDocument(reading.controls, reading.extra)!, 'feature');
    expect(composed).toEqual({ node: 'allOf', of: [CAVE_REGISTRY_SCOPE, either] });
  });

  it('narrows nothing when the edited document is about another world', () => {
    expect(readCaveRegistryEditor(emptyDocument(['tripLog']))).toEqual({ controls: {}, extra: [] });
  });
});

describe('nearest first in the registry', () => {
  const near = { anchor: 'point' as const, longitude: 22.5, latitude: 46.25 };
  const reading = { controls: { region: 'Bihor' }, extra: [] };

  it('carries the place in the address beside the sort, and only beside nearest first', () => {
    const params = writeCaveRegistryAddress({ ...reading, sort: 'proximity', near, page: 1, pageSize: 20 });
    expect(readCaveRegistryAddress(params).near).toEqual(near);

    const object = { anchor: 'object' as const, world: 'feature', id: 'f0e1d2c3-0000-4000-8000-000000000001' };
    const byObject = writeCaveRegistryAddress({ ...reading, sort: 'proximity', near: object, page: 1, pageSize: 20 });
    expect(readCaveRegistryAddress(byObject).near).toEqual(object);

    const byName = writeCaveRegistryAddress({ ...reading, sort: 'name', near, page: 1, pageSize: 20 });
    expect(byName.has('near')).toBe(false);
    expect(readCaveRegistryAddress(new URLSearchParams('sort=proximity&near=500,1')).near).toBeUndefined();
  });

  it('asks the list with the place and keeps it out of the token the exports and a save use', () => {
    const listed = decodeFilter(caveRegistryListToken({ ...reading, sort: 'proximity', near }) ?? null);
    expect(listed?.sort).toBe('proximity');
    expect(listed?.anchor).toEqual(near);

    const plain = decodeFilter(caveRegistryToken(reading) ?? null);
    expect(plain?.anchor ?? null).toBeNull();
    // With no condition at all there is still a question to carry the place: caves.
    expect(decodeFilter(caveRegistryListToken({ controls: {}, extra: [], sort: 'proximity', near }) ?? null)?.anchor)
      .toEqual(near);
  });

  it('opens the editor on the order the list has and applies the order chosen there', () => {
    const opened = caveRegistryEditorQuestion({ ...reading, sort: 'proximity', near });
    expect([opened.sort, opened.anchor]).toEqual(['proximity', near]);

    // Nearest first chosen: the place goes to the address.
    const byName = caveRegistryEditorQuestion({ ...reading, sort: 'name' });
    const chosen = { ...byName, sort: 'proximity' as const, anchor: near };
    expect(caveRegistryOrderAfterEdit(chosen, byName, 'name')).toEqual({ sort: 'proximity', near });

    // Untouched: a column sort the editor cannot show is kept.
    expect(caveRegistryOrderAfterEdit(byName, byName, 'name')).toEqual({ sort: 'name', near: undefined });

    // Nearest first turned off goes back to the registry's own order.
    const off = { ...opened, sort: 'updated' as const, descending: true, anchor: null };
    expect(caveRegistryOrderAfterEdit(off, opened, 'proximity')).toEqual({ sort: undefined, near: undefined });
  });
});
