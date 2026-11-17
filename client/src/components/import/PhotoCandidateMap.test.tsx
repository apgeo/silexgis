// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render } from '@testing-library/react';
import type Feature from 'ol/Feature';
import type OlMap from 'ol/Map';
import type MapBrowserEvent from 'ol/MapBrowserEvent';
import { fromLonLat } from 'ol/proj';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { PhotoCandidate } from '../../api/hooks.ts';
import { getWorkspaceMap } from '../../map/mapContext.ts';
import PhotoCandidateMap from './PhotoCandidateMap.tsx';

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

const candidate = (key: string, lon: number, lat: number) =>
  ({
    key,
    positionSource: 'exif',
    directionDegrees: 90,
    geom: { type: 'Point', coordinates: [lon, lat] },
  }) as unknown as PhotoCandidate;

const page = [candidate('a', 22.1, 46.1), candidate('b', 22.2, 46.2)];

/** The features on the map's own layers — the background draws tiles, not features. */
function drawnFeatures(map: OlMap): Feature[] {
  return map
    .getAllLayers()
    .flatMap((layer) => (layer.getSource() as { getFeatures?: () => Feature[] } | null)?.getFeatures?.() ?? []);
}

/** The places drawn as points, by key; each place's view cone carries the same key. */
function pointKeys(map: OlMap): string[] {
  return drawnFeatures(map)
    .filter((feature) => !feature.get('isCone'))
    .map((feature) => feature.get('candidateKey') as string)
    .sort();
}

/**
 * A click on the map as OpenLayers delivers it. Hit-detection needs a rendered frame, which jsdom
 * never draws, so the hit is named here; what is under test is what the component does with it.
 */
function click(map: OlMap, hit: Feature | undefined, lonLat: [number, number] = [0, 0]) {
  vi.spyOn(map, 'forEachFeatureAtPixel').mockReturnValue(hit as never);
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

describe('PhotoCandidateMap', () => {
  it('keeps one map and the reader’s view while the table re-renders it', () => {
    const { rerender } = render(<PhotoCandidateMap candidates={page} selected={new Set()} />);
    expect(built).toHaveLength(1);
    const view = built[0].getView();
    view.setCenter(fromLonLat([10, 50]));
    view.setZoom(12);

    // A selection, a focused row, a fresh callback: each is a new render with new props.
    for (let i = 0; i < 4; i += 1) {
      rerender(<PhotoCandidateMap candidates={page} selected={new Set([`${i}`])} focused={`${i}`} onPick={() => {}} />);
    }
    expect(built).toHaveLength(1);
    expect(built[0].getTargetElement()).not.toBeNull();
    expect(view.getZoom()).toBe(12);
  });

  it('frames a page of candidates once, and not again when a row is selected', () => {
    const { rerender } = render(<PhotoCandidateMap candidates={page} selected={new Set()} />);
    const map = built[0];
    const fit = vi.spyOn(map.getView(), 'fit');
    act(() => map.setSize([400, 300]));
    expect(fit).toHaveBeenCalledTimes(1);

    rerender(<PhotoCandidateMap candidates={page} selected={new Set(['a'])} focused="a" />);
    expect(fit).toHaveBeenCalledTimes(1);

    rerender(<PhotoCandidateMap candidates={[candidate('c', 23, 47)]} selected={new Set(['a'])} />);
    expect(fit).toHaveBeenCalledTimes(2);
  });

  it('reports the key of the place clicked, through the callback of the latest render', () => {
    const first = vi.fn();
    const latest = vi.fn();
    const { rerender } = render(<PhotoCandidateMap candidates={page} selected={new Set()} onPick={first} />);
    rerender(<PhotoCandidateMap candidates={page} selected={new Set()} onPick={latest} />);
    const map = built[0];
    const hit = drawnFeatures(map).find((feature) => feature.get('candidateKey') === 'b' && !feature.get('isCone'));
    expect(hit).toBeDefined();

    click(map, hit);
    expect(latest).toHaveBeenCalledWith('b');
    expect(first).not.toHaveBeenCalled();
  });

  it('places the place being positioned where the map is clicked, and selects nothing', () => {
    const onPick = vi.fn();
    const onPlace = vi.fn();
    const { rerender } = render(<PhotoCandidateMap candidates={page} selected={new Set()} onPick={onPick} onPlace={onPlace} />);
    rerender(
      <PhotoCandidateMap candidates={page} selected={new Set()} onPick={onPick} onPlace={onPlace} placingKey="a" />,
    );
    const map = built[0];
    // A click that lands on another place still positions the one being placed.
    const other = drawnFeatures(map).find((feature) => feature.get('candidateKey') === 'b');

    click(map, other, [23.5, 47.25]);
    expect(onPick).not.toHaveBeenCalled();
    expect(onPlace).toHaveBeenCalledTimes(1);
    const [key, [lon, lat]] = onPlace.mock.calls[0] as [string, [number, number]];
    expect(key).toBe('a');
    expect(lon).toBeCloseTo(23.5, 6);
    expect(lat).toBeCloseTo(47.25, 6);

    // Placing turned off, the same click selects again.
    rerender(<PhotoCandidateMap candidates={page} selected={new Set()} onPick={onPick} onPlace={onPlace} placingKey={null} />);
    click(map, other);
    expect(onPick).toHaveBeenCalledWith('b');
    expect(onPlace).toHaveBeenCalledTimes(1);
  });

  it('redraws the places when the candidates change', () => {
    const { rerender } = render(<PhotoCandidateMap candidates={page} selected={new Set()} />);
    const map = built[0];
    expect(pointKeys(map)).toEqual(['a', 'b']);

    rerender(<PhotoCandidateMap candidates={[candidate('c', 23, 47), candidate('d', 23.1, 47.1)]} selected={new Set()} />);
    expect(pointKeys(map)).toEqual(['c', 'd']);
  });

  it('disposes its map and every layer on it when it goes', () => {
    const { unmount } = render(<PhotoCandidateMap candidates={page} selected={new Set()} />);
    const map = built[0];
    unmount();
    expect(map.getTargetElement()).toBeNull();
    expect(map.getLayers().getLength()).toBe(0);
  });

  it('draws the installation’s background, never a hard-coded tile server', () => {
    render(<PhotoCandidateMap candidates={page} selected={new Set()} />);
    const urls = built[0]
      .getAllLayers()
      .flatMap((layer) => (layer.getSource() as { getUrls?: () => string[] | null })?.getUrls?.() ?? []);
    expect(urls.length).toBeGreaterThan(0);
    expect(urls.some((url) => url.includes('tile.openstreetmap.org'))).toBe(false);
  });
});
