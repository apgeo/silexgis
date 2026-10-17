// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  featureListDocument,
  featureListToken,
  readFeatureList,
  readFeatureListAddress,
  sameFeatureQuestion,
  writeFeatureListAddress,
} from './featureList.ts';
import { decodeFilter, encodeFilter } from './url.ts';
import { emptyDocument, withWhere } from './document.ts';
import type { FilterNode } from './types.ts';

describe('the feature list as one filter document', () => {
  it('narrows nothing, and so has no document, when no control is set', () => {
    expect(featureListDocument({})).toBeNull();
    expect(featureListDocument({ search: '   ' })).toBeNull();
    expect(featureListToken({ controls: {}, extra: [] })).toBeUndefined();
  });

  it('says only what it was told: no kind is implied', () => {
    const document = featureListDocument({ tag: 'bats' })!;

    expect(document.scope).toEqual([
      {
        world: 'feature',
        where: {
          node: 'allOf',
          of: [{ node: 'condition', field: 'tag', op: 'equals', values: [{ type: 'id', value: 'bats' }] }],
        },
      },
    ]);
  });

  it('reads every control back from the document it wrote', () => {
    const controls = { search: 'dol', kind: 'generic', featureTypeId: 7, category: 'surface', tag: 'bats' } as const;

    expect(readFeatureList(featureListDocument(controls)!)).toEqual({ controls, extra: [] });
  });

  it('keeps a condition the controls cannot show, and a second one of a kind they can, as extra', () => {
    const owner: FilterNode = {
      node: 'condition',
      field: 'ownerId',
      op: 'equals',
      values: [{ type: 'id', value: 'u1' }],
    };
    const secondKind: FilterNode = {
      node: 'condition',
      field: 'kind',
      op: 'equals',
      values: [{ type: 'id', value: 'cave' }],
    };
    const unknownKind: FilterNode = {
      node: 'condition',
      field: 'kind',
      op: 'equals',
      values: [{ type: 'id', value: 'dragon' }],
    };
    const document = featureListDocument({ kind: 'generic' }, [owner, secondKind, unknownKind])!;

    expect(readFeatureList(document)).toEqual({ controls: { kind: 'generic' }, extra: [owner, secondKind, unknownKind] });
  });

  it('is not a reading of a document about another world', () => {
    expect(readFeatureList(withWhere(emptyDocument(['tripLog']), 'tripLog', null))).toBeNull();
  });

  it('tells two equal questions from different ones', () => {
    expect(sameFeatureQuestion(
      { controls: { tag: 'bats', kind: 'generic' }, extra: [] },
      { controls: { kind: 'generic', tag: 'bats' }, extra: [] },
    )).toBe(true);
    expect(sameFeatureQuestion(
      { controls: { tag: 'bats' }, extra: [] },
      { controls: { tag: 'owls' }, extra: [] },
    )).toBe(false);
  });
});

describe('the feature list address', () => {
  it('round-trips controls, the saved filter and the page, leaving defaults out', () => {
    const params = writeFeatureListAddress({
      controls: { featureTypeId: 7 },
      extra: [],
      savedFilterId: 'f1',
      page: 1,
      pageSize: 20,
    });

    expect([...params.keys()].sort()).toEqual(['filter', 'saved']);
    expect(readFeatureListAddress(params)).toEqual({
      controls: { featureTypeId: 7 },
      extra: [],
      unreadableLink: false,
      savedFilterId: 'f1',
      page: 1,
      pageSize: 20,
    });
  });

  it('marks a link whose filter cannot be read, or is about another world, and opens unfiltered', () => {
    const cutShort = readFeatureListAddress(new URLSearchParams('filter=eyJ2ZXJzaW9uIjox'));
    const aboutTrips = readFeatureListAddress(
      new URLSearchParams({ filter: encodeFilter(emptyDocument(['tripLog'])) }),
    );

    for (const address of [cutShort, aboutTrips]) {
      expect(address.unreadableLink).toBe(true);
      expect(address.controls).toEqual({});
      expect(featureListToken(address)).toBeUndefined();
    }
  });

  it('carries the document a link was written with', () => {
    const token = encodeFilter(featureListDocument({ category: 'area' })!);
    const address = readFeatureListAddress(new URLSearchParams({ filter: token, page: '3' }));

    expect(address.page).toBe(3);
    expect(decodeFilter(featureListToken(address))).toEqual(decodeFilter(token));
  });
});
