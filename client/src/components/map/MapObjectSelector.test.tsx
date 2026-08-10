// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import MapObjectSelector from './MapObjectSelector.tsx';

/**
 * Where a pick takes you, which is the whole of what this component decides.
 *
 * The selector itself is proved elsewhere — its behaviour against values in
 * `src/filters/selection.test.ts`, its chrome in `src/components/selector/`. What is left here is
 * the routing: a feature moves the map, everything else opens its page, and a feature the map
 * cannot draw falls back to a page rather than doing nothing.
 */

const navigate = vi.fn();
const setSearchParams = vi.fn();
const setSelection = vi.fn();
const fitGeoJsonGeometry = vi.fn();
const fetchFeature = vi.fn();

vi.mock('react-router-dom', () => ({
  useNavigate: () => navigate,
  useSearchParams: () => [new URLSearchParams(), setSearchParams],
}));

vi.mock('../../map/mapContext.ts', () => ({
  fitGeoJsonGeometry: (geometry: object) => fitGeoJsonGeometry(geometry),
}));

vi.mock('../../stores/workspaceStore.ts', () => ({
  useWorkspaceStore: (selector: (s: unknown) => unknown) => selector({ setSelection }),
}));

vi.mock('../../api/hooks.ts', () => ({
  fetchFeature: (id: string) => fetchFeature(id),
}));

/**
 * The selector stands in for itself: this file is about what happens *after* a pick, and driving
 * the real control would mean re-testing the search box to get at the callback.
 */
let lastOnPick: ((hit: unknown) => void) | undefined;
vi.mock('../selector/ObjectSelector.tsx', () => ({
  default: (props: { onPick?: (hit: unknown) => void }) => {
    lastOnPick = props.onPick;
    return <div data-testid="stand-in" />;
  },
}));

const hit = (world: string, id: string) =>
  ({ world, id, title: 'x', subtitle: null, symbol: null, placeable: true });

beforeEach(() => {
  cleanup();
  vi.clearAllMocks();
  lastOnPick = undefined;
});

function pick(world: string, id: string) {
  // Inside antd's App, as it is on the map: the component reaches for the message channel that
  // provider supplies, and outside it there is no channel to reach.
  render(
    <App>
      <MapObjectSelector />
    </App>,
  );
  lastOnPick!(hit(world, id));
}

describe('picking from the map selector', () => {
  it('takes the map to a feature rather than opening its page', async () => {
    fetchFeature.mockResolvedValue({
      kind: 'cave',
      feature: { geometry: { type: 'Point', coordinates: [22.5, 46.5] } },
    });

    pick('feature', 'f1');

    await waitFor(() => expect(fitGeoJsonGeometry).toHaveBeenCalledWith(
      { type: 'Point', coordinates: [22.5, 46.5] }));
    expect(setSelection).toHaveBeenCalledWith({ kind: 'feature', featureId: 'f1' });
    expect(navigate).not.toHaveBeenCalled();
  });

  it('draws the geometry the feature endpoint returned, not one of its own', async () => {
    // The hit carries no position, so the only geometry available is the one the feature's own
    // endpoint hands back — already snapped or withheld per this caller. A selector that kept a
    // coordinate of its own would be a second place deciding that, and would show the exact
    // position where the map shows an approximate one.
    const snapped = { type: 'Point', coordinates: [22.0, 46.0] };
    fetchFeature.mockResolvedValue({ kind: 'cave', feature: { geometry: snapped } });

    pick('feature', 'f1');

    await waitFor(() => expect(fitGeoJsonGeometry).toHaveBeenCalledWith(snapped));
  });

  it('opens the page of a feature the map cannot draw', async () => {
    // Either it was never placed, or this caller may not be shown where it is. Its page is the
    // honest destination and it says which; doing nothing would read as a broken click.
    fetchFeature.mockResolvedValue({ kind: 'cave', feature: { geometry: null } });

    pick('feature', 'f2');

    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/caves/f2'));
    expect(fitGeoJsonGeometry).not.toHaveBeenCalled();
  });

  it('opens the page of anything the map does not draw', async () => {
    pick('document', 'd1');
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/documents/d1'));

    cleanup();
    pick('tripLog', 't1');
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/trip-logs/t1'));

    expect(fetchFeature).not.toHaveBeenCalled();
  });

  it('applies a saved view on the map instead of opening a page', async () => {
    pick('mapView', 'v1');

    await waitFor(() => expect(setSearchParams).toHaveBeenCalled());
    expect(navigate).not.toHaveBeenCalled();
  });

  it('does nothing loudly when a later world has no destination here', async () => {
    pick('somethingNew', 'x1');

    await waitFor(() => expect(navigate).not.toHaveBeenCalled());
    expect(fitGeoJsonGeometry).not.toHaveBeenCalled();
  });
});
