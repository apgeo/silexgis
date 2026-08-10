// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import Map from 'ol/Map';
import { Draw, Modify } from 'ol/interaction';
import { fromLonLat } from 'ol/proj';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import TripGeometryField from './TripGeometryField.tsx';
import type { TripGeometry } from './tripGeometry.ts';

const point = { type: 'Point', coordinates: [25.6, 45.65] } as unknown as TripGeometry;

/**
 * The draw interaction the component attached to its own map.
 *
 * The map and its interactions are the component's private business — nothing renders under
 * jsdom and there is no handle on them from outside — so the attachment itself is watched and
 * the sketch is then driven through OpenLayers' own API, which is what a drawing user produces.
 */
function attachedDraw(): Draw {
  const spy = vi.mocked(Map.prototype.addInteraction);
  const draws = spy.mock.calls.map((call) => call[0]).filter((i) => i instanceof Draw);
  expect(draws.length).toBeGreaterThan(0);
  return draws[draws.length - 1];
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe('TripGeometryField', () => {
  it('offers the three shapes and a way to clear, and warns either way', () => {
    render(<TripGeometryField value={null} onChange={vi.fn()} />);

    expect(screen.getByRole('button', { name: /Point$/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Line$/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Area$/ })).toBeInTheDocument();
    // Nothing drawn yet, so there is nothing to clear.
    expect(screen.getByRole('button', { name: /Clear shape/ })).toBeDisabled();
    expect(screen.getByTestId('trip-geometry-warning')).toBeInTheDocument();
  });

  it('reads a trip that has no shape and one that has one', () => {
    const { unmount } = render(<TripGeometryField value={null} onChange={vi.fn()} />);
    expect(screen.getByRole('button', { name: /Clear shape/ })).toBeDisabled();
    unmount();

    render(<TripGeometryField value={point} onChange={vi.fn()} />);
    expect(screen.getByRole('button', { name: /Clear shape/ })).toBeEnabled();
  });

  it('clears the shape by reporting no geometry at all', () => {
    const onChange = vi.fn();
    render(<TripGeometryField value={point} onChange={onChange} />);

    fireEvent.click(screen.getByRole('button', { name: /Clear shape/ }));

    expect(onChange).toHaveBeenCalledWith(null);
  });

  it('draws only, when read only — and still carries the warning', () => {
    render(<TripGeometryField value={point} readOnly />);

    expect(screen.queryByRole('button', { name: /Point$/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Clear shape/ })).not.toBeInTheDocument();
    expect(screen.getByTestId('trip-geometry-map')).toBeInTheDocument();
    // The reader meets the shape here, so the note about it not being obfuscated belongs here too.
    expect(screen.getByTestId('trip-geometry-warning')).toBeInTheDocument();
  });

  it('reports a drawn line as WGS84 the API can take', () => {
    vi.spyOn(Map.prototype, 'addInteraction');
    const onChange = vi.fn();
    render(<TripGeometryField value={null} onChange={onChange} />);

    fireEvent.click(screen.getByRole('button', { name: /Line$/ }));
    const draw = attachedDraw();
    // Wrapped: finishing publishes the shape and puts the tool down, both of which are state.
    act(() => {
      draw.appendCoordinates([fromLonLat([25.6, 45.65]), fromLonLat([25.7, 45.7])]);
      draw.finishDrawing();
    });

    expect(onChange).toHaveBeenCalledTimes(1);
    const written = onChange.mock.calls[0][0] as { type: string; coordinates: number[][] };
    expect(written.type).toBe('LineString');
    expect(written.coordinates[0][0]).toBeCloseTo(25.6, 5);
    expect(written.coordinates[0][1]).toBeCloseTo(45.65, 5);
    expect(written.coordinates[1][0]).toBeCloseTo(25.7, 5);
    expect(written.coordinates[1][1]).toBeCloseTo(45.7, 5);
    // Finishing puts the tool down, so the next click on the map does not start a second shape.
    expect(screen.getByRole('button', { name: /Line$/ })).toHaveClass(/ant-btn-default/);
  });

  it('draws one shape at a time — a new sketch replaces the one before it', () => {
    vi.spyOn(Map.prototype, 'addInteraction');
    const onChange = vi.fn();
    render(<TripGeometryField value={point} onChange={onChange} />);

    fireEvent.click(screen.getByRole('button', { name: /Line$/ }));
    const draw = attachedDraw();
    // Wrapped: finishing publishes the shape and puts the tool down, both of which are state.
    act(() => {
      draw.appendCoordinates([fromLonLat([25.6, 45.65]), fromLonLat([25.7, 45.7])]);
      draw.finishDrawing();
    });

    const written = onChange.mock.calls[0][0] as { type: string };
    expect(written.type).toBe('LineString');
  });

  it('builds its map once, however often the toolbar changes', () => {
    vi.spyOn(Map.prototype, 'addInteraction');
    vi.spyOn(Map.prototype, 'setTarget');
    render(<TripGeometryField value={null} onChange={vi.fn()} />);

    const modifies = () =>
      vi.mocked(Map.prototype.addInteraction).mock.calls.filter((call) => call[0] instanceof Modify).length;
    const detachments = () =>
      vi.mocked(Map.prototype.setTarget).mock.calls.filter((call) => call[0] == null).length;
    expect(modifies()).toBe(1);

    // Arming and disarming a tool is state, so React re-renders — and a map rebuilt on re-render
    // would throw away wherever the user had panned and zoomed to before reaching for the tool.
    fireEvent.click(screen.getByRole('button', { name: /Point$/ }));
    fireEvent.click(screen.getByRole('button', { name: /Point$/ }));

    expect(modifies()).toBe(1);
    expect(detachments()).toBe(0);
  });

  it('says what the warning says, when it is asked', () => {
    render(<TripGeometryField value={point} readOnly />);

    fireEvent.click(screen.getByTestId('trip-geometry-warning'));

    expect(screen.getByText(/never approximated/)).toBeInTheDocument();
    expect(screen.getByText(/discloses that entrance/)).toBeInTheDocument();
  });
});
