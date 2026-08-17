// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import Polygon, { fromExtent } from 'ol/geom/Polygon';
import { Draw } from 'ol/interaction';
import { transformExtent } from 'ol/proj';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../../i18n';
import TerrainAreaField from './TerrainAreaField.tsx';
import type { TerrainBbox } from './terrainArea.ts';

/**
 * The draw interaction the component attached to its own map.
 *
 * The map and its interactions are the component's private business — nothing renders under jsdom
 * and there is no handle on them from outside — so the attachment itself is watched and the
 * rectangle is then driven through OpenLayers' own API, which is what a dragging user produces.
 */
function attachedDraw(): Draw {
  const draws = vi
    .mocked(Map.prototype.addInteraction)
    .mock.calls.map((call) => call[0])
    .filter((interaction) => interaction instanceof Draw);
  expect(draws.length).toBeGreaterThan(0);
  return draws[draws.length - 1];
}

/** Finishes a box drag over the given degrees, the way the interaction reports one. */
function dragRectangle(draw: Draw, bbox: TerrainBbox) {
  const geometry = new Polygon(
    fromExtent(transformExtent(bbox, 'EPSG:4326', 'EPSG:3857')).getCoordinates(),
  );
  act(() => {
    draw.dispatchEvent({ type: 'drawend', feature: new Feature({ geometry }) } as never);
  });
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe('TerrainAreaField', () => {
  it('offers a way to draw and a way to clear, and says nothing is chosen yet', () => {
    render(<TerrainAreaField value={null} onChange={vi.fn()} />);

    expect(screen.getByRole('button', { name: /Draw rectangle/ })).toBeInTheDocument();
    // Nothing drawn yet, so there is nothing to clear.
    expect(screen.getByRole('button', { name: /Clear rectangle/ })).toBeDisabled();
    expect(screen.getByTestId('terrain-area-extent')).toHaveTextContent('Drag a rectangle');
  });

  it('attaches a draw interaction only once armed, and removes it once a rectangle is drawn', () => {
    vi.spyOn(Map.prototype, 'addInteraction');
    vi.spyOn(Map.prototype, 'removeInteraction');
    render(<TerrainAreaField value={null} onChange={vi.fn()} />);

    expect(vi.mocked(Map.prototype.addInteraction).mock.calls).toHaveLength(0);

    fireEvent.click(screen.getByRole('button', { name: /Draw rectangle/ }));
    const draw = attachedDraw();

    dragRectangle(draw, [25, 45.5, 25.5, 46]);

    // Disarmed by finishing, so the interaction the effect attached is taken off again rather
    // than left listening for a second drag nobody asked for.
    expect(
      vi.mocked(Map.prototype.removeInteraction).mock.calls.filter((call) => call[0] === draw),
    ).toHaveLength(1);
  });

  it('hands out the four degrees of the rectangle that was dragged', () => {
    vi.spyOn(Map.prototype, 'addInteraction');
    const onChange = vi.fn();
    render(<TerrainAreaField value={null} onChange={onChange} />);
    fireEvent.click(screen.getByRole('button', { name: /Draw rectangle/ }));

    dragRectangle(attachedDraw(), [25, 45.5, 25.5, 46]);

    expect(onChange).toHaveBeenCalledTimes(1);
    const [west, south, east, north] = onChange.mock.calls[0][0] as TerrainBbox;
    expect(west).toBeCloseTo(25, 4);
    expect(south).toBeCloseTo(45.5, 4);
    expect(east).toBeCloseTo(25.5, 4);
    expect(north).toBeCloseTo(46, 4);
    expect(screen.getByTestId('terrain-area-extent')).toHaveTextContent('0.25 square degrees');
  });

  it('clears the rectangle by reporting none at all', () => {
    const onChange = vi.fn();
    render(<TerrainAreaField value={[25, 45.5, 25.5, 46]} onChange={onChange} />);

    fireEvent.click(screen.getByRole('button', { name: /Clear rectangle/ }));

    expect(onChange).toHaveBeenCalledWith(null);
  });

  it('disposes the map it built when the page goes away', () => {
    vi.spyOn(Map.prototype, 'dispose');
    vi.spyOn(Map.prototype, 'setTarget');
    const { unmount } = render(<TerrainAreaField value={null} onChange={vi.fn()} />);

    unmount();

    // Detached from the document and then disposed: dropping the reference alone would leave
    // every layer, source and interaction the map owns subscribed for the life of the page.
    expect(
      vi.mocked(Map.prototype.setTarget).mock.calls.filter((call) => call[0] == null),
    ).not.toHaveLength(0);
    expect(vi.mocked(Map.prototype.dispose).mock.calls).not.toHaveLength(0);
  });
});
