// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import SelectionToolbar from './SelectionToolbar.tsx';

afterEach(cleanup);

const counts = { candidates: 4, matched: 3, unmatched: 1, tracks: 0, areas: 0 };

function toolbar(props: Partial<Parameters<typeof SelectionToolbar>[0]> = {}) {
  const onSelectedChange = vi.fn();
  const onFiltersChange = vi.fn();
  render(
    <SelectionToolbar
      filters={{}}
      onFiltersChange={onFiltersChange}
      ruleHits={[]}
      selected={new Set()}
      onSelectedChange={onSelectedChange}
      filteredIds={[1, 2, 3, 4]}
      selectableIds={[1, 2, 4]}
      disabled={false}
      counts={counts}
      {...props}
    />,
  );
  return { onSelectedChange, onFiltersChange };
}

const lastSelection = (spy: ReturnType<typeof vi.fn>) => [...(spy.mock.lastCall![0] as Set<number>)].sort();

describe('picking rows without paging through them', () => {
  it('takes every row the filter matches that something says what to become, and no other', () => {
    const { onSelectedChange } = toolbar();

    fireEvent.click(screen.getByTestId('import-select-all'));

    expect(lastSelection(onSelectedChange)).toEqual([1, 2, 4]);
  });

  it('inverts only what could be selected', () => {
    const { onSelectedChange } = toolbar({ selected: new Set([1, 3]) });

    fireEvent.click(screen.getByText(i18n.t('vectorImport.selectInvert')));

    // 3 is not selectable and keeps whatever it had; 1 goes, 2 and 4 come.
    expect(lastSelection(onSelectedChange)).toEqual([2, 3, 4]);
  });

  it('counts the selection out of everything the filter matches, not out of one page', () => {
    toolbar({ selected: new Set([1, 2]) });

    expect(screen.getByText(i18n.t('vectorImport.selectedCount', { count: 2, total: 4 }))).toBeInTheDocument();
  });

  it('offers nothing to press while there is nothing to pick from', () => {
    toolbar({ disabled: true });

    expect(screen.getByTestId('import-select-all')).toBeDisabled();
  });

  it('hands a typed search on as a filter, and an emptied one as no filter', () => {
    const { onFiltersChange } = toolbar({ filters: { rule: 'cave', search: 'izbu' } });
    const search = screen.getByPlaceholderText(i18n.t('vectorImport.searchPlaceholder'));

    fireEvent.change(search, { target: { value: 'izbuc' } });
    expect(onFiltersChange).toHaveBeenLastCalledWith({ rule: 'cave', search: 'izbuc' });

    fireEvent.change(search, { target: { value: '' } });
    expect(onFiltersChange).toHaveBeenLastCalledWith({ rule: 'cave', search: undefined });
  });
});
