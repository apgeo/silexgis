// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';

const ID = '0f8fad5b-d9cb-469f-a165-70867728950e';
const geometry = { type: 'Point', coordinates: [25.4, 45.5] };

const fetchFeature = vi.fn();
vi.mock('../../api/hooks.ts', () => ({ fetchFeature: (id: string) => fetchFeature(id) }));
const fitGeoJsonGeometry = vi.fn();
vi.mock('../../map/mapContext.ts', () => ({
  APPROXIMATE_MAX_ZOOM: 12,
  fitGeoJsonGeometry: (...args: unknown[]) => fitGeoJsonGeometry(...args),
}));

const { useSelectionInUrl } = await import('./useSelectionInUrl.ts');

function Probe() {
  useSelectionInUrl();
  return null;
}

beforeEach(() => {
  fetchFeature.mockReset();
  fitGeoJsonGeometry.mockReset();
  useWorkspaceStore.getState().setSelection(null);
});

afterEach(() => {
  cleanup();
  window.history.replaceState(null, '', '/');
});

describe('the selection an address names', () => {
  it('is selected once the reader’s own fetch returns it, and framed as that fetch drew it', async () => {
    window.history.replaceState(null, '', `/map?select=cave:${ID}`);
    fetchFeature.mockResolvedValue({ kind: 'cave', feature: { id: ID, geometry, approximateLocation: true } });

    render(<Probe />);

    await waitFor(() => expect(useWorkspaceStore.getState().selection).toEqual({ kind: 'cave', caveId: ID }));
    expect(fetchFeature).toHaveBeenCalledWith(ID);
    // Approximate: framed loosely, never closer than a snapped position deserves.
    expect(fitGeoJsonGeometry).toHaveBeenCalledWith(geometry, 12);
    expect(new URLSearchParams(window.location.search).get('select')).toBe(`cave:${ID}`);
  });

  it('leaves the camera where an address that also names one puts it', async () => {
    window.history.replaceState(null, '', `/map?select=feature:${ID}#14/45.5/25.4`);
    fetchFeature.mockResolvedValue({ kind: 'generic', feature: { id: ID, geometry, approximateLocation: false } });

    render(<Probe />);

    await waitFor(() => expect(useWorkspaceStore.getState().selection).toEqual({ kind: 'feature', featureId: ID }));
    expect(fitGeoJsonGeometry).not.toHaveBeenCalled();
  });

  it('selects nothing, moves nothing and keeps nothing of an id the reader may not see', async () => {
    window.history.replaceState(null, '', `/map?select=cave:${ID}`);
    fetchFeature.mockRejectedValue(new Error('404'));

    render(<Probe />);

    await waitFor(() => expect(window.location.search).toBe(''));
    expect(useWorkspaceStore.getState().selection).toBeNull();
    expect(fitGeoJsonGeometry).not.toHaveBeenCalled();
  });

  it('does not move the camera to an object whose position is withheld', async () => {
    window.history.replaceState(null, '', `/map?select=cave:${ID}`);
    fetchFeature.mockResolvedValue({ kind: 'cave', feature: { id: ID, geometry: null, approximateLocation: false } });

    render(<Probe />);

    await waitFor(() => expect(useWorkspaceStore.getState().selection).toEqual({ kind: 'cave', caveId: ID }));
    expect(fitGeoJsonGeometry).not.toHaveBeenCalled();
  });
});

it('writes each new selection into the address, and takes it out when nothing is selected', async () => {
  render(<Probe />);

  act(() => useWorkspaceStore.getState().setSelection({ kind: 'feature', featureId: ID }));
  await waitFor(() => expect(new URLSearchParams(window.location.search).get('select')).toBe(`feature:${ID}`));

  act(() => useWorkspaceStore.getState().setSelection(null));
  await waitFor(() => expect(window.location.search).toBe(''));
  expect(fetchFeature).not.toHaveBeenCalled();
});
