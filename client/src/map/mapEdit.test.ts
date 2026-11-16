// SPDX-License-Identifier: AGPL-3.0-or-later
import Collection from 'ol/Collection';
import Map from 'ol/Map';
import View from 'ol/View';
import Feature from 'ol/Feature';
import LineString from 'ol/geom/LineString';
import Point from 'ol/geom/Point';
import { Draw, Modify, Snap, Translate } from 'ol/interaction';
import { DrawEvent } from 'ol/interaction/Draw';
import { ModifyEvent } from 'ol/interaction/Modify';
import { TranslateEvent } from 'ol/interaction/Translate';
import { toLonLat } from 'ol/proj';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getSurfaceFeatureSource } from './featureLayer.ts';
import { MapEditController, type EditState } from './mapEdit.ts';

// A reshape of a saved feature reads the version it began from; no server answers here.
vi.mock('../api/hooks.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/hooks.ts')>()),
  fetchFeatureVersion: vi.fn(async () => undefined),
}));

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

  it('refuses to finish a line that has only one vertex down', () => {
    // OL guards the finishing *gesture* by vertex count but not finishDrawing(), which
    // would end this as a one-coordinate LineString: accepted here, rejected by the
    // server on save, with nothing to point the user at by then.
    const map = realMap();
    const controller = new MapEditController(map);
    controller.setMode('draw', 'LineString', 1);
    activeDraw(map).appendCoordinates([[0, 0]]);

    expect(controller.finishDrawing()).toBe(false);
    expect(controller.getPendingEdits().created).toHaveLength(0);

    // The sketch survives the refusal, so the next tap continues the same shape.
    activeDraw(map).appendCoordinates([[1000, 1000]]);
    expect(controller.finishDrawing()).toBe(true);
    expect(controller.getPendingEdits().created).toHaveLength(1);
  });

  it('refuses to finish a ring that would enclose no area', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    controller.setMode('draw', 'Polygon', 1);
    activeDraw(map).appendCoordinates([[0, 0], [1000, 1000]]);

    // Two vertices close into a flat, zero-area ring — a valid triangle needs three.
    expect(controller.finishDrawing()).toBe(false);
    expect(controller.getPendingEdits().created).toHaveLength(0);
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

describe('what the reshaping tools may take hold of', () => {
  const featureWithId = (id: string) => {
    const feature = new Feature(new LineString([[0, 0], [100, 100]]));
    feature.set('id', id);
    return feature;
  };

  const attached = (map: Map, kind: typeof Modify | typeof Translate) =>
    map.getInteractions().getArray().find((i) => i instanceof kind)!;

  /** The filter an attached interaction applies to a feature it could take hold of. */
  function takesHold(map: Map, kind: typeof Modify | typeof Translate, feature: Feature): boolean {
    const filter = (attached(map, kind) as unknown as { filter_: (f: Feature, layer?: unknown) => boolean }).filter_;
    return filter(feature, undefined);
  }

  it('takes hold of every feature until it is limited', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    controller.setMode('modify');
    expect(takesHold(map, Modify, featureWithId('a'))).toBe(true);
  });

  it('takes hold only of the features in scope, and always of one drawn here and not saved', () => {
    const unsaved = new Feature(new Point([0, 0]));
    unsaved.set('pendingNew', true);
    const cases = [
      { scope: { kind: 'only', id: 'mine' }, mine: true, other: false },
      { scope: { kind: 'every', except: 'held-back' }, mine: true, other: true },
      { scope: { kind: 'none' }, mine: false, other: false },
    ] as const;

    for (const { scope, mine, other } of cases) {
      const map = realMap();
      const controller = new MapEditController(map);
      controller.setReshapeScope(scope);
      for (const mode of ['modify', 'translate'] as const) {
        controller.setMode(mode);
        const kind = mode === 'modify' ? Modify : Translate;
        expect(takesHold(map, kind, featureWithId('mine')), `${scope.kind} ${mode}`).toBe(mine);
        expect(takesHold(map, kind, featureWithId('somebody-else')), `${scope.kind} ${mode}`).toBe(other);
        expect(takesHold(map, kind, featureWithId('held-back'))).toBe(false);
        expect(takesHold(map, kind, unsaved)).toBe(true);
      }
    }
  });

  it('holds a new scope at once on a tool already armed, and leaves the tool alone for the same scope', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    controller.setMode('modify');
    const armed = attached(map, Modify);

    // The same scope again — the tool keeps what it holds.
    controller.setReshapeScope({ kind: 'every' });
    expect(attached(map, Modify)).toBe(armed);

    controller.setReshapeScope({ kind: 'none' });
    expect(attached(map, Modify)).not.toBe(armed);
    expect(takesHold(map, Modify, featureWithId('a'))).toBe(false);
    expect(map.getInteractions().getArray().filter((i) => i instanceof Modify)).toHaveLength(1);
  });
});

describe('MapEditController.markCreatedSaved', () => {
  it('takes only the saved feature off the pending list and keeps it on the map as saved', () => {
    const controller = new MapEditController(fakeMap);
    let state: EditState | undefined;
    controller.subscribe((s) => {
      state = s;
    });
    controller.placePointAt([24, 45], 1);
    controller.placePointAt([24.1, 45.1], 1);
    const [first] = controller.getPendingEdits().created;

    controller.markCreatedSaved(first.feature, 'server-1');

    expect(controller.getPendingEdits().created.map((c) => c.feature)).not.toContain(first.feature);
    expect(state?.dirty).toBe(1);
    expect(first.feature.get('id')).toBe('server-1');
    expect(first.feature.get('pendingNew')).toBeUndefined();
    expect(getSurfaceFeatureSource().getFeatures()).toContain(first.feature);
  });

  it('ends the edit history, so a redo cannot put a saved feature back on the pending list', () => {
    const controller = new MapEditController(fakeMap);
    let state: EditState | undefined;
    controller.subscribe((s) => {
      state = s;
    });
    controller.placePointAt([24, 45], 1);
    controller.placePointAt([24.1, 45.1], 1);
    controller.undo();
    expect(state?.canRedo).toBe(true);
    const [first] = controller.getPendingEdits().created;

    controller.markCreatedSaved(first.feature, 'server-1');
    controller.redo();
    controller.undo();

    expect(state?.canUndo).toBe(false);
    expect(state?.canRedo).toBe(false);
    expect(controller.getPendingEdits().created).toHaveLength(0);
    expect(getSurfaceFeatureSource().getFeatures()).toContain(first.feature);
  });
});

describe('reshaping gestures and their history', () => {
  /** A saved feature on the edit source, as the layer loader puts one there. */
  function savedLine(id: string): Feature {
    const feature = new Feature(new LineString([[0, 0], [100, 100]]));
    feature.set('id', id);
    getSurfaceFeatureSource().addFeature(feature);
    return feature;
  }

  const coordinatesOf = (geometry: object) => (geometry as { coordinates: number[][] }).coordinates;

  function modifyGesture(map: Map, feature: Feature, change: () => void) {
    const modify = map.getInteractions().getArray().find((i) => i instanceof Modify)!;
    const features = new Collection([feature]);
    modify.dispatchEvent(new ModifyEvent('modifystart', features, undefined as never));
    change();
    modify.dispatchEvent(new ModifyEvent('modifyend', features, undefined as never));
  }

  it('records a vertex edit on a saved feature, and undo and redo move it back and forth', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    let state: EditState | undefined;
    controller.subscribe((s) => {
      state = s;
    });
    const line = savedLine('saved-1');
    const original = MapEditController.toGeoJsonGeometry(line.getGeometry()!);
    controller.setMode('modify');

    modifyGesture(map, line, () => (line.getGeometry() as LineString).setCoordinates([[0, 0], [500, 500]]));

    const edited = controller.getPendingEdits().modified.get('saved-1');
    expect(edited).toBeDefined();
    expect(edited).not.toEqual(original);
    expect(state?.dirty).toBe(1);
    expect(state?.canUndo).toBe(true);

    controller.undo();
    expect((line.getGeometry() as LineString).getCoordinates()).toEqual([[0, 0], [100, 100]]);
    // Back in the shape the server holds, the feature has nothing left to save.
    expect(controller.getPendingEdits().modified.has('saved-1')).toBe(false);
    expect(state?.dirty).toBe(0);
    expect(state?.canRedo).toBe(true);

    controller.redo();
    expect((line.getGeometry() as LineString).getCoordinates()).toEqual([[0, 0], [500, 500]]);
    expect(coordinatesOf(controller.getPendingEdits().modified.get('saved-1')!)).toEqual(coordinatesOf(edited!));
    expect(state?.dirty).toBe(1);
  });

  it('reads the version a reshaping began from once per feature object, and afresh after a save or a reload', async () => {
    const map = realMap();
    const versions = ['"v1"', '"v2"', '"v3"'];
    const readVersion = vi.fn(async () => versions.shift());
    const controller = new MapEditController(map, readVersion);
    const line = savedLine('saved-1');
    controller.setMode('modify');

    modifyGesture(map, line, () => (line.getGeometry() as LineString).setCoordinates([[0, 0], [200, 200]]));
    modifyGesture(map, line, () => (line.getGeometry() as LineString).setCoordinates([[0, 0], [300, 300]]));
    expect(readVersion).toHaveBeenCalledTimes(1);
    expect(await controller.openedVersion('saved-1')).toBe('"v1"');

    // Saved: the next reshaping starts from the version this editor's own save produced.
    controller.markModifiedSaved('saved-1', controller.getPendingEdits().modified.get('saved-1')!);
    expect(await controller.openedVersion('saved-1')).toBeUndefined();
    modifyGesture(map, line, () => (line.getGeometry() as LineString).setCoordinates([[0, 0], [400, 400]]));
    expect(await controller.openedVersion('saved-1')).toBe('"v2"');

    // A reload put a new object of the same feature on the map: its shape may be newer.
    controller.undo();
    getSurfaceFeatureSource().removeFeature(line);
    const reloaded = savedLine('saved-1');
    modifyGesture(map, reloaded, () => (reloaded.getGeometry() as LineString).setCoordinates([[0, 0], [500, 500]]));
    expect(await controller.openedVersion('saved-1')).toBe('"v3"');
  });

  it('reads the version again at the next gesture when the first read failed', async () => {
    const map = realMap();
    const readVersion = vi.fn().mockRejectedValueOnce(new Error('offline')).mockResolvedValue('"v1"');
    const controller = new MapEditController(map, readVersion);
    const line = savedLine('saved-1');
    controller.setMode('modify');

    modifyGesture(map, line, () => (line.getGeometry() as LineString).setCoordinates([[0, 0], [200, 200]]));
    expect(await controller.openedVersion('saved-1')).toBeUndefined();
    modifyGesture(map, line, () => (line.getGeometry() as LineString).setCoordinates([[0, 0], [300, 300]]));
    expect(await controller.openedVersion('saved-1')).toBe('"v1"');
  });

  it('keeps a feature pending until every one of several edits to it is undone', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    const line = savedLine('saved-1');
    controller.setMode('modify');

    modifyGesture(map, line, () => (line.getGeometry() as LineString).setCoordinates([[0, 0], [200, 200]]));
    modifyGesture(map, line, () => (line.getGeometry() as LineString).setCoordinates([[0, 0], [300, 300]]));
    controller.undo();

    expect(coordinatesOf(controller.getPendingEdits().modified.get('saved-1')!)).toEqual(
      coordinatesOf(MapEditController.toGeoJsonGeometry(line.getGeometry()!)),
    );
    controller.undo();
    expect(controller.getPendingEdits().modified.size).toBe(0);
  });

  it('records a move made with the move tool', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    const point = new Feature(new Point([0, 0]));
    point.set('id', 'saved-2');
    getSurfaceFeatureSource().addFeature(point);
    controller.setMode('translate');
    const translate = map.getInteractions().getArray().find((i) => i instanceof Translate)!;
    const moved = new Collection([point]);

    translate.dispatchEvent(new TranslateEvent('translatestart', moved, [0, 0], [0, 0], undefined as never));
    point.getGeometry()!.translate(1000, 0);
    translate.dispatchEvent(new TranslateEvent('translateend', moved, [1000, 0], [0, 0], undefined as never));

    expect(controller.getPendingEdits().modified.has('saved-2')).toBe(true);
  });

  it('leaves a drawing that is not saved yet on the created list only, not as a geometry update', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    controller.placePointAt([24, 45], 1);
    const [{ feature }] = controller.getPendingEdits().created;
    controller.setMode('modify');

    modifyGesture(map, feature, () => feature.getGeometry()!.translate(10, 10));

    expect(controller.getPendingEdits().modified.size).toBe(0);
    expect(controller.getPendingEdits().created).toHaveLength(1);
  });
});

describe('a shape finished with the draw tool', () => {
  it('becomes a pending drawing of the armed type, which undo takes off the map', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    const drawEnded = vi.fn();
    controller.onDrawEnd = drawEnded;
    let state: EditState | undefined;
    controller.subscribe((s) => {
      state = s;
    });
    controller.setMode('draw', 'LineString', 4);
    const feature = new Feature(new LineString([[0, 0], [100, 0]]));

    // OL announces the finished shape and then adds it to the source it was given.
    activeDraw(map).dispatchEvent(new DrawEvent('drawend', feature));
    getSurfaceFeatureSource().addFeature(feature);

    expect(feature.get('pendingNew')).toBe(true);
    expect(feature.get('featureTypeId')).toBe(4);
    expect(controller.getPendingEdits().created).toEqual([{ feature, geometryType: 'LineString' }]);
    expect(drawEnded).toHaveBeenCalledWith(feature);
    expect(state?.dirty).toBe(1);

    controller.undo();
    expect(getSurfaceFeatureSource().hasFeature(feature)).toBe(false);
    expect(state?.dirty).toBe(0);
  });
});

describe('the interactions the controller puts on the map', () => {
  const ours = (map: Map) =>
    map.getInteractions().getArray().filter((i) => i instanceof Draw || i instanceof Modify || i instanceof Translate || i instanceof Snap);

  it('swaps tools on a mode change, and snap comes and goes without disarming the draw tool', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    let state: EditState | undefined;
    controller.subscribe((s) => {
      state = s;
    });

    controller.setMode('draw', 'Polygon', 3);
    expect(ours(map).map((i) => i.constructor)).toEqual([Draw, Snap]);

    controller.toggleSnap();
    expect(ours(map).map((i) => i.constructor)).toEqual([Draw]);
    expect(state).toMatchObject({ mode: 'draw', snap: false, drawShape: 'Polygon', drawTypeId: 3 });

    controller.toggleSnap();
    controller.setMode('modify');
    expect(ours(map).map((i) => i.constructor)).toEqual([Modify, Snap]);

    controller.setMode('none');
    expect(ours(map)).toHaveLength(0);
  });

  it('throws away every pending edit on reset, takes the drawings off the map and disarms', () => {
    const map = realMap();
    const controller = new MapEditController(map);
    let state: EditState | undefined;
    controller.subscribe((s) => {
      state = s;
    });
    controller.placePointAt([24, 45], 1);
    const [{ feature: drawn }] = controller.getPendingEdits().created;
    const saved = new Feature(new Point([0, 0]));
    saved.set('id', 'saved-1');
    getSurfaceFeatureSource().addFeature(saved);
    controller.setMode('translate');
    const translate = map.getInteractions().getArray().find((i) => i instanceof Translate)!;
    const moved = new Collection([saved]);
    translate.dispatchEvent(new TranslateEvent('translatestart', moved, [0, 0], [0, 0], undefined as never));
    saved.getGeometry()!.translate(5, 5);
    translate.dispatchEvent(new TranslateEvent('translateend', moved, [5, 5], [0, 0], undefined as never));
    expect(state?.dirty).toBe(2);

    controller.reset();

    expect(controller.getPendingEdits().created).toHaveLength(0);
    expect(controller.getPendingEdits().modified.size).toBe(0);
    expect(getSurfaceFeatureSource().hasFeature(drawn)).toBe(false);
    expect(state).toMatchObject({ mode: 'none', dirty: 0, canUndo: false, canRedo: false });
    expect(ours(map)).toHaveLength(0);
  });

  it('leaves nothing of its own on the map after dispose, and tells no one about later changes', () => {
    const map = realMap();
    const before = map.getInteractions().getLength();
    const controller = new MapEditController(map);
    const listener = vi.fn();
    controller.subscribe(listener);
    controller.setMode('modify');
    expect(map.getInteractions().getLength()).toBeGreaterThan(before);
    listener.mockClear();

    controller.dispose();
    controller.placePointAt([24, 45], 1);

    expect(map.getInteractions().getLength()).toBe(before);
    expect(ours(map)).toHaveLength(0);
    expect(listener).not.toHaveBeenCalled();
  });
});
