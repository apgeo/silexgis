// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render } from '@testing-library/react';
import type Feature from 'ol/Feature';
import type OlMap from 'ol/Map';
import type MapBrowserEvent from 'ol/MapBrowserEvent';
import { fromLonLat } from 'ol/proj';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ImportCandidate } from '../../api/hooks.ts';
import { getWorkspaceMap } from '../../map/mapContext.ts';
import CandidateMap from './CandidateMap.tsx';

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

const candidate = (sourceId: number, lon: number, lat: number) =>
  ({
    sourceId,
    proposedKind: 'cave',
    geom: { type: 'Point', coordinates: [lon, lat] },
  }) as unknown as ImportCandidate;

const page = [candidate(1, 22.1, 46.1), candidate(2, 22.2, 46.2)];

/** The features on the map's own layers — the background draws tiles, not features. */
function drawnFeatures(map: OlMap): Feature[] {
  return map
    .getAllLayers()
    .flatMap((layer) => (layer.getSource() as { getFeatures?: () => Feature[] } | null)?.getFeatures?.() ?? []);
}

function sourceIds(map: OlMap): number[] {
  return drawnFeatures(map)
    .map((feature) => feature.get('sourceId') as number)
    .sort((a, b) => a - b);
}

/**
 * A click on the map as OpenLayers delivers it. Hit-detection needs a rendered frame, which jsdom
 * never draws, so the hit is named here; what is under test is what the component does with it.
 */
function click(map: OlMap, hit: Feature | undefined) {
  vi.spyOn(map, 'forEachFeatureAtPixel').mockReturnValue(hit as never);
  act(() => {
    map.dispatchEvent({ type: 'singleclick', pixel: [10, 10], coordinate: [0, 0] } as unknown as MapBrowserEvent);
  });
}

beforeEach(() => {
  // The workspace map is the application's one shared map, which the background choice reads;
  // it exists in every session, so it is made before counting starts.
  getWorkspaceMap();
  built.length = 0;
});
afterEach(cleanup);

describe('CandidateMap', () => {
  it('keeps one map and the reader’s view while the table re-renders it', () => {
    const { rerender } = render(<CandidateMap candidates={page} selected={new Set()} />);
    expect(built).toHaveLength(1);
    const view = built[0].getView();
    view.setCenter(fromLonLat([10, 50]));
    view.setZoom(12);

    // A selection, a focused row, a fresh callback: each is a new render with new props.
    for (let i = 0; i < 4; i += 1) {
      rerender(<CandidateMap candidates={page} selected={new Set([i])} focused={i} onPick={() => {}} />);
    }
    expect(built).toHaveLength(1);
    expect(built[0].getTargetElement()).not.toBeNull();
    expect(view.getZoom()).toBe(12);
  });

  it('frames a page of candidates once, and not again when a row is selected', () => {
    const { rerender } = render(<CandidateMap candidates={page} selected={new Set()} />);
    const map = built[0];
    const fit = vi.spyOn(map.getView(), 'fit');
    act(() => map.setSize([400, 300]));
    expect(fit).toHaveBeenCalledTimes(1);

    rerender(<CandidateMap candidates={page} selected={new Set([1])} focused={1} />);
    expect(fit).toHaveBeenCalledTimes(1);

    rerender(<CandidateMap candidates={[candidate(3, 23, 47)]} selected={new Set([1])} />);
    expect(fit).toHaveBeenCalledTimes(2);
  });

  it('reports the source id of the point clicked, through the callback of the latest render', () => {
    const first = vi.fn();
    const latest = vi.fn();
    const { rerender } = render(<CandidateMap candidates={page} selected={new Set()} onPick={first} />);
    rerender(<CandidateMap candidates={page} selected={new Set()} onPick={latest} />);
    const map = built[0];
    const hit = drawnFeatures(map).find((feature) => feature.get('sourceId') === 2);
    expect(hit).toBeDefined();

    click(map, hit);
    expect(latest).toHaveBeenCalledWith(2);
    expect(first).not.toHaveBeenCalled();

    // A click on empty ground picks nothing.
    latest.mockClear();
    click(map, undefined);
    expect(latest).not.toHaveBeenCalled();
  });

  it('redraws the points when the page of candidates changes', () => {
    const { rerender } = render(<CandidateMap candidates={page} selected={new Set()} />);
    const map = built[0];
    expect(sourceIds(map)).toEqual([1, 2]);

    rerender(<CandidateMap candidates={[candidate(3, 23, 47), candidate(4, 23.1, 47.1), candidate(5, 23.2, 47.2)]} selected={new Set()} />);
    expect(sourceIds(map)).toEqual([3, 4, 5]);
  });

  it('disposes its map and every layer on it when it goes', () => {
    const { unmount } = render(<CandidateMap candidates={page} selected={new Set()} />);
    const map = built[0];
    unmount();
    expect(map.getTargetElement()).toBeNull();
    expect(map.getLayers().getLength()).toBe(0);
  });

  it('draws the installation’s background, never a hard-coded tile server', () => {
    render(<CandidateMap candidates={page} selected={new Set()} />);
    const urls = built[0]
      .getAllLayers()
      .flatMap((layer) => (layer.getSource() as { getUrls?: () => string[] | null })?.getUrls?.() ?? []);
    expect(urls.length).toBeGreaterThan(0);
    expect(urls.some((url) => url.includes('tile.openstreetmap.org'))).toBe(false);
  });
});
