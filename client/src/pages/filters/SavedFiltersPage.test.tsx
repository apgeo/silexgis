// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { caveRegistryDocument, readCaveRegistryAddress } from '../../filters/caveRegistry.ts';
import type { SavedFilter } from '../../api/hooks/savedFilters.ts';

const { filters, updateSpy, deleteSpy } = vi.hoisted(() => ({
  filters: { current: [] as unknown[] },
  updateSpy: vi.fn(),
  deleteSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useCaveTypes: () => ({ data: [{ id: 3, name: 'Karst cave' }] }),
  useTags: () => ({ data: [{ id: 1, slug: 'bats', name: 'Bats' }] }),
  useCavingGroups: () => ({ data: [{ id: 'g-1', name: 'Speo Club', slug: 'speo-club' }] }),
}));

vi.mock('../../api/hooks/savedFilters.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks/savedFilters.ts')>(
    '../../api/hooks/savedFilters.ts',
  );
  return {
    documentOf: actual.documentOf,
    useSavedFilters: () => ({ data: filters.current, isLoading: false }),
    useUpdateSavedFilter: () => ({ mutateAsync: updateSpy, isPending: false }),
    useDeleteSavedFilter: () => ({ mutateAsync: deleteSpy, isPending: false }),
  };
});

const { default: SavedFiltersPage } = await import('./SavedFiltersPage.tsx');

function Address() {
  const location = useLocation();
  return <span data-testid="address">{`${location.pathname}${location.search}`}</span>;
}

function show() {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={['/saved-filters']}>
        <SavedFiltersPage />
        <Address />
      </MemoryRouter>
    </AntApp>,
  );
}

function filter(over: Partial<SavedFilter>): SavedFilter {
  return {
    id: 'f-1',
    name: 'Bihor bats',
    description: null,
    document: caveRegistryDocument({ caveTypeId: 3, tag: 'bats', region: 'Bihor' }) as unknown as SavedFilter['document'],
    state: 'ready',
    cavingGroupId: null,
    visibility: 'private',
    mine: true,
    createdAt: '2026-10-16T00:00:00Z',
    updatedAt: '2026-10-16T00:00:00Z',
    ...over,
  };
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
  updateSpy.mockReset().mockResolvedValue({});
  deleteSpy.mockReset().mockResolvedValue(undefined);
});
afterEach(cleanup);

describe('the saved filters screen', () => {
  it('says what each filter asks, naming values only through lists the reader may read', () => {
    filters.current = [
      filter({}),
      filter({
        id: 'f-2',
        name: 'Somebody else’s',
        mine: false,
        visibility: 'cavingGroup',
        cavingGroupId: 'g-1',
        // A tag this reader's list does not hold: shown as not available, never as its slug.
        document: caveRegistryDocument({ tag: 'secret-slug' }) as unknown as SavedFilter['document'],
      }),
    ];
    show();

    const rows = screen.getAllByRole('row');
    const mine = rows.find((row) => within(row).queryByText('Bihor bats'))!;
    expect(within(mine).getByText('Type: Karst cave')).toBeInTheDocument();
    expect(within(mine).getByText('Tag: Bats')).toBeInTheDocument();
    expect(within(mine).getByText('Region contains “Bihor”')).toBeInTheDocument();
    expect(within(mine).getByText('Only me')).toBeInTheDocument();

    const theirs = rows.find((row) => within(row).queryByText('Somebody else’s'))!;
    expect(within(theirs).getByText('Tag: not available to you')).toBeInTheDocument();
    expect(within(theirs).queryByText(/secret-slug/)).toBeNull();
    expect(within(theirs).getByText('Speo Club')).toBeInTheDocument();
  });

  it('offers changes only on the reader’s own filters', () => {
    filters.current = [filter({}), filter({ id: 'f-2', name: 'Shared with me', mine: false })];
    show();

    expect(screen.getByTestId('saved-filter-edit-f-1')).toBeInTheDocument();
    expect(screen.getByTestId('saved-filter-delete-f-1')).toBeInTheDocument();
    expect(screen.queryByTestId('saved-filter-edit-f-2')).toBeNull();
    expect(screen.queryByTestId('saved-filter-delete-f-2')).toBeNull();
  });

  it('shows a filter that has stopped meaning anything as such, and will not open one it cannot read', () => {
    filters.current = [
      filter({ id: 'f-stale', name: 'Stale one', state: 'stale' }),
      filter({ id: 'f-bad', name: 'Broken one', state: 'unreadable' }),
    ];
    show();

    expect(screen.getByTestId('saved-filter-state-stale')).toHaveTextContent('Out of date');
    expect(screen.getByTestId('saved-filter-state-unreadable')).toHaveTextContent('Cannot be read');
    expect(screen.getByTestId('saved-filter-open-f-bad')).toBeDisabled();
  });

  it('opens a filter in the cave registry with its conditions and its name in the address', () => {
    filters.current = [filter({})];
    show();

    fireEvent.click(screen.getByTestId('saved-filter-open-f-1'));

    const address = screen.getByTestId('address').textContent!;
    expect(address.startsWith('/caves?')).toBe(true);
    const state = readCaveRegistryAddress(new URLSearchParams(address.slice('/caves?'.length)));
    expect(state.controls).toEqual({ caveTypeId: 3, tag: 'bats', region: 'Bihor' });
    expect(state.savedFilterId).toBe('f-1');
  });
});
