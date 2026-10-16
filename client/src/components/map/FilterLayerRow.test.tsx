// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { FilterLayerRowTitle, type FilterLayerRowInfo } from './FilterLayerRow.tsx';

const savedFilterId = '00000000-0000-4000-8000-000000000001';
const row = (overrides: Partial<FilterLayerRowInfo> = {}): FilterLayerRowInfo => ({
  spec: { savedFilterId, color: 'violet', symbol: 'circle', visible: true },
  name: 'Winter leads',
  status: 'ready',
  ...overrides,
});

afterEach(cleanup);

describe('a filter layer row', () => {
  it('names each of its buttons after the layer and what pressing it does', async () => {
    await i18n.changeLanguage('en');
    const onStyleChange = vi.fn();
    const onRemove = vi.fn();
    render(<FilterLayerRowTitle row={row()} onStyleChange={onStyleChange} onRemove={onRemove} />);

    fireEvent.click(screen.getByRole('button', { name: /colour of winter leads: violet/i }));
    expect(onStyleChange).toHaveBeenLastCalledWith({ color: 'magenta' });
    fireEvent.click(screen.getByRole('button', { name: /symbol of winter leads: circle/i }));
    expect(onStyleChange).toHaveBeenLastCalledWith({ symbol: 'square' });
    fireEvent.click(screen.getByRole('button', { name: /remove the layer winter leads/i }));
    expect(onRemove).toHaveBeenCalledOnce();
  });

  it('says when its filter cannot be run, and only then', async () => {
    await i18n.changeLanguage('en');
    const { rerender } = render(<FilterLayerRowTitle row={row()} onStyleChange={vi.fn()} onRemove={vi.fn()} />);
    expect(screen.queryByText('Unreadable')).toBeNull();
    rerender(
      <FilterLayerRowTitle row={row({ status: 'unreadable', name: null })} onStyleChange={vi.fn()} onRemove={vi.fn()} />,
    );
    expect(screen.getByText('Unreadable')).toBeInTheDocument();
    // A filter this person can no longer see is not named: its name is the filter's, not theirs.
    expect(screen.getByText(/a saved filter you cannot open/i)).toBeInTheDocument();
    expect(screen.queryByText('Winter leads')).toBeNull();
  });
});
