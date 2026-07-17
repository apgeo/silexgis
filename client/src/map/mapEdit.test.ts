// SPDX-License-Identifier: AGPL-3.0-or-later
import Map from 'ol/Map';
import View from 'ol/View';
import type Point from 'ol/geom/Point';
import { Draw, Modify, Translate } from 'ol/interaction';
import { toLonLat } from 'ol/proj';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getSurfaceFeatureSource } from './featureLayer.ts';
import { MapEditController, type EditState } from './mapEdit.ts';

// placePointAt/requestPlacement never touch the OL map (no interactions are
// attached), so a bare object stands in for it.
const fakeMap = {} as Map;

/** A real map, for the intents that reach into the interactions attached to one. */
function realMap(): Map {
  return new Map({ view: new View({ center: [0, 0], zoom: 10 }) });
}

function activeDraw(map: Map): Draw {
  const draw = map.getInteractions().getArray().find((i) => i instanceof Draw);
  expect(draw).toBeDefined();
  return draw as Draw;
}

afterEach(() => {
  getSurfaceFeatureSource().clear();
  vi.restoreAllMocks();
});

describe('MapEditController.placePointAt', () => {
  it('creates a pending point feature at the exact coordinate through the undo pipeline', () => {
    const controller = new MapEditController(fakeMap);
    const drawEnded = vi.fn();
    controller.onDrawEnd = drawEnded;
    let state: EditState | undefined;
    controller.subscribe((s) => {
      state = s;
    });

    controller.placePointAt([25.5, 45.25], 7);

    const { created } = controller.getPendingEdits();
    expect(created).toHaveLength(1);
    expect(created[0].geometryType).toBe('Point');
    expect(created[0].feature.get('featureTypeId')).toBe(7);
    expect(created[0].feature.get('pendingNew')).toBe(true);
    const [lon, lat] = toLonLat((created[0].feature.getGeometry() as Point).getCoordinates());
    expect(lon).toBeCloseTo(25.5, 5);
    expect(lat).toBeCloseTo(45.25, 5);
    expect(getSurfaceFeatureSource().getFeatures()).toContain(created[0].feature);
    expect(drawEnded).toHaveBeenCalledWith(created[0].feature);
    expect(state?.dirty).toBe(1);
    expect(state?.canUndo).toBe(true);

    controller.undo();
    expect(controller.getPendingEdits().created).toHaveLength(0);
    expect(getSurfaceFeatureSource().getFeatures()).toHaveLength(0);
    expect(state?.dirty).toBe(0);

    controller.redo();
    expect(controller.getPendingEdits().created).toHaveLength(1);
    expect(state?.dirty).toBe(1);
  });
});

describe('sketch intents', () => {
  // Everything here is what a finger has instead of "double-tap the last vertex": the
  // buttons the mobile sketch bar dispatches. Sketches are driven through OL's own
  // appendCoordinates so the controller sees the same events a tapping user produces.

  it('reports a sketch as active from its first vertex until it is finished', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    const states: EditState[] = [];
    controller.subscribe((s) => states.push(s));

    controller.setMode('draw', 'LineString', 3);
    expect(states.at(-1)?.sketchActive).toBe(false);

    activeDraw(map).appendCoordinates([[0, 0], [1000, 1000]]);
    expect(states.at(-1)?.sketchActive).toBe(true);

    controller.finishDrawing();
    expect(states.at(-1)?.sketchActive).toBe(false);

    // Finishing by button lands the feature exactly as a double-click would: pending,
    // typed, in the undo stack.
    const { created } = controller.getPendingEdits();
    expect(created).toHaveLength(1);
    expect(created[0].geometryType).toBe('LineString');
    expect(created[0].feature.get('featureTypeId')).toBe(3);
    expect(states.at(-1)?.dirty).toBe(1);
    expect(states.at(-1)?.canUndo).toBe(true);
  });

  it('retracts the last vertex without ending the sketch', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    controller.setMode('draw', 'LineString', 1);
    const draw = activeDraw(map);
    draw.appendCoordinates([[0, 0], [1000, 1000], [2000, 0]]);

    controller.removeLastPoint();
    controller.finishDrawing();

    const geometry = controller.getPendingEdits().created[0].feature.getGeometry();
    expect((geometry as never as { getCoordinates: () => number[][] }).getCoordinates()).toHaveLength(2);
  });

  it('abandons the sketch on cancel but leaves the tool armed', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    let state: EditState | undefined;
    controller.subscribe((s) => {
      state = s;
    });

    controller.setMode('draw', 'LineString', 1);
    activeDraw(map).appendCoordinates([[0, 0], [1000, 1000]]);
    controller.abortDrawing();

    expect(state?.sketchActive).toBe(false);
    expect(state?.mode).toBe('draw');
    expect(state?.drawTypeId).toBe(1);
    expect(controller.getPendingEdits().created).toHaveLength(0);
  });

  it('finishes a sketch owned by another interaction, so one button also ends a measurement', () => {
    // The measure buttons add their own Draw to the map; the controller never sees it.
    const map = realMap();
    const controller = new MapEditController(map);
    const foreign = new Draw({ type: 'LineString' });
    const finished = vi.spyOn(foreign, 'finishDrawing');
    map.addInteraction(foreign);

    controller.finishDrawing();

    expect(finished).toHaveBeenCalled();
  });

  it('leaves an inactive Draw alone', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    const foreign = new Draw({ type: 'LineString' });
    foreign.setActive(false);
    const finished = vi.spyOn(foreign, 'finishDrawing');
    map.addInteraction(foreign);

    controller.finishDrawing();

    expect(finished).not.toHaveBeenCalled();
  });
});

describe('MapEditController.removeVertex', () => {
  it('delegates to the attached Modify and reports whether a vertex went', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    controller.setMode('modify');
    const modify = map.getInteractions().getArray().find((i) => i instanceof Modify) as Modify;
    const removePoint = vi.spyOn(modify, 'removePoint').mockReturnValue(true);

    expect(controller.removeVertex()).toBe(true);
    expect(removePoint).toHaveBeenCalled();
  });

  it('answers false when no vertex was touched, so the toolbar can explain the gesture', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    controller.setMode('modify');

    // Nothing has been pointed at: OL has no vertex to remove.
    expect(controller.removeVertex()).toBe(false);
  });

  it('answers false when the modify tool is not even armed', () => {
    const controller = new MapEditController(realMap());
    expect(controller.removeVertex()).toBe(false);
  });
});

describe('pointer-dependent interaction options', () => {
  // OL keeps constructor options private, and these have no behavioural surface without a
  // rendered map and real gestures. Reading the fields is the only way to hold the values
  // still: a finger needs a wider grab than a cursor, and losing that silently makes the
  // tools feel broken on a phone while every test stays green.
  const optionOf = (interaction: object, field: string) =>
    (interaction as unknown as Record<string, number>)[field];

  let coarse = false;

  beforeEach(() => {
    const real = window.matchMedia;
    vi.spyOn(window, 'matchMedia').mockImplementation((query: string) =>
      query.includes('pointer: coarse') && coarse
        ? ({ matches: true, media: query, addEventListener() {}, removeEventListener() {} } as unknown as MediaQueryList)
        : real.call(window, query));
  });

  it('grabs vertices and features tightly for a mouse', () => {
    coarse = false;
    const map = realMap();
    const controller = new MapEditController(map);

    controller.setMode('modify');
    const modify = map.getInteractions().getArray().find((i) => i instanceof Modify)!;
    expect(optionOf(modify, 'pixelTolerance_')).toBe(10);

    controller.setMode('translate');
    const translate = map.getInteractions().getArray().find((i) => i instanceof Translate)!;
    expect(optionOf(translate, 'hitTolerance_')).toBe(4);
  });

  it('widens both for a finger', () => {
    coarse = true;
    const map = realMap();
    const controller = new MapEditController(map);

    controller.setMode('modify');
    const modify = map.getInteractions().getArray().find((i) => i instanceof Modify)!;
    expect(optionOf(modify, 'pixelTolerance_')).toBe(16);

    controller.setMode('translate');
    const translate = map.getInteractions().getArray().find((i) => i instanceof Translate)!;
    expect(optionOf(translate, 'hitTolerance_')).toBe(10);
  });

  it('keeps sketch clicks out of the map selection handler', () => {
    coarse = false;
    const map = realMap();
    const controller = new MapEditController(map);
    controller.setMode('draw', 'LineString', 1);

    // Without stopClick every vertex placed also runs singleclick: the details panel
    // churns mid-draw on desktop, and on touch the finishing double-tap zooms the map.
    expect(optionOf(activeDraw(map), 'stopClick_')).toBe(true);

    controller.setMode('add-cave');
    expect(optionOf(activeDraw(map), 'stopClick_')).toBe(true);
  });
});

describe('MapEditController.requestPlacement', () => {
  it('reports the coordinate through onPointPlaced and disarms', () => {
    const controller = new MapEditController(fakeMap);
    const placed = vi.fn();
    controller.onPointPlaced = placed;
    let state: EditState | undefined;
    controller.subscribe((s) => {
      state = s;
    });

    controller.requestPlacement('add-cave', [22.1, 46.9]);

    expect(placed).toHaveBeenCalledWith('add-cave', [22.1, 46.9]);
    expect(state?.mode).toBe('none');
  });
});
