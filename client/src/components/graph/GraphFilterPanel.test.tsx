// SPDX-License-Identifier: AGPL-3.0-or-later
import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { noFilters, toggleCategory, toggleRelation } from '../../graph/filters.ts';
import GraphFilterPanel, { type GraphFilterPanelProps } from './GraphFilterPanel.tsx';

const text = {
  labels: { node: () => '', edge: () => '' },
  node: () => '',
  relation: (e: { relation: string }) => `rel ${e.relation}`,
  categoryName: (c: string) => `cat ${c}`,
  typeName: (t: string) => `type ${t}`,
} as unknown as GraphFilterPanelProps['text'];

const props = (over: Partial<GraphFilterPanelProps> = {}): GraphFilterPanelProps => ({
  facets: {
    categories: [
      { category: 'tag', edges: 2, relations: [{ relation: 'tagged', edges: 2 }] },
      { category: 'hierarchy', edges: 3, relations: [{ relation: 'parent', edges: 2 }, { relation: 'entrance', edges: 1 }] },
    ],
    types: [{ type: 'feature', nodes: 4 }, { type: 'document', nodes: 1 }],
  },
  samples: new Map(),
  filters: noFilters(),
  text,
  onToggleCategory: vi.fn(),
  onToggleRelation: vi.fn(),
  onToggleType: vi.fn(),
  onOnlyCategory: vi.fn(),
  onOnlyType: vi.fn(),
  onReset: vi.fn(),
  ...over,
});

const row = (category: string) => document.querySelector(`[data-category="${category}"]`) as HTMLElement;

describe('GraphFilterPanel', () => {
  it('lists only what the graph holds, with counts, sorted by name', () => {
    render(<GraphFilterPanel {...props()} />);
    const names = screen.getAllByTestId('graph-filter-category').map((li) => li.getAttribute('data-category'));
    expect(names).toEqual(['hierarchy', 'tag']);
    expect(within(row('hierarchy')).getByText('(3)')).toBeInTheDocument();
  });

  it('reports a category switch, "only" and the reset', () => {
    const p = props({ filters: toggleCategory(noFilters(), 'tag') });
    render(<GraphFilterPanel {...p} />);
    expect(within(row('tag')).getByRole('checkbox')).not.toBeChecked();
    fireEvent.click(within(row('hierarchy')).getByRole('checkbox'));
    expect(p.onToggleCategory).toHaveBeenCalledWith('hierarchy', false);
    fireEvent.click(within(row('tag')).getByTestId('graph-filter-category-only'));
    expect(p.onOnlyCategory).toHaveBeenCalledWith('tag');
    fireEvent.click(screen.getByTestId('graph-filter-all'));
    expect(p.onReset).toHaveBeenCalled();
  });

  it('expands a category to its relation codes, worded by the label function, and marks a partly-off category', () => {
    const p = props({ filters: toggleRelation(noFilters(), 'hierarchy', 'parent') });
    render(<GraphFilterPanel {...p} />);
    expect(document.querySelector('[data-relation="parent"]')).toBeNull();
    fireEvent.click(within(row('hierarchy')).getByTestId('graph-filter-expand'));
    const parent = document.querySelector('[data-relation="parent"]')!.closest('label') as HTMLElement;
    expect(within(parent).getByRole('checkbox')).not.toBeChecked();
    fireEvent.click(within(document.querySelector('[data-relation="entrance"]')!.closest('label') as HTMLElement).getByRole('checkbox'));
    expect(p.onToggleRelation).toHaveBeenCalledWith('hierarchy', 'entrance', false);
    expect(row('hierarchy').querySelector('.ant-checkbox-indeterminate')).not.toBeNull();
  });

  it('switches several types and keeps "reset" off while nothing is filtered', () => {
    const p = props();
    render(<GraphFilterPanel {...p} />);
    expect(screen.getByTestId('graph-filter-all')).toBeDisabled();
    const doc = document.querySelector('[data-type="document"]') as HTMLElement;
    fireEvent.click(within(doc).getByRole('checkbox'));
    expect(p.onToggleType).toHaveBeenCalledWith('document', false);
    fireEvent.click(within(doc).getByTestId('graph-filter-type-only'));
    expect(p.onOnlyType).toHaveBeenCalledWith('document');
  });
});
