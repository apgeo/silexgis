// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { FilterValue } from '../../filters/types.ts';
import SpatialValueInput from './SpatialValueInput.tsx';

/**
 * Each spatial input writes the values the server reads, and writes nothing until it is whole.
 *
 * The object pickers are the ordinary selector, which has its own tests; here it is stood in for by
 * a box that hands back an id, so what is proved is what this input does with that id.
 */

const CAVE = '0b3f6a52-1d7e-4c5a-9a61-3f2d9c4e8b10';

vi.mock('../selector/ObjectSelector.tsx', () => ({
  default: (props: { value: string | null; onChange: (id: string | null) => void; 'data-testid': string }) => (
    <input
      data-testid={props['data-testid']}
      value={props.value ?? ''}
      onChange={(e) => props.onChange(e.target.value === '' ? null : e.target.value)}
    />
  ),
}));

let savedFilters: unknown[] = [];
vi.mock('../../api/hooks.ts', () => ({
  useSavedFilters: () => ({ data: savedFilters, isLoading: false }),
  documentOf: (f: { document: unknown }) => f.document,
}));

const seen: FilterValue[][] = [];

function show(op: 'within' | 'inside', values: FilterValue[] = []) {
  seen.length = 0;
  render(
    <SpatialValueInput world="feature" op={op} values={values}
      onChange={(next) => seen.push(next)} testId="v" />,
  );
}

function inputOf(testId: string): HTMLInputElement {
  const el = screen.getByTestId(testId);
  return (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' ? el : el.querySelector('input')) as HTMLInputElement;
}

function pick(testId: string, label: string) {
  fireEvent.mouseDown(within(screen.getByTestId(testId)).getByRole('combobox'));
  fireEvent.click(screen.getAllByText(label).at(-1)!);
}

beforeEach(async () => {
  cleanup();
  savedFilters = [];
  await i18n.changeLanguage('en');
});

describe('within a distance', () => {
  it('of an object: a radius and the picked object, in that order', () => {
    show('within', [{ type: 'number', value: 500 }]);
    fireEvent.change(inputOf('v-object'), { target: { value: CAVE } });

    expect(seen.at(-1)).toEqual([{ type: 'number', value: 500 }, { type: 'object', world: 'feature', id: CAVE }]);
  });

  it('of a point: written only once both halves are on the globe', () => {
    show('within', [{ type: 'number', value: 250 }]);
    pick('v-anchor-kind', 'a point');
    fireEvent.change(inputOf('v-lon'), { target: { value: '23.5' } });
    expect(seen.at(-1)).toEqual([{ type: 'number', value: 250 }]);

    fireEvent.change(inputOf('v-lat'), { target: { value: '45.25' } });
    expect(seen.at(-1)).toEqual([
      { type: 'number', value: 250 }, { type: 'point', longitude: 23.5, latitude: 45.25 },
    ]);
  });

  it('keeps the anchor in its place when the radius is emptied, so the row is unfinished', () => {
    show('within', [{ type: 'number', value: 500 }, { type: 'object', world: 'feature', id: CAVE }]);
    fireEvent.change(inputOf('v-radius'), { target: { value: '' } });

    expect(seen.at(-1)).toEqual([
      { type: 'number', value: null }, { type: 'object', world: 'feature', id: CAVE },
    ]);
  });

  it('of a saved filter: only those this reader can run over this world are offered', () => {
    const doc = (world: string) => ({ version: 1, scope: [{ world, where: null }], sort: 'updated', descending: true });
    savedFilters = [
      { id: 's1', name: 'Big caves', state: 'ready', document: doc('feature') },
      { id: 's2', name: 'Unreadable one', state: 'unreadable', document: null },
      { id: 's3', name: 'Some trips', state: 'ready', document: doc('tripLog') },
    ];
    show('within', [{ type: 'number', value: 1000 }]);
    pick('v-anchor-kind', "a saved filter's results");
    fireEvent.mouseDown(within(screen.getByTestId('v-saved')).getByRole('combobox'));

    expect(screen.queryByText('Unreadable one')).toBeNull();
    expect(screen.queryByText('Some trips')).toBeNull();
    fireEvent.click(screen.getByText('Big caves'));
    expect(seen.at(-1)).toEqual([{ type: 'number', value: 1000 }, { type: 'savedFilter', id: 's1' }]);
  });

  it('names a saved filter it cannot offer as not available, never by its identifier', () => {
    show('within', [{ type: 'number', value: 1000 }, { type: 'savedFilter', id: 'gone-1234' }]);

    expect(screen.getByTestId('v-saved').textContent).toContain(i18n.t('savedFilters.notAvailable'));
    expect(screen.getByTestId('v-saved').textContent).not.toContain('gone-1234');
  });
});

describe('inside an outline', () => {
  it('of a named area: the picked area object', () => {
    show('inside');
    fireEvent.change(inputOf('v-area'), { target: { value: CAVE } });

    expect(seen.at(-1)).toEqual([{ type: 'object', world: 'feature', id: CAVE }]);
  });

  it('of a pasted polygon: written once it reads, with the reason shown while it does not', () => {
    show('inside');
    pick('v-outline-kind', 'a pasted polygon');
    fireEvent.change(inputOf('v-polygon'), { target: { value: '{"type":"Point","coordinates":[1,2]}' } });
    expect(screen.getByTestId('v-polygon-problem')).toHaveTextContent('This is not a single polygon.');
    expect(seen.at(-1)).toEqual([]);

    const ring = [[23, 45], [23.01, 45], [23.01, 45.01], [23, 45]];
    fireEvent.change(inputOf('v-polygon'), { target: { value: JSON.stringify({ type: 'Polygon', coordinates: [ring] }) } });
    expect(screen.queryByTestId('v-polygon-problem')).toBeNull();
    expect(seen.at(-1)).toEqual([{ type: 'polygon', coordinates: [ring] }]);
  });
});

describe('in Romanian', () => {
  it('names every choice in the reader language', async () => {
    await i18n.changeLanguage('ro');
    show('within');
    expect(screen.getByTestId('v-anchor-kind').textContent).toContain('un obiect');
  });
});
