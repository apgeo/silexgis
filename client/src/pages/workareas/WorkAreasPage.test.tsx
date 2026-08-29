// SPDX-License-Identifier: AGPL-3.0-or-later
import { MemoryRouter } from 'react-router-dom';
import { cleanup, render, screen, fireEvent } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { WorkArea } from '../../api/hooks.ts';

const navigate = vi.fn();
vi.mock('react-router-dom', async () => ({
  ...(await vi.importActual<typeof import('react-router-dom')>('react-router-dom')),
  useNavigate: () => navigate,
}));

let areas: WorkArea[] = [];
let truncated = false;
vi.mock('../../api/hooks.ts', () => ({
  useWorkAreas: () => ({ data: { items: areas, truncated }, isLoading: false, isError: false }),
  useMapLayers: () => ({ data: [] }),
}));

// The canvas is not what these assertions are about, and jsdom has no WebGL or layout for it.
// Stubbed to the smallest surface the page actually calls, so a real render still runs.
vi.mock('ol/Map', () => ({
  default: class {
    setTarget() {}
    getLayers() { return { getArray: () => [] }; }
    addLayer() {}
    getView() { return { fit: () => {} }; }
    on() {}
    un() {}
    forEachFeatureAtPixel() { return undefined; }
  },
}));

import WorkAreasPage from './WorkAreasPage.tsx';

const square = (west: number): WorkArea['geometry'] => ({
  type: 'Polygon',
  coordinates: [[[west, 45], [west + 1, 45], [west + 1, 46], [west, 46], [west, 45]]],
} as WorkArea['geometry']);

function area(id: string, name: string, parentId: string | null, childCount: number): WorkArea {
  return { id, name, description: null, parentId, childCount, geometry: square(25) };
}

function renderPage() {
  return render(<MemoryRouter><WorkAreasPage /></MemoryRouter>);
}

beforeEach(() => {
  navigate.mockClear();
  truncated = false;
  areas = [];
});

afterEach(cleanup);

describe('the work-area overview', () => {
  it('shows the top level first', () => {
    areas = [area('a', 'Bucegi', null, 1), area('b', 'Ialomița valley', 'a', 0)];
    renderPage();

    expect(screen.getByText('Bucegi')).toBeInTheDocument();
    // The level beneath is reached by opening the one above it, not by being listed alongside.
    expect(screen.queryByText('Ialomița valley')).toBeNull();
  });

  it('opens the level inside an area rather than leaving the page', () => {
    areas = [area('a', 'Bucegi', null, 1), area('b', 'Ialomița valley', 'a', 0)];
    renderPage();

    fireEvent.click(screen.getByText('Bucegi'));

    expect(screen.getByText('Ialomița valley')).toBeInTheDocument();
    // Drilling in is not navigation: the reader is still choosing where to go.
    expect(navigate).not.toHaveBeenCalled();
  });

  it('goes to the map for an area with nothing inside it', () => {
    areas = [area('a', 'Bucegi', null, 0)];
    renderPage();

    fireEvent.click(screen.getByText('Bucegi'));

    expect(navigate).toHaveBeenCalledWith('/map?area=a');
  });

  it('offers the map directly, without opening the levels beneath first', () => {
    areas = [area('a', 'Bucegi', null, 3)];
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /On the map/ }));

    expect(navigate).toHaveBeenCalledWith('/map?area=a');
  });

  it('says so when the answer was capped rather than showing a short list as if it were all', () => {
    areas = [area('a', 'Bucegi', null, 0)];
    truncated = true;
    renderPage();

    expect(screen.getByText(/Not every work area is shown/)).toBeInTheDocument();
  });

  it('lists an area nobody has outlined yet, which the map cannot show', () => {
    areas = [{ id: 'a', name: 'Unmapped', description: null, parentId: null, childCount: 0, geometry: null }];
    renderPage();

    expect(screen.getByText('Unmapped')).toBeInTheDocument();
  });
});
