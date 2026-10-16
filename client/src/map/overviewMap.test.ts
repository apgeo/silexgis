// SPDX-License-Identifier: AGPL-3.0-or-later
import Map from 'ol/Map';
import View from 'ol/View';
import OverviewMap from 'ol/control/OverviewMap';
import TileLayer from 'ol/layer/Tile';
import XYZ from 'ol/source/XYZ';
import { describe, expect, it, vi } from 'vitest';
import { attachOverviewMap } from './overviewMap.ts';

function setUp(open: boolean) {
  const map = new Map({ view: new View({ center: [0, 0], zoom: 8 }) });
  const shown = new TileLayer({ source: new XYZ({ url: 'https://tiles.invalid/a/{z}/{x}/{y}.png' }), visible: true });
  const hidden = new TileLayer({ source: new XYZ({ url: 'https://tiles.invalid/b/{z}/{x}/{y}.png' }), visible: false });
  const onToggle = vi.fn();
  const handle = attachOverviewMap(map, [shown, hidden], { open, label: 'Overview map', onToggle });
  const overview = () => map.getControls().getArray().find((c): c is OverviewMap => c instanceof OverviewMap);
  // Found where the map put the control, since a control keeps its own element to itself.
  const button = () => map.getOverlayContainerStopEvent().querySelector<HTMLButtonElement>('.map-overview button')!;
  return { map, shown, hidden, onToggle, handle, overview, button };
}

describe('the overview inset', () => {
  it('draws the base layer the map is showing, and follows a switch to another', () => {
    const { shown, hidden, overview } = setUp(true);
    const mirrors = overview()!.getOverviewMap().getLayers().getArray() as TileLayer<XYZ>[];

    expect(mirrors.map((layer) => layer.getVisible())).toEqual([true, false]);
    // The same tile sources: the inset fetches nothing the map is not already fetching.
    expect(mirrors[0].getSource()).toBe(shown.getSource());

    shown.setVisible(false);
    hidden.setVisible(true);
    expect(mirrors.map((layer) => layer.getVisible())).toEqual([false, true]);
  });

  it('closed, puts no second map in the page — only the button that opens one', () => {
    const { map, overview, button } = setUp(false);

    expect(overview()).toBeUndefined();
    // Nothing inside the map's own viewport is a map viewport of its own.
    expect(map.getViewport().querySelectorAll('.ol-viewport')).toHaveLength(0);
    expect(button().getAttribute('aria-label')).toBe('Overview map');
    expect(button().getAttribute('aria-expanded')).toBe('false');
  });

  it('opens and closes from its button, says which it is, and reports each toggle', () => {
    const { map, overview, button, onToggle } = setUp(false);

    button().click();
    expect(overview()).toBeDefined();
    expect(map.getViewport().querySelectorAll('.ol-viewport')).toHaveLength(1);
    expect(button().getAttribute('aria-expanded')).toBe('true');
    expect(onToggle).toHaveBeenLastCalledWith(true);

    button().click();
    expect(overview()).toBeUndefined();
    expect(button().getAttribute('aria-expanded')).toBe('false');
    expect(onToggle).toHaveBeenLastCalledWith(false);
  });

  it('opens and closes when told to from outside, without reporting it as a toggle', () => {
    // The account's own choice arrives once its preferences have been read, after the map is drawn.
    const { overview, handle, onToggle, button } = setUp(false);

    handle.setOpen(true);
    expect(overview()).toBeDefined();
    expect(button().getAttribute('aria-expanded')).toBe('true');
    handle.setOpen(true);
    expect(overview()).toBeDefined();

    handle.setOpen(false);
    expect(overview()).toBeUndefined();
    expect(onToggle).not.toHaveBeenCalled();
  });

  it('comes off the map when detached', () => {
    const { map, handle } = setUp(true);
    handle.detach();
    expect(map.getControls().getArray().some((c) => c instanceof OverviewMap)).toBe(false);
    expect(map.getOverlayContainerStopEvent().querySelector('.map-overview')).toBeNull();
  });
});
