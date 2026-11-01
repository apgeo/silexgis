// SPDX-License-Identifier: AGPL-3.0-or-later
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const useGraphSearch = vi.fn();
vi.mock('../../api/hooks/graph.ts', () => ({
  useGraphSearch: (q: string) => ({ data: useGraphSearch(q) }),
}));

import GraphSeedSearch, { SEED_SEARCH_DEBOUNCE_MS } from './GraphSeedSearch.tsx';

function Where() {
  const loc = useLocation();
  return <div data-testid="where">{loc.pathname + loc.search}</div>;
}

beforeEach(() => {
  useGraphSearch.mockReset();
  useGraphSearch.mockImplementation((q: string) =>
    q === 'pes' ? [{ key: 'feature:f1', type: 'feature', id: 'f1', title: 'Pestera 1', subtitle: 'cave', kind: 'cave', route: '/caves/f1' }] : undefined,
  );
});

describe('GraphSeedSearch', () => {
  it('asks only after the quiet period, groups hits by type and opens the graph of the pick', async () => {
    render(
      <MemoryRouter initialEntries={['/graph']}>
        <GraphSeedSearch />
        <Where />
      </MemoryRouter>,
    );
    expect(useGraphSearch).toHaveBeenLastCalledWith('');
    const input = screen.getByRole('combobox');
    fireEvent.change(input, { target: { value: 'pes' } });
    expect(useGraphSearch).not.toHaveBeenCalledWith('pes');
    // Nothing was asked yet: the quiet period has not passed.
    expect(SEED_SEARCH_DEBOUNCE_MS).toBe(250);
    await waitFor(() => expect(useGraphSearch).toHaveBeenCalledWith('pes'));
    fireEvent.mouseDown(input);
    const option = await screen.findByText('Pestera 1');
    expect(document.querySelector('.ant-select-item-group')).not.toBeNull();
    fireEvent.click(option);
    expect(screen.getByTestId('where')).toHaveTextContent('/graph?type=feature&id=f1');
  });
});
