// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import Map from 'ol/Map';
import View from 'ol/View';
import type Point from 'ol/geom/Point';
import VectorSource from 'ol/source/Vector';
import type Feature from 'ol/Feature';
import type { Style } from 'ol/style';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import RasterMapView from './RasterMapView.tsx';

/**
 * The image the component probes for its natural size, faked because jsdom loads nothing.
 * Deliberately not square, so a missing y-flip cannot hide behind symmetry.
 */
const IMAGE = { width: 4000, height: 1000 };
let failLoads = false;

class FakeImage {
  onload: (() => void) | null = null;
  onerror: (() => void) | null = null;
  naturalWidth = IMAGE.width;
  naturalHeight = IMAGE.height;
  set src(_value: string) {
    queueMicrotask(() => (failLoads ? this.onerror?.() : this.onload?.()));
  }
}

const MARKER = { station: 'p.g.7', x: 0.25, y: 0.25, linkId: 'link-1', memberId: 'member-1' };

/** Every point feature handed to any vector source, with its style read back. */
function drawnPoints(): { coordinate: number[]; label: string | undefined }[] {
  const spy = vi.mocked(VectorSource.prototype.addFeature);
  return spy.mock.calls.map(([feature]) => {
    const typed = feature as Feature<Point>;
    const style = typed.getStyle() as Style;
    return {
      coordinate: typed.getGeometry()!.getCoordinates(),
      label: style.getText()?.getText() as string | undefined,
    };
  });
}

beforeEach(() => {
  failLoads = false;
  vi.stubGlobal('Image', FakeImage);
  vi.spyOn(VectorSource.prototype, 'addFeature');
  vi.spyOn(View.prototype, 'fit').mockImplementation(() => undefined);
  vi.spyOn(Map.prototype, 'updateSize');
  vi.spyOn(Map.prototype, 'dispose');
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('RasterMapView', () => {
  it('builds the map once the picture has told it its size, and draws it into the target', async () => {
    render(<RasterMapView imageUrl="http://files.local/map" alt="Sheet A" markers={[]} active />);

    // The fit call is the build's signature: one per map, framing the whole picture.
    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(1));
    expect(screen.getByTestId('rastermap-map')).toBeInTheDocument();
  });

  it('builds nothing while inactive: a hidden pane measures 0×0 and would frame nothing', async () => {
    const { rerender } = render(
      <RasterMapView imageUrl="http://files.local/map" alt="Sheet A" markers={[]} active={false} />,
    );

    // The size probe resolves either way; the map still must not be built.
    await act(async () => {});
    expect(View.prototype.fit).not.toHaveBeenCalled();

    // The positive twin: the same mounted component builds the moment it is activated.
    rerender(<RasterMapView imageUrl="http://files.local/map" alt="Sheet A" markers={[]} active />);
    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(1));
  });

  it('re-measures on reactivation instead of rebuilding', async () => {
    const { rerender } = render(
      <RasterMapView imageUrl="http://files.local/map" alt="Sheet A" markers={[]} active />,
    );
    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(1));

    rerender(
      <RasterMapView imageUrl="http://files.local/map" alt="Sheet A" markers={[]} active={false} />,
    );
    rerender(<RasterMapView imageUrl="http://files.local/map" alt="Sheet A" markers={[]} active />);

    // Told to re-measure — its pane was display:none and OL's cached size is stale — but
    // never rebuilt: a rebuild would call fit again and lose wherever the reader panned.
    await waitFor(() => expect(Map.prototype.updateSize).toHaveBeenCalled());
    expect(View.prototype.fit).toHaveBeenCalledTimes(1);
  });

  it('rebuilds against the new picture when the image URL changes', async () => {
    const { rerender } = render(
      <RasterMapView imageUrl="http://files.local/map-v1" alt="Sheet A" markers={[]} active />,
    );
    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(1));

    // The document's current file was replaced while the pane sat mounted. The first map
    // still holds the v1 bytes in the v1 extent, and the pane has already switched its
    // marker filter to the v2 file — drawing those pins over the old scan would mislocate
    // every one of them. So the map must be disposed and built again, not re-measured.
    rerender(
      <RasterMapView imageUrl="http://files.local/map-v2" alt="Sheet A" markers={[]} active />,
    );

    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(2));
    expect(Map.prototype.dispose).toHaveBeenCalledTimes(1);
  });

  it('draws a marker at the flipped pixel coordinate, labelled with the short station name', async () => {
    render(
      <RasterMapView imageUrl="http://files.local/map" alt="Sheet A" markers={[MARKER]} active />,
    );

    // Stored (0.25, 0.25) — a quarter across, a quarter DOWN — lands a quarter across and
    // three quarters UP the OL extent of the 4000×1000 picture. This is the one boundary
    // where the stored frame meets OL's, and this assertion is what pins the flip to it.
    await waitFor(() =>
      expect(drawnPoints()).toEqual([{ coordinate: [1000, 750], label: '7' }]),
    );
  });

  it('says the picture cannot be shown rather than presenting an empty map', async () => {
    failLoads = true;
    render(<RasterMapView imageUrl="http://files.local/map" alt="Sheet A" markers={[]} active />);

    await waitFor(() => expect(screen.getByTestId('rastermap-missing')).toBeInTheDocument());
    expect(View.prototype.fit).not.toHaveBeenCalled();
  });
});
