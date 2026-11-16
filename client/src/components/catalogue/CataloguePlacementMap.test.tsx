// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render } from '@testing-library/react';
import type Feature from 'ol/Feature';
import type OlMap from 'ol/Map';
import type MapBrowserEvent from 'ol/MapBrowserEvent';
import { fromLonLat } from 'ol/proj';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { getWorkspaceMap } from '../../map/mapContext.ts';
import CataloguePlacementMap, { type PlacedCave } from './CataloguePlacementMap.tsx';

// Every map the component builds, in order: the number is the defect, not the markup.
const built = vi.hoisted(() => [] as OlMap[]);
vi.mock('ol/Map', async (original) => {
  const Actual = (await original<typeof import('ol/Map')>()).default;
  class Counted extends Actual {
    constructor(...args: ConstructorParameters<typeof Actual>) {
      super(...args);
      built.push(this);
    }
  }
  return { default: Counted };
});
vi.mock('../../api/hooks.ts', async (original) => ({
  ...(await original<typeof import('../../api/hooks.ts')>()),
  useMapLayers: () => ({ isPending: false, data: [] }),
}));

const cave = (id: number, longitude: number, latitude: number): PlacedCave => ({
  id,
  title: `pestera ${id}`,
  longitude,
  latitude,
});

// A fresh array on every call, as the import page builds it on every render.
const placedNow = () => [cave(1, 22.1, 46.1), cave(2, 22.2, 46.2)];
const noop = () => {};

/** The labels of the caves drawn on the map's own layer — the background draws tiles, not features. */
function labels(map: OlMap): string[] {
  return map
    .getAllLayers()
    .flatMap((layer) => (layer.getSource() as { getFeatures?: () => Feature[] } | null)?.getFeatures?.() ?? [])
    .map((feature) => feature.get('label') as string)
    .sort();
}

/** A click on the map as OpenLayers delivers it, at a position given as [longitude, latitude]. */
function click(map: OlMap, lonLat: [number, number]) {
  act(() => {
    map.dispatchEvent({
      type: 'singleclick',
      pixel: [10, 10],
      coordinate: fromLonLat(lonLat),
    } as unknown as MapBrowserEvent);
  });
}

beforeEach(() => {
  // The workspace map is the application's one shared map, which the background choice reads;
  // it exists in every session, so it is made before counting starts.
  getWorkspaceMap();
  built.length = 0;
});
afterEach(cleanup);

describe('CataloguePlacementMap', () => {
  it('keeps one map and the reader’s view while the wizard re-renders it', () => {
    const { rerender } = render(<CataloguePlacementMap placed={placedNow()} placingId={null} onPlace={noop} />);
    expect(built).toHaveLength(1);
    const view = built[0].getView();
    view.setCenter(fromLonLat([10, 50]));
    view.setZoom(12);

    // A selection, a focused row, a fresh callback: each is a new render with new props.
    for (let i = 0; i < 4; i += 1) {
      rerender(<CataloguePlacementMap placed={placedNow()} placingId={i} onPlace={() => {}} />);
    }
    expect(built).toHaveLength(1);
    expect(built[0].getTargetElement()).not.toBeNull();
    expect(view.getZoom()).toBe(12);
  });

  it('frames the placed caves once, and not again when the page re-renders or a cave is picked to place', () => {
    const { rerender } = render(<CataloguePlacementMap placed={placedNow()} placingId={null} onPlace={noop} />);
    const map = built[0];
    const fit = vi.spyOn(map.getView(), 'fit');
    act(() => map.setSize([400, 300]));
    expect(fit).toHaveBeenCalledTimes(1);

    rerender(<CataloguePlacementMap placed={placedNow()} placingId={7} onPlace={noop} />);
    rerender(<CataloguePlacementMap placed={placedNow()} placingId={null} onPlace={noop} />);
    expect(fit).toHaveBeenCalledTimes(1);

    rerender(<CataloguePlacementMap placed={[...placedNow(), cave(3, 23, 47)]} placingId={null} onPlace={noop} />);
    expect(fit).toHaveBeenCalledTimes(2);
  });

  it('places the cave being positioned where the map is clicked, through the latest callback', () => {
    const first = vi.fn();
    const latest = vi.fn();
    const { rerender } = render(<CataloguePlacementMap placed={placedNow()} placingId={null} onPlace={first} />);
    const map = built[0];

    // Nothing is being placed: a click does nothing.
    click(map, [23.5, 47.25]);
    expect(first).not.toHaveBeenCalled();

    rerender(<CataloguePlacementMap placed={placedNow()} placingId={9} onPlace={latest} />);
    click(map, [23.5, 47.25]);
    expect(first).not.toHaveBeenCalled();
    expect(latest).toHaveBeenCalledTimes(1);
    const [id, [lon, lat]] = latest.mock.calls[0] as [number, [number, number]];
    expect(id).toBe(9);
    expect(lon).toBeCloseTo(23.5, 6);
    expect(lat).toBeCloseTo(47.25, 6);
  });

  it('redraws the placed caves when they change', () => {
    const { rerender } = render(<CataloguePlacementMap placed={placedNow()} placingId={null} onPlace={noop} />);
    const map = built[0];
    expect(labels(map)).toEqual(['pestera 1', 'pestera 2']);

    rerender(<CataloguePlacementMap placed={[cave(3, 23, 47)]} placingId={null} onPlace={noop} />);
    expect(labels(map)).toEqual(['pestera 3']);
  });

  it('disposes its map and every layer on it when it goes', () => {
    const { unmount } = render(<CataloguePlacementMap placed={placedNow()} placingId={null} onPlace={noop} />);
    const map = built[0];
    unmount();
    expect(map.getTargetElement()).toBeNull();
    expect(map.getLayers().getLength()).toBe(0);
  });

  it('draws the installation’s background, never a hard-coded tile server', () => {
    render(<CataloguePlacementMap placed={placedNow()} placingId={null} onPlace={noop} />);
    const urls = built[0]
      .getAllLayers()
      .flatMap((layer) => (layer.getSource() as { getUrls?: () => string[] | null })?.getUrls?.() ?? []);
    expect(urls.length).toBeGreaterThan(0);
    expect(urls.some((url) => url.includes('tile.openstreetmap.org'))).toBe(false);
  });
});
