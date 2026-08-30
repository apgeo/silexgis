// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import Map from 'ol/Map';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const { mapSpy } = vi.hoisted(() => ({ mapSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useExpeditionMap: (...args: unknown[]) => mapSpy(...args),
}));

const { default: ExpeditionMapTab } = await import('./ExpeditionMapTab.tsx');

const CAMP = '77777777-8888-9999-aaaa-bbbbbbbbbbbb';

const collection = (features: unknown[]) => ({ type: 'FeatureCollection', features });

const area = {
  type: 'Feature',
  geometry: {
    type: 'Polygon',
    coordinates: [
      [
        [25.4, 45.5],
        [25.6, 45.5],
        [25.6, 45.7],
        [25.4, 45.7],
        [25.4, 45.5],
      ],
    ],
  },
  properties: { kind: 'area', id: CAMP, name: 'Bihor summer camp' },
};

afterEach(cleanup);
beforeEach(() => {
  vi.clearAllMocks();
  mapSpy.mockReturnValue({ data: collection([area]), isPending: false });
});

/**
 * Nothing of an OpenLayers map renders under jsdom, so the assertions here are about what the
 * component asks for and when — which is the part that has gone wrong on this shape before.
 */
describe("a camp's map", () => {
  it('does not build a map, or ask for one, while its tab is not the one on screen', () => {
    const built = vi.spyOn(Map.prototype, 'setTarget');
    render(<ExpeditionMapTab expeditionId={CAMP} active={false} />);

    // A map built against a pane nobody is looking at measures nothing and draws a blank tile
    // grid that never repairs itself, so it is not built at all until the pane is shown.
    expect(built).not.toHaveBeenCalled();
    expect(mapSpy).toHaveBeenCalledWith(CAMP, false);
  });

  it('builds the map once its tab becomes the one on screen', () => {
    const { rerender } = render(<ExpeditionMapTab expeditionId={CAMP} active={false} />);
    rerender(<ExpeditionMapTab expeditionId={CAMP} active />);

    expect(screen.getByTestId('expedition-map')).toBeTruthy();
    expect(mapSpy).toHaveBeenLastCalledWith(CAMP, true);
  });

  it('says what the drawing is over, because two readers see two maps of one camp', () => {
    render(<ExpeditionMapTab expeditionId={CAMP} active />);

    expect(screen.getByText(/Drawn over the trips you may read/)).toBeTruthy();
  });

  it('re-measures itself when an answer that was empty stops being empty', () => {
    // A cached empty answer is there on the first render, so the map is built while its own
    // container is not laid out and measures nothing. Whatever un-hides it later must re-measure,
    // or the reader gets the blank tile grid instead of the drawing.
    const measured = vi.spyOn(Map.prototype, 'updateSize');
    mapSpy.mockReturnValue({ data: collection([]), isPending: false });
    const { rerender } = render(<ExpeditionMapTab expeditionId={CAMP} active />);

    // Building a map measures it once by itself, so what is asserted is a measurement caused by
    // the answer changing — not whatever the construction did.
    const onceBuilt = measured.mock.calls.length;
    mapSpy.mockReturnValue({ data: collection([area]), isPending: false });
    rerender(<ExpeditionMapTab expeditionId={CAMP} active />);

    expect(measured.mock.calls.length).toBeGreaterThan(onceBuilt);
  });

  it('says so plainly when there is nothing it may draw', () => {
    mapSpy.mockReturnValue({ data: collection([]), isPending: false });
    render(<ExpeditionMapTab expeditionId={CAMP} active />);

    expect(screen.getByText(/Nothing to draw for this camp/)).toBeTruthy();
  });
});
