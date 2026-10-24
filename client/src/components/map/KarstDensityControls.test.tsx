// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { KarstDensityLoadState } from '../../map/karstDensityLayer.ts';

/**
 * The density row's words. An empty overlay and a refused window draw the same nothing on a map,
 * so the row is the only place the difference is visible, and it is asserted as text.
 */

let state: KarstDensityLoadState;
const listeners = new Set<(s: KarstDensityLoadState) => void>();

vi.mock('../../map/karstDensityLayer.ts', () => ({
  KARST_DENSITY_MODES: ['density', 'kernel', 'hotSpot'],
  KARST_DENSITY_BAND_SCHEMES: ['relative', 'fixed'],
  getKarstDensityLoadState: () => state,
  subscribeKarstDensityLoadState: (listener: (s: KarstDensityLoadState) => void) => {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
}));

const { default: KarstDensityControls } = await import('./KarstDensityControls.tsx');

function base(over: Partial<KarstDensityLoadState>): KarstDensityLoadState {
  return {
    status: 'idle',
    cellMetres: null,
    minimumCellMetres: null,
    bandMaxima: [],
    significanceZ: null,
    mode: 'density',
    bands: 'relative',
    ...over,
  };
}

afterEach(() => {
  cleanup();
  listeners.clear();
});

describe('KarstDensityControls', () => {
  it('names the cell size the server answered at, beside the finest it publishes', async () => {
    await i18n.changeLanguage('en');
    state = base({ status: 'ok', cellMetres: 20000, minimumCellMetres: 5000 });
    render(<KarstDensityControls mode="density" onModeChange={() => {}} bands="relative" onBandsChange={() => {}} />);
    expect(screen.getByTestId('map-karst-density-cell').textContent).toContain('20000 m');
    expect(screen.getByTestId('map-karst-density-cell').textContent).toContain('5000 m');
    expect(screen.queryByTestId('map-karst-density-refused')).toBeNull();
  });

  it('says a window too wide for any cell is refused, and turns back when an answer arrives', async () => {
    await i18n.changeLanguage('en');
    state = base({ status: 'refused', refusal: 'density.too_many_cells' });
    render(<KarstDensityControls mode="density" onModeChange={() => {}} bands="relative" onBandsChange={() => {}} />);
    expect(screen.getByTestId('map-karst-density-refused').textContent).toMatch(/too wide.*Zoom in/);

    act(() => {
      for (const listener of listeners) listener(base({ status: 'ok', cellMetres: 5000, minimumCellMetres: 5000 }));
    });
    expect(screen.queryByTestId('map-karst-density-refused')).toBeNull();
    expect(screen.getByTestId('map-karst-density-cell').textContent).toContain('5000 m');
  });

  it('says a refusal it has no words of its own for as a refusal, in Romanian too', async () => {
    await i18n.changeLanguage('ro');
    state = base({ status: 'refused', refusal: 'density.something_new' });
    render(<KarstDensityControls mode="kernel" onModeChange={() => {}} bands="relative" onBandsChange={() => {}} />);
    expect(screen.getByTestId('map-karst-density-refused').textContent).toBe(
      i18n.t('map.karstDensity.refused.other'),
    );
    await i18n.changeLanguage('en');
  });

  it('offers relative or fixed bands for the two density readings, and none for hot spots', async () => {
    await i18n.changeLanguage('en');
    state = base({ status: 'ok', cellMetres: 5000, minimumCellMetres: 5000 });
    const onBandsChange = vi.fn();
    const { rerender } = render(
      <KarstDensityControls mode="density" onModeChange={() => {}} bands="relative" onBandsChange={onBandsChange} />,
    );
    const bands = screen.getByTestId('map-karst-density-bands');
    expect(bands.textContent).toContain('Relative to view');
    fireEvent.click(screen.getByText('Fixed'));
    expect(onBandsChange).toHaveBeenCalledWith('fixed');

    rerender(<KarstDensityControls mode="hotSpot" onModeChange={() => {}} bands="fixed" onBandsChange={onBandsChange} />);
    expect(screen.queryByTestId('map-karst-density-bands')).toBeNull();
  });
});
