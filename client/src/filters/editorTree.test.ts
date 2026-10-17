// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  appendChild,
  depthOf,
  editableOps,
  nodeAt,
  placeAnchorRefusal,
  placeRefusal,
  prune,
  removeAt,
  replaceAt,
  requirementMet,
  toggleNot,
  valuesForOp,
  withinDepth,
} from './editorTree.ts';
import type { FilterField, FilterNode } from './types.ts';

const cond = (field: string): FilterNode => ({
  node: 'condition', field, op: 'equals', values: [{ type: 'number', value: 1 }],
});
const blank: FilterNode = { node: 'condition', field: '', op: 'equals', values: [] };
const typeIs = (id: string): FilterNode => ({
  node: 'condition', field: 'typeId', op: 'equals', values: [{ type: 'id', value: id }],
});

const depthProperty: FilterField = {
  key: 'property:sinkhole:depth_m', labelKey: 'filters.fields.property', kind: 'number',
  ops: ['lessThan', 'greaterOrEqual'], options: null, label: 'Depth (m)',
  requires: { field: 'typeId', value: '7' },
};

describe('the editor tree', () => {
  it('reaches, replaces and removes nodes by their path, through a negation', () => {
    const root: FilterNode = { node: 'allOf', of: [cond('a'), { node: 'not', of: { node: 'anyOf', of: [cond('b'), cond('c')] } }] };

    expect(nodeAt(root, [1, 0, 1])).toEqual(cond('c'));
    expect(nodeAt(replaceAt(root, [1, 0, 1], cond('d')), [1, 0, 1])).toEqual(cond('d'));
    expect(removeAt(root, [1, 0, 0])).toEqual({ node: 'allOf', of: [cond('a'), { node: 'not', of: { node: 'anyOf', of: [cond('c')] } }] });
    // A negation of nothing is not a question: taking its only child takes the negation.
    expect(removeAt(root, [1, 0])).toEqual({ node: 'allOf', of: [cond('a')] });
    // The original is never changed.
    expect(nodeAt(root, [1, 0, 1])).toEqual(cond('c'));
  });

  it('negates a node and takes the negation away again', () => {
    const root: FilterNode = { node: 'allOf', of: [cond('a')] };
    const negated = toggleNot(root, [0]);

    expect(negated).toEqual({ node: 'allOf', of: [{ node: 'not', of: cond('a') }] });
    expect(toggleNot(negated, [0])).toEqual(root);
  });

  it('counts depth the way the server does, and refuses a group past the limit', () => {
    // Each nested group adds one level; a condition inside is one more.
    let root: FilterNode = { node: 'allOf', of: [] };
    let path: number[] = [];
    for (let level = 2; level <= 5; level++) {
      root = appendChild(root, path, { node: 'anyOf', of: [] });
      path = [...path, 0];
    }
    root = appendChild(root, path, blank);
    expect(depthOf(root)).toBe(6);
    expect(withinDepth(root, 6)).toBe(true);
    expect(withinDepth(appendChild(root, path, { node: 'allOf', of: [blank] }), 6)).toBe(false);
    // A negation is a level too.
    expect(withinDepth(toggleNot(root, [...path, 0]), 6)).toBe(false);
  });

  it('leaves half-written rows, and the groups they empty, out of what is sent', () => {
    const root: FilterNode = {
      node: 'allOf',
      of: [cond('a'), blank, { node: 'anyOf', of: [blank] }, { node: 'not', of: blank },
        { node: 'condition', field: 'n', op: 'between', values: [{ type: 'number', value: 1 }, { type: 'number', value: null as unknown as number }] }],
    };

    expect(prune(root)).toEqual({ node: 'allOf', of: [cond('a')] });
    expect(prune({ node: 'allOf', of: [blank] })).toBeNull();
  });

  it('keeps as many values as the new comparison takes', () => {
    const two = [{ type: 'number' as const, value: 1 }, { type: 'number' as const, value: 2 }];
    expect(valuesForOp('greaterOrEqual', two)).toEqual([two[0]]);
    expect(valuesForOp('between', two)).toEqual(two);
    expect(valuesForOp('isEmpty', two)).toEqual([]);
  });

  it('starts a spatial row again when its operator changes either way', () => {
    const near = [{ type: 'number' as const, value: 500 }, { type: 'point' as const, longitude: 1, latitude: 2 }];
    const area = [{ type: 'object' as const, world: 'feature', id: 'a' }];
    expect(valuesForOp('within', near)).toEqual(near);
    expect(valuesForOp('inside', near)).toEqual([]);
    expect(valuesForOp('inside', area)).toEqual(area);
    expect(valuesForOp('within', area)).toEqual([]);
    expect(valuesForOp('equals', near)).toEqual([]);
  });
});

describe('spatial rows', () => {
  it('offer every operator the server lists, within and inside among them', () => {
    const position: FilterField = {
      key: 'position', labelKey: 'x', kind: 'spatial', ops: ['within', 'inside'], options: null, label: null, requires: null,
    };
    expect(editableOps(position)).toEqual(['within', 'inside']);
  });

  it('take an anchor refusal on every sent row that names something to look up, and on no other', () => {
    const lookedUp: FilterNode = { node: 'condition', field: 'position', op: 'within',
      values: [{ type: 'number', value: 5 }, { type: 'savedFilter', id: 's' }] };
    const typed: FilterNode = { node: 'condition', field: 'position', op: 'within',
      values: [{ type: 'number', value: 5 }, { type: 'point', longitude: 1, latitude: 2 }] };
    const root: FilterNode = { node: 'allOf', of: [typed, { node: 'not', of: lookedUp }] };

    const placed = placeAnchorRefusal('Not available.', root);
    expect([...placed.byPath.keys()]).toEqual(['1.0']);
    expect(placed.unplaced).toEqual([]);
    expect(placeAnchorRefusal('Not available.', { node: 'allOf', of: [typed] }).unplaced).toEqual(['Not available.']);
  });
});

describe('a property field is offered only where its type is fixed', () => {
  it('beside its type in an AND, or anywhere below one', () => {
    const root: FilterNode = {
      node: 'allOf',
      of: [typeIs('7'), blank, { node: 'anyOf', of: [blank, { node: 'not', of: blank }] }],
    };
    expect(requirementMet(root, [1], depthProperty)).toBe(true);
    expect(requirementMet(root, [2, 0], depthProperty)).toBe(true);
    expect(requirementMet(root, [2, 1, 0], depthProperty)).toBe(true);
  });

  it('not beside another type, under an OR, under a negation, or in a sibling group', () => {
    expect(requirementMet({ node: 'allOf', of: [typeIs('8'), blank] }, [1], depthProperty)).toBe(false);
    expect(requirementMet({ node: 'anyOf', of: [typeIs('7'), blank] }, [1], depthProperty)).toBe(false);
    expect(requirementMet({ node: 'allOf', of: [{ node: 'not', of: typeIs('7') }, blank] }, [1], depthProperty)).toBe(false);
    expect(requirementMet(
      { node: 'allOf', of: [{ node: 'allOf', of: [typeIs('7')] }, { node: 'allOf', of: [blank] }] },
      [1, 0], depthProperty,
    )).toBe(false);
    // The row is not its own guard.
    expect(requirementMet({ node: 'allOf', of: [typeIs('7')] }, [0], depthProperty)).toBe(false);
  });
});

describe('a refusal is placed on the row it names', () => {
  it('puts a sentence that quotes a field on that row, and the rest above the rows', () => {
    const root: FilterNode = { node: 'allOf', of: [cond('name'), { node: 'anyOf', of: [cond('depth')] }, { ...blank, field: 'depth' }] };
    const placed = placeRefusal(
      "'depth' was given a range that ends before it starts. A filter holds at most 200 conditions and groups.",
      root,
    );

    expect([...placed.byPath.entries()]).toEqual([['1.0', ["'depth' was given a range that ends before it starts."]]]);
    expect(placed.unplaced).toEqual(['A filter holds at most 200 conditions and groups.']);
  });
});
