// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render } from '@testing-library/react';
import Feature from 'ol/Feature';
import type Map from 'ol/Map';
import Point from 'ol/geom/Point';
import type TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import type XYZ from 'ol/source/XYZ';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { MapLayerInfo } from '../../api/hooks.ts';
import { useSmallMap, type SmallMap, type SmallMapOptions } from './useSmallMap.ts';

// The catalogue as the hook reads it; each test says what the server has answered so far.
const catalogue = vi.hoisted(() => ({
  current: { isPending: true, data: undefined as MapLayerInfo[] | undefined },
}));
vi.mock('../../api/hooks.ts', async (original) => ({
  ...(await original<typeof import('../../api/hooks.ts')>()),
  useMapLayers: () => catalogue.current,
}));

const base = (id: number, url: string) =>
  ({ id, name: `Base ${id}`, layerKind: 'xyz', urlTemplate: url, isBase: true, isDefault: true }) as MapLayerInfo;

let last: SmallMap;

function Harness({ options, showTarget = true, slot = 'a' }: { options: SmallMapOptions; showTarget?: boolean; slot?: string }) {
  last = useSmallMap(options);
  // A different key mounts a different element, as a dialog moved into a drawer does.
  return showTarget ? <div key={slot} data-testid={`slot-${slot}`} ref={last.targetRef} /> : null;
}

function optionsWith(source: VectorSource, extra: Partial<SmallMapOptions> = {}): SmallMapOptions {
  return { center: [25.6, 45.65], zoom: 8, layers: () => [new VectorLayer({ source })], ...extra };
}

function backgroundUrls(map: Map): string[] {
  return map
    .getLayers()
    .getArray()
    .filter((layer) => !(layer instanceof VectorLayer))
    .flatMap((layer) => (layer as TileLayer<XYZ>).getSource()?.getUrls() ?? []);
}

beforeEach(() => {
  catalogue.current = { isPending: true, data: undefined };
});
afterEach(cleanup);

describe('useSmallMap', () => {
  it('keeps one map and the reader’s view through any number of re-renders', () => {
    const source = new VectorSource();
    const { rerender } = render(<Harness options={optionsWith(source)} />);
    const first = last.map.current!;
    expect(first).toBeTruthy();
    expect(last.ready).toBe(true);
    first.getView().setCenter(fromLonLat([10, 50]));
    first.getView().setZoom(12);

    // Every render hands the hook fresh options and a fresh layer factory, as a page's would.
    for (let i = 0; i < 5; i += 1) {
      rerender(<Harness options={optionsWith(source)} />);
    }
    expect(last.map.current).toBe(first);
    expect(first.getView().getZoom()).toBe(12);
    expect(first.getTargetElement()).not.toBeNull();
  });

  it('leaves no layer, listener or target behind when its owner unmounts', () => {
    const source = new VectorSource();
    const onClick = vi.fn();
    const { unmount } = render(
      <Harness options={optionsWith(source, { onCreate: (map) => map.on('click', onClick) })} />,
    );
    const map = last.map.current!;
    expect(source.hasListener('change')).toBe(true);
    expect(map.hasListener('click')).toBe(true);

    unmount();
    expect(map.getLayers().getLength()).toBe(0);
    expect(map.getTargetElement()).toBeNull();
    expect(map.hasListener('click')).toBe(false);
    // The source outlives the map in its owner; no disposed layer may still be drawing it.
    expect(source.hasListener('change')).toBe(false);
  });

  it('exists only while active, and builds afresh when made active again', () => {
    const source = new VectorSource();
    const { rerender } = render(<Harness options={optionsWith(source, { active: false })} />);
    expect(last.map.current).toBeNull();
    expect(last.ready).toBe(false);

    rerender(<Harness options={optionsWith(source, { active: true })} />);
    const first = last.map.current!;
    expect(last.ready).toBe(true);

    rerender(<Harness options={optionsWith(source, { active: false })} />);
    expect(last.map.current).toBeNull();
    expect(last.ready).toBe(false);
    expect(first.getLayers().getLength()).toBe(0);
    expect(source.hasListener('change')).toBe(false);

    rerender(<Harness options={optionsWith(source, { active: true })} />);
    expect(last.map.current).not.toBe(first);
    expect(source.hasListener('change')).toBe(true);
  });

  it('builds once its container appears after the first render, and drops the map when it goes', () => {
    const source = new VectorSource();
    const { rerender } = render(<Harness options={optionsWith(source)} showTarget={false} />);
    expect(last.map.current).toBeNull();

    rerender(<Harness options={optionsWith(source)} showTarget />);
    expect(last.map.current).not.toBeNull();

    rerender(<Harness options={optionsWith(source)} showTarget={false} />);
    expect(last.map.current).toBeNull();
    expect(source.hasListener('change')).toBe(false);
  });

  it('moves the same map into a container that replaces its own', () => {
    const source = new VectorSource();
    const { rerender, getByTestId } = render(<Harness options={optionsWith(source)} slot="a" />);
    const map = last.map.current!;

    rerender(<Harness options={optionsWith(source)} slot="b" />);
    expect(last.map.current).toBe(map);
    expect(map.getTargetElement()).toBe(getByTestId('slot-b'));
  });

  it('frames each set of things once, after the map is measured, and leaves the reader’s panning alone', () => {
    const source = new VectorSource();
    render(<Harness options={optionsWith(source)} />);
    const map = last.map.current!;
    const view = map.getView();
    const fit = vi.spyOn(view, 'fit');
    const extent = new Point(fromLonLat([22, 46])).getExtent();

    // Not laid out yet: framing waits for the first measurement instead of guessing a size.
    act(() => last.frame('set-1', extent));
    expect(fit).not.toHaveBeenCalled();
    act(() => map.setSize([400, 300]));
    expect(fit).toHaveBeenCalledTimes(1);

    // A redraw of the same set — a row selected, a row hovered — does not move the camera.
    act(() => last.frame('set-1', extent));
    expect(fit).toHaveBeenCalledTimes(1);

    // An empty set frames nothing and is not remembered as framed.
    act(() => last.frame('set-2', source.getExtent()));
    expect(fit).toHaveBeenCalledTimes(1);
    act(() => last.frame('set-2', extent, { maxZoom: 10 }));
    expect(fit).toHaveBeenCalledTimes(2);
    expect(fit.mock.calls[1][1]).toMatchObject({ maxZoom: 10, padding: [32, 32, 32, 32] });
  });

  it('draws the catalogue’s background under its own layers once the catalogue answers', () => {
    const source = new VectorSource({ features: [new Feature(new Point([0, 0]))] });
    const { rerender } = render(<Harness options={optionsWith(source)} />);
    const map = last.map.current!;
    // Nothing fetched while the catalogue is still on its way.
    expect(map.getLayers().getLength()).toBe(1);

    catalogue.current = { isPending: false, data: [base(4, 'https://tiles.example/{z}/{x}/{y}.png')] };
    rerender(<Harness options={optionsWith(source)} />);
    expect(map.getLayers().getLength()).toBe(2);
    expect(map.getLayers().item(0)).not.toBeInstanceOf(VectorLayer);
    expect(backgroundUrls(map)).toEqual(['https://tiles.example/{z}/{x}/{y}.png']);
  });

  it('draws only a background of a kind its owner accepts', () => {
    catalogue.current = {
      isPending: false,
      data: [
        { ...base(1, 'https://service.example/wms'), layerKind: 'wms', wms: {} } as MapLayerInfo,
        { ...base(2, 'https://tiles.example/{z}/{x}/{y}.png'), isDefault: false },
      ],
    };
    render(
      <Harness options={optionsWith(new VectorSource(), { backgrounds: (entry) => entry.layerKind === 'xyz' })} />,
    );
    expect(backgroundUrls(last.map.current!)).toEqual(['https://tiles.example/{z}/{x}/{y}.png']);
  });

  it('falls back to OpenTopoMap when the installation names no background', () => {
    catalogue.current = { isPending: false, data: [] };
    render(<Harness options={optionsWith(new VectorSource())} />);
    const urls = backgroundUrls(last.map.current!);
    expect(urls.length).toBeGreaterThan(0);
    expect(urls.every((url) => url.includes('opentopomap.org'))).toBe(true);
    expect(urls.some((url) => url.includes('tile.openstreetmap.org'))).toBe(false);
  });
});
