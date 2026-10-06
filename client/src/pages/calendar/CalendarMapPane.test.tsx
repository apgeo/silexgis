// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import Map from 'ol/Map';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CalendarEntry } from '../../api/hooks.ts';

const { mapSpy, campSpy } = vi.hoisted(() => ({ mapSpy: vi.fn(), campSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useTripLogMap: (...args: unknown[]) => mapSpy(...args),
  useCampAreasMap: (...args: unknown[]) => campSpy(...args),
}));

const { default: CalendarMapPane } = await import('./CalendarMapPane.tsx');

const DRAWN = '11111111-1111-1111-1111-111111111111';
const OTHER = '22222222-2222-2222-2222-222222222222';

const collection = (features: unknown[]) => ({ type: 'FeatureCollection', features });

const point = (id: string, kind: string, lon = 25.6, lat = 45.65) => ({
  type: 'Feature',
  geometry: { type: 'Point', coordinates: [lon, lat] },
  properties: { id, title: 'Coiba Mare recce', tripDate: '2026-09-05', kind },
});

/** A camp's working area as the camp map read answers it: a surface, named, and nothing else. */
const area = (id: string) => ({
  type: 'Feature',
  geometry: {
    type: 'Polygon',
    coordinates: [[[25.5, 45.6], [25.7, 45.6], [25.7, 45.7], [25.5, 45.7], [25.5, 45.6]]],
  },
  properties: { id, name: 'Padiș camp', startDate: '2026-09-10', kind: 'area' },
});

function row(overrides: Partial<CalendarEntry> = {}): CalendarEntry {
  const base: CalendarEntry = {
    source: 'tripLog',
    id: DRAWN,
    title: 'Coiba Mare recce',
    start: '2026-09-05',
    end: null,
    startTime: null,
    endTime: null,
    kind: null,
    state: 'planned',
    placement: 'ahead',
    cavingGroupId: null,
    hasPosition: true,
  };
  return { ...base, ...overrides };
}

const WINDOW = { from: '2026-09-01', to: '2026-09-30' };

/** What the pane last asked the map answer for. */
function lastAsk(): { bbox: string | undefined; from: string; to: string; enabled: boolean } {
  const call = mapSpy.mock.calls.at(-1)!;
  return { bbox: call[0] as string | undefined, from: call[1] as string, to: call[2] as string, enabled: call[3] as boolean };
}

afterEach(cleanup);
beforeEach(() => {
  vi.clearAllMocks();
  mapSpy.mockReturnValue({ data: collection([point(DRAWN, 'sketch')]) });
  campSpy.mockReturnValue({ data: collection([]) });
});

/**
 * Nothing of an OpenLayers map renders under jsdom, so the assertions here are about what the
 * pane asks for, when, and which of the shapes that come back it keeps — which is the whole of
 * what it decides.
 */
describe("the calendar's map", () => {
  it('does not build a map while the pane is not on screen, and does once it is', () => {
    // Asserted on the map's own markup in the container rather than on a call the map might or
    // might not make while being built: a map built against a container with no size measures
    // nothing and draws a blank tile grid that never repairs itself, so what matters is whether
    // there is a map in there at all. The two halves sit in one test so that neither can pass by
    // measuring nothing.
    const hidden = render(<CalendarMapPane entries={[row()]} {...WINDOW} active={false} />);
    expect(hidden.container.querySelector('.ol-viewport')).toBeNull();
    expect(lastAsk().enabled).toBe(false);
    cleanup();

    const shown = render(<CalendarMapPane entries={[row()]} {...WINDOW} active />);
    expect(shown.container.querySelector('.ol-viewport')).not.toBeNull();
    expect(lastAsk().enabled).toBe(true);
  });

  it('asks nothing at all until its container has been measured', () => {
    // Nothing lays anything out under jsdom, so the map never learns a size — which is the same
    // state a real map is in for the moment between being built and its container being
    // measured. A rectangle guessed in that moment is a rectangle nobody is looking at.
    render(<CalendarMapPane entries={[row()]} {...WINDOW} active />);

    expect(lastAsk().bbox).toBeUndefined();
  });

  it('asks over its own rectangle and the days on screen, never over the whole world', () => {
    vi.spyOn(Map.prototype, 'getSize').mockReturnValue([800, 320]);
    render(<CalendarMapPane entries={[row()]} {...WINDOW} active />);

    const ask = lastAsk();
    expect(ask.from).toBe('2026-09-01');
    expect(ask.to).toBe('2026-09-30');
    const bounds = ask.bbox!.split(',').map(Number);
    expect(bounds).toHaveLength(4);
    // The rectangle is a view of a map, not the planet: a world request would be answered under
    // a cap and drawn as though it were everything in view.
    expect(bounds[0]).toBeGreaterThan(-180);
    expect(bounds[2]).toBeLessThan(180);
  });

  it('draws a shape only when the row it belongs to is one of the rows on screen', () => {
    // Both shapes are readable — the answer already decided that. What decides whether one is
    // drawn is whether the reader's own narrowing of the record left its row in.
    mapSpy.mockReturnValue({
      data: collection([point(DRAWN, 'sketch'), point(OTHER, 'sketch', 22.7, 46.5)]),
    });
    render(<CalendarMapPane entries={[row()]} {...WINDOW} active />);

    expect(screen.getByTestId('calendar-map-drawn').textContent).toContain('1');
  });

  it('counts a trip once when it states both where it worked and where its party met', () => {
    mapSpy.mockReturnValue({
      data: collection([point(DRAWN, 'sketch'), point(DRAWN, 'meeting', 25.7, 45.7)]),
    });
    render(<CalendarMapPane entries={[row()]} {...WINDOW} active />);

    expect(screen.getByTestId('calendar-map-drawn').textContent).toContain('1');
  });

  it('draws a camp from the camp answer when its row is on screen, and counts it as a record', () => {
    // The camp's shape comes from its own read and is matched against the camp rows, never the
    // trip rows: a trip row carrying the same identifier would not draw it. One trip and one camp
    // on screen, both answered, is two records.
    campSpy.mockReturnValue({ data: collection([area(OTHER)]) });
    render(
      <CalendarMapPane
        entries={[row(), row({ source: 'expedition', id: OTHER, title: 'Padiș camp' })]}
        {...WINDOW}
        active
      />,
    );

    expect(screen.getByTestId('calendar-map-drawn').textContent).toContain('2');
    // Both reads are asked over the same rectangle and the same days.
    expect(campSpy.mock.calls.at(-1)?.slice(1, 3)).toEqual([WINDOW.from, WINDOW.to]);
  });

  it('does not draw a camp whose row is not on screen, whatever the camp answer holds', () => {
    campSpy.mockReturnValue({ data: collection([area(OTHER)]) });
    render(<CalendarMapPane entries={[row()]} {...WINDOW} active />);

    expect(screen.getByTestId('calendar-map-drawn').textContent).toContain('1');
  });

  it('draws nothing for rows that are not trips, and says so rather than looking broken', () => {
    // The answer carries a shape whose identifier a camp row on screen also carries — which is
    // the case that tells the two apart. A pane matching on identifier alone would draw it; the
    // shapes in this answer belong to trips, and a camp that borrowed one would put a reader at
    // a cave nobody went into on those days. The trip row beside it is drawn, so the assertion
    // is about the source and not about the answer being empty.
    mapSpy.mockReturnValue({
      data: collection([point(OTHER, 'sketch', 22.7, 46.5)]),
    });
    const camps = render(
      <CalendarMapPane
        entries={[row({ source: 'expedition', id: OTHER }), row({ source: 'event', id: OTHER })]}
        {...WINDOW}
        active
      />,
    );

    expect(screen.getByTestId('calendar-map-empty')).toBeTruthy();
    expect(camps.container.querySelector('[data-testid="calendar-map-drawn"]')).toBeNull();
    cleanup();

    render(<CalendarMapPane entries={[row({ id: OTHER })]} {...WINDOW} active />);
    expect(screen.getByTestId('calendar-map-drawn').textContent).toContain('1');
  });

  /**
   * An answer that never came is not an answer that came back empty. "No trip in view has a
   * position recorded" is a claim about the days and the rectangle, and a pane whose request
   * failed has not earned it — a reader would pan away from a region satisfied there was nothing
   * in it.
   */
  it('says a failed map failed, rather than reporting it as an empty stretch of ground', () => {
    mapSpy.mockReturnValue({ data: undefined, isError: true });
    render(<CalendarMapPane entries={[row()]} {...WINDOW} active />);

    expect(screen.getByTestId('calendar-map-failed')).toBeTruthy();
    expect(screen.queryByTestId('calendar-map-empty')).toBeNull();
  });

  /** Nor is an answer still on its way. It has told the pane nothing, so the pane says nothing. */
  it('claims nothing about the ground while the first answer is still on its way', () => {
    mapSpy.mockReturnValue({ data: undefined });
    render(<CalendarMapPane entries={[row()]} {...WINDOW} active />);

    expect(screen.queryByTestId('calendar-map-empty')).toBeNull();
    expect(screen.queryByTestId('calendar-map-failed')).toBeNull();
    expect(screen.queryByTestId('calendar-map-drawn')).toBeNull();
  });

  it('says what it is drawing and what it is leaving out', () => {
    render(<CalendarMapPane entries={[row()]} {...WINDOW} active />);

    expect(screen.getByText(/Only trips and camps are drawn/)).toBeTruthy();
  });
});
