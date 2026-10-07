// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { declaredPartsView, NOTHING_DECLARED } from './declaredParts.ts';
import type { CaveViewTreeNode } from './loadCaveView.ts';

const station = (name: string): CaveViewTreeNode => ({ name, children: [], isStation: () => true });
const survey = (name: string, ...children: CaveViewTreeNode[]): CaveViewTreeNode => ({
  name,
  children,
  isStation: () => false,
});

/** A file wrapped in one survey named for the cave, as most are. */
const wrapped = survey(
  '',
  survey(
    'cave',
    survey('ent', station('0')),
    survey('upper', station('1'), station('2'), survey('loop', station('1'))),
    survey('upper2', station('1')),
    survey('deep', station('3')),
  ),
);

/** The same cave as a viewer draws it for the format whose outermost name it drops. */
const unwrapped = survey(
  '',
  survey('ent', station('0')),
  survey('upper', station('1'), station('2')),
  survey('deep', station('3')),
);

describe('declaredPartsView', () => {
  it('hides nothing where nothing is declared or nothing is drawn', () => {
    expect(declaredPartsView(wrapped, [])).toBe(NOTHING_DECLARED);
    expect(declaredPartsView(null, ['cave.upper'])).toBe(NOTHING_DECLARED);
  });

  it('keeps a declared survey with everything in it and hides its siblings whole', () => {
    const view = declaredPartsView(wrapped, ['cave.deep']);
    expect(view.unmatched).toEqual([]);
    expect(view.hide).toEqual([
      ['cave', 'ent'],
      ['cave', 'upper'],
      ['cave', 'upper2'],
    ]);
  });

  it('reads an entry as the start of a name, as a station is read on the server', () => {
    // "cave.upper" is the start of both surveys' names, and of the loop inside the first.
    expect(declaredPartsView(wrapped, ['cave.upper']).hide).toEqual([
      ['cave', 'ent'],
      ['cave', 'deep'],
    ]);
  });

  it('keeps the survey an entry reaches into, and asks its own surveys in turn', () => {
    // A station of the upper series: the series stays, the loop inside it holds nothing declared.
    const view = declaredPartsView(wrapped, ['cave.upper.2']);
    expect(view.unmatched).toEqual([]);
    expect(view.hide).toEqual([
      ['cave', 'ent'],
      ['cave', 'upper', 'loop'],
      ['cave', 'upper2'],
      ['cave', 'deep'],
    ]);
  });

  it('takes several entries together', () => {
    expect(declaredPartsView(wrapped, ['cave.ent', 'cave.deep']).hide).toEqual([
      ['cave', 'upper'],
      ['cave', 'upper2'],
    ]);
  });

  it('reads an entry written with the outermost name the drawing drops', () => {
    const view = declaredPartsView(unwrapped, ['cave.upper']);
    expect(view.unmatched).toEqual([]);
    expect(view.hide).toEqual([['ent'], ['deep']]);
    // And the drawing's own spelling is taken as it is.
    expect(declaredPartsView(unwrapped, ['upper'])).toEqual(view);
  });

  it('reports an entry no survey answers to, in either reading', () => {
    const view = declaredPartsView(wrapped, ['cave.deep', 'elsewhere.series']);
    expect(view.unmatched).toEqual(['elsewhere.series']);
    // What it would hide is still worked out for the entries that were read; whether to act on
    // half a declaration is the caller's to refuse.
    expect(view.hide).toEqual([
      ['cave', 'ent'],
      ['cave', 'upper'],
      ['cave', 'upper2'],
    ]);
  });
});
