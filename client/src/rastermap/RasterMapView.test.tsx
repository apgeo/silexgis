// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import Map from 'ol/Map';
import Observable from 'ol/Observable';
import View from 'ol/View';
import type Point from 'ol/geom/Point';
import VectorSource from 'ol/source/Vector';
import type Feature from 'ol/Feature';
import type { Style } from 'ol/style';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import type { TrackedCaver } from '../caveview/trackedCavers.ts';
import RasterMapView, { type SheetCaverDrawnMarker } from './RasterMapView.tsx';

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

const MARKER = {
  station: 'p.g.7',
  x: 0.25,
  y: 0.25,
  linkId: 'link-1',
  memberId: 'member-1',
  mayEdit: true,
};

const ANA: TrackedCaver = {
  caverId: 'caver-ana',
  name: 'Ana Popescu',
  teamId: null,
  teamTitle: null,
  position: { kind: 'station', station: 'p.g.7' },
  lastRecordedAt: '2026-09-20T10:00:00Z',
  positionAt: '2026-09-20T10:00:00Z',
  enteredAt: null,
  out: false,
};

/** Ana at MARKER's pin, fanned 16px up — already placed, worded and coloured by the pane. */
const ANA_DOT: SheetCaverDrawnMarker = {
  marker: { caver: ANA, station: 'p.g.7', x: 0.25, y: 0.25, offsetPx: [0, 16] },
  label: 'Ana Popescu',
  color: '#3ab5b5',
};

/**
 * `on` is copied off the protected `onInternal` in the Observable constructor, so the
 * spy has to stand there (before any map is built) to see every listener registration —
 * reached through an unknown-cast because the protection is a compile-time courtesy the
 * test deliberately walks past.
 */
const observableInternals = Observable.prototype as unknown as {
  onInternal: (type: string, listener: (event: unknown) => unknown) => unknown;
};

/** The singleclick listener the built map holds, driven directly with a synthetic event. */
function singleclickHandler(): (event: { coordinate: number[] }) => void {
  const spy = vi.mocked(observableInternals.onInternal);
  const call = spy.mock.calls.find(([type]) => type === 'singleclick');
  if (call === undefined) {
    throw new Error('no singleclick listener was attached');
  }
  const listener = call[1];
  return (event) => listener(event);
}

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
  vi.spyOn(observableInternals, 'onInternal');
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

describe('authoring clicks', () => {
  it('answers a placement click in stored fractions — the same frame the pins are stored in', async () => {
    const onMapClick = vi.fn();
    render(
      <RasterMapView
        imageUrl="http://files.local/map"
        alt="Sheet A"
        markers={[]}
        active
        onMapClick={onMapClick}
      />,
    );
    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(1));

    // A click at OL [1000, 750] on the 4000×1000 picture is a quarter across and — the
    // flip — a quarter DOWN in the stored frame. This is the write-side twin of the
    // marker-drawing assertion above: place at the click, read back at the click.
    singleclickHandler()({ coordinate: [1000, 750] });
    expect(onMapClick).toHaveBeenCalledWith({ x: 0.25, y: 0.25 });
  });

  it('refuses a click outside the picture rather than pinning an edge nobody pointed at', async () => {
    const onMapClick = vi.fn();
    render(
      <RasterMapView
        imageUrl="http://files.local/map"
        alt="Sheet A"
        markers={[]}
        active
        onMapClick={onMapClick}
      />,
    );
    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(1));

    singleclickHandler()({ coordinate: [-50, 500] });
    expect(onMapClick).not.toHaveBeenCalled();
  });

  it('sends a click that lands on a marker to the marker handler, not the placement one', async () => {
    const onMapClick = vi.fn();
    const onMarkerClick = vi.fn();
    render(
      <RasterMapView
        imageUrl="http://files.local/map"
        alt="Sheet A"
        markers={[MARKER]}
        active
        onMapClick={onMapClick}
        onMarkerClick={onMarkerClick}
      />,
    );
    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(1));

    // Dead on the marker's drawn coordinate (the flip applied): the marker answers,
    // and the click point rides along in stored fractions — here the marker's own.
    singleclickHandler()({ coordinate: [1000, 750] });
    expect(onMarkerClick).toHaveBeenCalledWith(MARKER, { x: 0.25, y: 0.25 });
    expect(onMapClick).not.toHaveBeenCalled();

    // …and far from it, the sheet answers: the split is position, not registration order.
    singleclickHandler()({ coordinate: [3000, 200] });
    expect(onMapClick).toHaveBeenCalledWith({ x: 0.75, y: 0.8 });
  });

  it('a click inside a marker halo still reports where the click itself landed', async () => {
    const onMarkerClick = vi.fn();
    render(
      <RasterMapView
        imageUrl="http://files.local/map"
        alt="Sheet A"
        markers={[MARKER]}
        active
        onMarkerClick={onMarkerClick}
      />,
    );
    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(1));

    // Near the marker, not on it: within tolerance the marker answers, but the reported
    // point is the click's own fractions — a caller placing a *different* station there
    // must get the spot the author aimed at, never the neighbor's stored point.
    singleclickHandler()({ coordinate: [1004, 752] });
    expect(onMarkerClick).toHaveBeenCalledWith(MARKER, {
      x: 1004 / IMAGE.width,
      y: (IMAGE.height - 752) / IMAGE.height,
    });
  });

  it('a read-only mount ignores clicks entirely and shows no writing cursor', async () => {
    render(
      <RasterMapView imageUrl="http://files.local/map" alt="Sheet A" markers={[MARKER]} active />,
    );
    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(1));

    // The listener is attached but answers nobody — there is nobody to answer.
    expect(() => singleclickHandler()({ coordinate: [1000, 750] })).not.toThrow();
    expect(screen.getByTestId('rastermap-map').style.cursor).toBe('');
  });
});

describe('the party on the sheet', () => {
  it('draws a caver dot at its pin’s geometry, worded with the given line', async () => {
    render(
      <RasterMapView
        imageUrl="http://files.local/map"
        alt="Sheet A"
        markers={[MARKER]}
        cavers={[ANA_DOT]}
        active
      />,
    );

    // The dot's geometry IS the pin — the fan is style pixels, never a position claim —
    // so the drawn set is the pin's coordinate twice: once as the point, once as Ana.
    await waitFor(() =>
      expect(drawnPoints()).toEqual([
        { coordinate: [1000, 750], label: '7' },
        { coordinate: [1000, 750], label: 'Ana Popescu' },
      ]),
    );
  });

  it('says nothing on the dot when the pane sent no words', async () => {
    render(
      <RasterMapView
        imageUrl="http://files.local/map"
        alt="Sheet A"
        markers={[]}
        cavers={[{ ...ANA_DOT, label: null }]}
        active
      />,
    );

    await waitFor(() =>
      expect(drawnPoints()).toEqual([{ coordinate: [1000, 750], label: undefined }]),
    );
  });

  it('answers a press on the fanned dot with the person, and on the pin with the point', async () => {
    const onCaverClick = vi.fn();
    const onMarkerClick = vi.fn();
    // Pinned so the fan's pixel offset is the same number in map units: the dot is drawn
    // a fixed 16px from its pin whatever the zoom, and the press math follows the zoom.
    vi.spyOn(View.prototype, 'getResolution').mockReturnValue(1);
    render(
      <RasterMapView
        imageUrl="http://files.local/map"
        alt="Sheet A"
        markers={[MARKER]}
        cavers={[ANA_DOT]}
        active
        onCaverClick={onCaverClick}
        onMarkerClick={onMarkerClick}
      />,
    );
    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(1));

    // On the dot as displaced — 16px up from the pin at this resolution — the person
    // answers, and the pin under the fan does not swallow the press.
    singleclickHandler()({ coordinate: [1000, 766] });
    expect(onCaverClick).toHaveBeenCalledWith(ANA_DOT.marker);
    expect(onMarkerClick).not.toHaveBeenCalled();

    // Dead on the pin itself, outside the dot's reach, the point answers as ever.
    singleclickHandler()({ coordinate: [1000, 750] });
    expect(onMarkerClick).toHaveBeenCalledWith(MARKER, { x: 0.25, y: 0.25 });
    expect(onCaverClick).toHaveBeenCalledTimes(1);
  });

  it('answers a press on the dot when the person is the ONLY listener aboard', async () => {
    // The reader's mount: no placement click, no station press — a viewer without edit
    // rights, or a coordinator replaying a watch no longer armed, gets neither. The
    // caver card must still open, so the person's listener alone keeps the press alive.
    const onCaverClick = vi.fn();
    vi.spyOn(View.prototype, 'getResolution').mockReturnValue(1);
    render(
      <RasterMapView
        imageUrl="http://files.local/map"
        alt="Sheet A"
        markers={[MARKER]}
        cavers={[ANA_DOT]}
        active
        onCaverClick={onCaverClick}
      />,
    );
    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(1));

    singleclickHandler()({ coordinate: [1000, 766] });
    expect(onCaverClick).toHaveBeenCalledWith(ANA_DOT.marker);

    // Beside the dot, the press still answers nobody — there is nobody else to answer.
    singleclickHandler()({ coordinate: [3000, 200] });
    expect(onCaverClick).toHaveBeenCalledTimes(1);
  });

  it('glides the view to a focus point, and asks for no flight without one', async () => {
    const animate = vi.spyOn(View.prototype, 'animate').mockImplementation(() => undefined);
    const { rerender } = render(
      <RasterMapView
        imageUrl="http://files.local/map"
        alt="Sheet A"
        markers={[MARKER]}
        active
        focus={null}
      />,
    );
    await waitFor(() => expect(View.prototype.fit).toHaveBeenCalledTimes(1));
    expect(animate).not.toHaveBeenCalled();

    rerender(
      <RasterMapView
        imageUrl="http://files.local/map"
        alt="Sheet A"
        markers={[MARKER]}
        active
        focus={{ x: 0.25, y: 0.25 }}
      />,
    );

    await waitFor(() =>
      expect(animate).toHaveBeenCalledWith({ center: [1000, 750], duration: 300 }),
    );
  });
});
