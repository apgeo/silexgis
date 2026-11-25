// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import MapContext from '@terrestris/react-util/dist/Context/MapContext/MapContext';
import OlMap from 'ol/Map';
import View from 'ol/View';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Collection from 'ol/Collection';
import Feature from 'ol/Feature';
import Point from 'ol/geom/Point';
import { Translate } from 'ol/interaction';
import { TranslateEvent } from 'ol/interaction/Translate';
import { fromLonLat } from 'ol/proj';
import '../../i18n';
import { createFeature, fetchFeature, fetchFeatureVersion, updateFeature } from '../../api/hooks.ts';
import { ApiError } from '../../api/client.ts';
import { getSurfaceFeatureSource, SAVE_REFUSED } from '../../map/featureLayer.ts';
import { MapEditController as RealMapEditController } from '../../map/mapEdit.ts';
import type { EditState, MapEditController } from '../../map/mapEdit.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import EditToolbar from './EditToolbar.tsx';

let mobile = false;
// Layout follows viewport width; the sketch actions follow pointer type. A phone is both,
// but they are separate switches and the tests drive them separately.
let touch = false;

vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => mobile }));
vi.mock('../../map/pointer.ts', () => ({ coarsePointer: () => touch }));

vi.mock('../../api/hooks.ts', () => ({
  useFeatureTypes: () => ({
    data: [
      {
        id: 1,
        name: 'Sinkhole',
        code: 'sinkhole',
        acceptedGeometryClasses: ['point'],
        symbolFile: null,
        sortOrder: 0,
      },
    ],
  }),
  useGenericFeatureTypes: () => ({
    data: [
      {
        id: 1,
        name: 'Sinkhole',
        code: 'sinkhole',
        acceptedGeometryClasses: ['point'],
        symbolFile: null,
        sortOrder: 0,
      },
    ],
  }),
  createFeature: vi.fn(),
  fetchFeature: vi.fn(),
  fetchFeatureVersion: vi.fn(),
  updateFeature: vi.fn(),
}));

// The toolbar's own layout is what is under test; the dialogs it hosts pull in the whole
// cave/feature form stack and render nothing while closed.
vi.mock('./CaveAddModal.tsx', () => ({ default: () => null }));
vi.mock('../features/FeatureEditModal.tsx', () => ({ default: () => null }));

let removedVertex = true;
let finishedDrawing = true;

function fakeController(over: Partial<EditState> = {}): MapEditController {
  const state: EditState = {
    mode: 'none', snap: true, canUndo: false, canRedo: false, dirty: 0, created: 0, sketchActive: false, ...over,
  };
  return {
    subscribe: (cb: (s: EditState) => void) => {
      cb(state);
      return () => {};
    },
    setMode: vi.fn(),
    toggleSnap: vi.fn(),
    undo: vi.fn(),
    redo: vi.fn(),
    reset: vi.fn(),
    finishDrawing: vi.fn().mockReturnValue(finishedDrawing),
    abortDrawing: vi.fn(),
    removeLastPoint: vi.fn(),
    removeVertex: vi.fn().mockReturnValue(removedVertex),
    getPendingEdits: () => ({ created: [], modified: new Map() }),
  } as unknown as MapEditController;
}

let controller: MapEditController;

function renderToolbar(over: Partial<EditState> = {}, mayModify = true, rightsPending = false) {
  const map = new OlMap({ view: new View({ center: [0, 0], zoom: 2 }) });
  controller = fakeController(over);
  return render(
    <MapContext.Provider value={map}>
      <App>
        <EditToolbar controller={controller} mayModify={mayModify} rightsPending={rightsPending} />
      </App>
    </MapContext.Provider>,
  );
}

beforeEach(() => {
  mobile = false;
  touch = false;
  removedVertex = true;
  finishedDrawing = true;
});

afterEach(() => {
  cleanup();
  useUiPrefsStore.setState({ pinnedTypeIds: [] });
});

describe('EditToolbar', () => {
  it('lays out as a single row on desktop, with no separate scrolling strip', () => {
    renderToolbar();

    expect(screen.queryByTestId('edit-tool-strip')).not.toBeInTheDocument();
    expect(screen.queryByTestId('edit-save-cluster')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Save/ })).toBeInTheDocument();
  });

  it('splits into a scrolling tool strip and a save cluster on mobile', () => {
    mobile = true;
    renderToolbar();

    const strip = screen.getByTestId('edit-tool-strip');
    const saveCluster = screen.getByTestId('edit-save-cluster');

    // Every tool stays reachable — they live in the strip, which scrolls sideways.
    expect(within(strip).getByTestId('feature-palette-trigger')).toBeInTheDocument();
    expect(within(strip).getByTestId('tool-add-cave')).toBeInTheDocument();
    expect(within(strip).getByTestId('tool-add-entrance')).toBeInTheDocument();

    // ...but the save cluster is pinned outside the scroller. This is the point of the
    // split: unsaved edits must never be what scrolled out of sight.
    const save = within(saveCluster).getByRole('button', { name: /Save/ });
    expect(strip).not.toContainElement(save);
    expect(within(saveCluster).getAllByRole('button')).toHaveLength(2); // save + discard
  });

  it('keeps the dirty count on the pinned save button', () => {
    mobile = true;
    renderToolbar({ dirty: 3 });

    const saveCluster = screen.getByTestId('edit-save-cluster');
    expect(within(saveCluster).getByText('3')).toBeInTheDocument();
    expect(within(saveCluster).getByRole('button', { name: /Save/ })).toBeEnabled();
  });
});

describe('EditToolbar sketch actions', () => {
  // A finger cannot finish a shape the way a cursor does — hitting the last vertex needs
  // ~12px of precision, and double-tap is the map's zoom. These buttons are the whole
  // touch termination story, so each one has to reach the controller.

  it('stays out of the way of a mouse, which has the gestures for all of this', () => {
    renderToolbar({ mode: 'draw', drawShape: 'LineString', sketchActive: true });
    expect(screen.queryByTestId('map-sketch-bar')).not.toBeInTheDocument();
  });

  it('offers finish, retract and cancel while a multi-vertex shape is being drawn', () => {
    touch = true;
    renderToolbar({ mode: 'draw', drawShape: 'LineString', sketchActive: true });
    fireEvent.click(screen.getByTestId('sketch-finish'));
    expect(controller.finishDrawing).toHaveBeenCalled();

    fireEvent.click(screen.getByTestId('sketch-remove-point'));
    expect(controller.removeLastPoint).toHaveBeenCalled();

    fireEvent.click(screen.getByTestId('sketch-cancel'));
    expect(controller.abortDrawing).toHaveBeenCalled();
  });

  it('waits for a sketch to actually start before offering to end one', () => {
    touch = true;
    renderToolbar({ mode: 'draw', drawShape: 'Polygon', sketchActive: false });
    expect(screen.queryByTestId('map-sketch-bar')).not.toBeInTheDocument();
  });

  it('leaves points alone — one tap is the whole gesture', () => {
    touch = true;
    renderToolbar({ mode: 'draw', drawShape: 'Point', sketchActive: true });
    expect(screen.queryByTestId('map-sketch-bar')).not.toBeInTheDocument();
  });

  it('offers vertex deletion while modifying, since alt-click needs a keyboard', () => {
    touch = true;
    renderToolbar({ mode: 'modify' });
    expect(screen.queryByTestId('sketch-finish')).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId('sketch-delete-vertex'));
    expect(controller.removeVertex).toHaveBeenCalled();
  });

  it('explains the gesture when no vertex was touched, rather than doing nothing', async () => {
    touch = true;
    removedVertex = false;
    renderToolbar({ mode: 'modify' });
    fireEvent.click(screen.getByTestId('sketch-delete-vertex'));

    await waitFor(() => expect(screen.getByText(/Tap a vertex on the map first/)).toBeInTheDocument());
  });

  it('shows nothing when no tool is armed', () => {
    touch = true;
    renderToolbar({ mode: 'none' });
    expect(screen.queryByTestId('map-sketch-bar')).not.toBeInTheDocument();
  });

  it('explains a refused finish rather than leaving the button looking broken', async () => {
    touch = true;
    finishedDrawing = false; // too few vertices down for the shape to be valid yet
    renderToolbar({ mode: 'draw', drawShape: 'Polygon', sketchActive: true });

    fireEvent.click(screen.getByTestId('sketch-finish'));

    await waitFor(() => expect(screen.getByText(/Place more points/)).toBeInTheDocument());
  });
});

describe('EditToolbar for a selected feature the person may not change', () => {
  it('offers no reshaping tool, and keeps drawing new features', () => {
    renderToolbar({}, false);

    expect(screen.getByRole('button', { name: /^Modify/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Move geometry' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Draw' })).toBeInTheDocument();
  });

  it('puts down a reshaping tool that was armed when such a feature is selected', () => {
    renderToolbar({ mode: 'modify' }, false);

    expect(controller.setMode).toHaveBeenCalledWith('none');
  });

  it('leaves an armed tool armed while a newly selected feature’s rights are on their way', () => {
    renderToolbar({ mode: 'modify' }, false, true);

    expect(screen.getByRole('button', { name: /^Modify/ })).toBeDisabled();
    expect(controller.setMode).not.toHaveBeenCalledWith('none');
  });

  it('still offers them for what was drawn here and not saved yet', () => {
    renderToolbar({ mode: 'modify', created: 1, dirty: 1 }, false);

    expect(screen.getByRole('button', { name: /^Modify/ })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Move geometry' })).toBeEnabled();
    expect(controller.setMode).not.toHaveBeenCalledWith('none');
  });
});

describe('EditToolbar save', () => {
  // The server as the save loop sees it: every accepted create lands here, by its coordinate.
  let created: { id: string; coordinates: number[] }[];

  function renderWith(real: MapEditController) {
    const map = new OlMap({ view: new View({ center: [0, 0], zoom: 2 }) });
    return render(
      <MapContext.Provider value={map}>
        <App>
          <EditToolbar controller={real} />
        </App>
      </MapContext.Provider>,
    );
  }

  beforeEach(() => {
    created = [];
    vi.mocked(createFeature).mockReset();
    vi.mocked(fetchFeature).mockReset();
    vi.mocked(updateFeature).mockReset();
    // The version a saved feature is at when a gesture first takes hold of it.
    vi.mocked(fetchFeatureVersion).mockReset().mockResolvedValue('"opened"');
  });

  /** A saved feature on the map, moved once with the move tool. */
  function moveSaved(map: OlMap, real: MapEditController, id = 'saved-1'): Feature {
    const saved = new Feature(new Point(fromLonLat([25, 46])));
    saved.set('id', id);
    getSurfaceFeatureSource().addFeature(saved);
    act(() => real.setMode('translate'));
    const translate = map.getInteractions().getArray().find((i) => i instanceof Translate)!;
    const moved = new Collection([saved]);
    act(() => {
      translate.dispatchEvent(new TranslateEvent('translatestart', moved, [0, 0], [0, 0], undefined as never));
      saved.getGeometry()!.translate(1000, 1000);
      translate.dispatchEvent(new TranslateEvent('translateend', moved, [0, 0], [0, 0], undefined as never));
    });
    act(() => real.setMode('none'));
    return saved;
  }

  const storedSaved1 = {
    feature: { id: 'saved-1', name: 'x', featureTypeCode: 'sinkhole', description: null, properties: null,
      locationProtected: false, cavingGroupId: null, visibility: 'private' },
  } as never;

  it('names the version the reshaping began from, not the one read as the save goes out', async () => {
    const map = new OlMap({ view: new View({ center: [0, 0], zoom: 2 }) });
    const real = new RealMapEditController(map);
    renderWith(real);
    moveSaved(map, real);
    expect(fetchFeatureVersion).toHaveBeenCalledWith('saved-1');
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/ })).toBeEnabled());
    // Somebody else saves the feature meanwhile: what is read now is at their newer version.
    vi.mocked(fetchFeatureVersion).mockResolvedValue('"theirs"');
    vi.mocked(fetchFeature).mockResolvedValue(storedSaved1);
    vi.mocked(updateFeature).mockResolvedValue({} as never);

    fireEvent.click(screen.getByRole('button', { name: /Save/ }));
    expect(await screen.findByText('Saved.')).toBeInTheDocument();

    expect(vi.mocked(updateFeature).mock.calls[0][2]).toBe('"opened"');
  });

  it('turns a refusal for a newer version into the changed-since sentence, and a retry still names the old version', async () => {
    const map = new OlMap({ view: new View({ center: [0, 0], zoom: 2 }) });
    const real = new RealMapEditController(map);
    renderWith(real);
    const saved = moveSaved(map, real);
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/ })).toBeEnabled());
    vi.mocked(fetchFeature).mockResolvedValue(storedSaved1);
    vi.mocked(updateFeature).mockRejectedValue(new ApiError(412, 'concurrency.version_mismatch'));

    fireEvent.click(screen.getByRole('button', { name: /Save/ }));
    expect(await screen.findByText(/Someone else changed this since you loaded it/)).toBeInTheDocument();
    expect(real.getPendingEdits().modified.size).toBe(1);
    expect(saved.get(SAVE_REFUSED)).toBe(true);
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/ })).not.toHaveClass('ant-btn-loading'));

    fireEvent.click(screen.getByRole('button', { name: /Save/ }));
    await waitFor(() => expect(updateFeature).toHaveBeenCalledTimes(2));
    expect(vi.mocked(updateFeature).mock.calls.map((call) => call[2])).toEqual(['"opened"', '"opened"']);
    expect(real.getPendingEdits().modified.size).toBe(1);
  });

  afterEach(() => {
    getSurfaceFeatureSource().clear();
  });

  it('retries only what a partly failed save did not save, so nothing is created twice', async () => {
    // placePointAt never reaches into the map, so a bare object stands in for it.
    const real = new RealMapEditController({} as OlMap);
    renderWith(real);
    act(() => {
      real.placePointAt([24, 45], 1);
      real.placePointAt([24.1, 45.1], 1);
      real.placePointAt([24.2, 45.2], 1);
    });
    const drawings = real.getPendingEdits().created.map((c) => c.feature);
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/ })).toBeEnabled());

    let calls = 0;
    vi.mocked(createFeature).mockImplementation(async (body) => {
      calls += 1;
      // The second create of the first attempt is refused; every other one is accepted.
      if (calls === 2) throw new ApiError(422, 'validation');
      const id = `f${calls}`;
      created.push({ id, coordinates: (body.geometry as unknown as { coordinates: number[] }).coordinates });
      return { id } as never;
    });

    fireEvent.click(screen.getByRole('button', { name: /Save/ }));
    await waitFor(() => expect(created).toHaveLength(2));
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/ })).not.toHaveClass('ant-btn-loading'));
    // What was not saved is still pending, and only that — and it is the one marked on the map.
    expect(real.getPendingEdits().created).toHaveLength(1);
    expect(drawings.map((f) => f.get(SAVE_REFUSED) === true)).toEqual([false, true, false]);

    fireEvent.click(screen.getByRole('button', { name: /Save/ }));
    await waitFor(() => expect(created).toHaveLength(3));
    await waitFor(() => expect(drawings.some((f) => f.get(SAVE_REFUSED) === true)).toBe(false));

    const longitudes = created.map((c) => Number(c.coordinates[0].toFixed(3))).sort();
    expect(longitudes).toEqual([24, 24.1, 24.2]);
    await waitFor(() => expect(real.getPendingEdits().created).toHaveLength(0));
  });

  it('does not create again what was created before a geometry update failed', async () => {
    const map = new OlMap({ view: new View({ center: [0, 0], zoom: 2 }) });
    const real = new RealMapEditController(map);
    renderWith(real);
    act(() => real.placePointAt([24, 45], 1));
    // A saved feature moved with the move tool: its gesture events are what record the change.
    const saved = new Feature(new Point(fromLonLat([25, 46])));
    saved.set('id', 'saved-1');
    getSurfaceFeatureSource().addFeature(saved);
    act(() => real.setMode('translate'));
    const translate = map.getInteractions().getArray().find((i) => i instanceof Translate)!;
    const moved = new Collection([saved]);
    act(() => {
      translate.dispatchEvent(new TranslateEvent('translatestart', moved, [0, 0], [0, 0], undefined as never));
      saved.getGeometry()!.translate(1000, 1000);
      translate.dispatchEvent(new TranslateEvent('translateend', moved, [0, 0], [0, 0], undefined as never));
    });
    act(() => real.setMode('none'));
    expect(real.getPendingEdits().modified.size).toBe(1);
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/ })).toBeEnabled());

    vi.mocked(createFeature).mockImplementation(async () => {
      created.push({ id: 'new-1', coordinates: [] });
      return { id: 'new-1' } as never;
    });
    vi.mocked(fetchFeature).mockResolvedValue({
      feature: { id: 'saved-1', name: 'x', featureTypeCode: 'sinkhole', description: null, properties: null,
        locationProtected: false, cavingGroupId: null, visibility: 'private' },
    } as never);
    vi.mocked(updateFeature).mockRejectedValueOnce(new ApiError(503)).mockResolvedValue({} as never);

    fireEvent.click(screen.getByRole('button', { name: /Save/ }));
    await waitFor(() => expect(updateFeature).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/ })).not.toHaveClass('ant-btn-loading'));

    fireEvent.click(screen.getByRole('button', { name: /Save/ }));
    await waitFor(() => expect(updateFeature).toHaveBeenCalledTimes(2));
    expect(createFeature).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(real.getPendingEdits().modified.size).toBe(0));
  });

  it('sends what the editor filled in for a drawing, the safe defaults for the rest, and keeps a reshaped feature’s own fields', async () => {
    const map = new OlMap({ view: new View({ center: [0, 0], zoom: 2 }) });
    const real = new RealMapEditController(map);
    renderWith(real);
    act(() => {
      real.placePointAt([24, 45], 1);
      real.placePointAt([24.5, 45.5], 1);
    });
    const [described] = real.getPendingEdits().created;
    described.feature.set('pendingAttrs', {
      name: 'Doline 4',
      featureTypeId: 1,
      description: 'rim collapsed',
      properties: { depth: 3 },
      primaryParentId: 'parent-1',
      locationProtected: true,
      visibility: 'authenticated',
    });
    const saved = new Feature(new Point(fromLonLat([25, 46])));
    saved.set('id', 'saved-1');
    getSurfaceFeatureSource().addFeature(saved);
    act(() => real.setMode('translate'));
    const translate = map.getInteractions().getArray().find((i) => i instanceof Translate)!;
    const moved = new Collection([saved]);
    act(() => {
      translate.dispatchEvent(new TranslateEvent('translatestart', moved, [0, 0], [0, 0], undefined as never));
      saved.getGeometry()!.translate(1000, 1000);
      translate.dispatchEvent(new TranslateEvent('translateend', moved, [0, 0], [0, 0], undefined as never));
    });
    act(() => real.setMode('none'));
    const reshaped = real.getPendingEdits().modified.get('saved-1');
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/ })).toBeEnabled());

    let n = 0;
    vi.mocked(createFeature).mockImplementation(async () => ({ id: `new-${++n}` }) as never);
    vi.mocked(fetchFeature).mockResolvedValue({
      feature: { id: 'saved-1', name: 'Old sink', featureTypeCode: 'sinkhole', description: 'kept text',
        properties: { kept: true }, locationProtected: true, cavingGroupId: 'group-7', visibility: 'cavingGroup' },
    } as never);
    vi.mocked(updateFeature).mockResolvedValue({} as never);

    fireEvent.click(screen.getByRole('button', { name: /Save/ }));
    expect(await screen.findByText('Saved.')).toBeInTheDocument();

    expect(createFeature).toHaveBeenCalledTimes(2);
    expect(vi.mocked(createFeature).mock.calls[0][0]).toMatchObject({
      kind: 'generic', name: 'Doline 4', featureTypeId: 1, description: 'rim collapsed',
      properties: { depth: 3 }, parents: [{ parentId: 'parent-1', isPrimary: true }],
      locationProtected: true, cavingGroupId: null, visibility: 'authenticated',
      geometry: { type: 'Point' },
    });
    // A drawing nobody described is created private, unprotected, unnamed and parentless.
    expect(vi.mocked(createFeature).mock.calls[1][0]).toMatchObject({
      name: null, featureTypeId: 1, description: null, properties: null, parents: null,
      locationProtected: false, visibility: 'private',
    });
    // An update replaces the whole feature, so everything but the geometry is what the server holds.
    expect(updateFeature).toHaveBeenCalledWith('saved-1', {
      name: 'Old sink', featureTypeId: 1, geometry: reshaped, description: 'kept text',
      properties: { kept: true }, locationProtected: true, cavingGroupId: 'group-7', visibility: 'cavingGroup',
    }, '"opened"');
    expect(real.getPendingEdits().created).toHaveLength(0);
    expect(real.getPendingEdits().modified.size).toBe(0);
  });

  it('says which rule a refused drawing broke, rather than only that the save failed', async () => {
    const real = new RealMapEditController({} as OlMap);
    renderWith(real);
    act(() => real.placePointAt([24, 45], 1));
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/ })).toBeEnabled());
    vi.mocked(createFeature).mockRejectedValue(new ApiError(400, 'feature.geometry_invalid'));

    fireEvent.click(screen.getByRole('button', { name: /Save/ }));

    expect(
      await screen.findByText(/0 saved, 1 not saved\..*A shape is not one the server accepts/),
    ).toBeInTheDocument();
    expect(real.getPendingEdits().created).toHaveLength(1);
  });

  it('does not send a drawing that names no type, and keeps it pending', async () => {
    const real = new RealMapEditController({} as OlMap);
    renderWith(real);
    act(() => real.placePointAt([24, 45], 0));
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/ })).toBeEnabled());

    fireEvent.click(screen.getByRole('button', { name: /Save/ }));

    expect(await screen.findByText(/A feature has no type/)).toBeInTheDocument();
    expect(createFeature).not.toHaveBeenCalled();
    expect(real.getPendingEdits().created).toHaveLength(1);
  });

  it('does not guess a type for a reshaped feature whose type is not in the catalogue', async () => {
    const map = new OlMap({ view: new View({ center: [0, 0], zoom: 2 }) });
    const real = new RealMapEditController(map);
    renderWith(real);
    const saved = new Feature(new Point(fromLonLat([25, 46])));
    saved.set('id', 'saved-1');
    getSurfaceFeatureSource().addFeature(saved);
    act(() => real.setMode('translate'));
    const translate = map.getInteractions().getArray().find((i) => i instanceof Translate)!;
    const moved = new Collection([saved]);
    act(() => {
      translate.dispatchEvent(new TranslateEvent('translatestart', moved, [0, 0], [0, 0], undefined as never));
      saved.getGeometry()!.translate(1000, 1000);
      translate.dispatchEvent(new TranslateEvent('translateend', moved, [0, 0], [0, 0], undefined as never));
    });
    act(() => real.setMode('none'));
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/ })).toBeEnabled());
    vi.mocked(fetchFeature).mockResolvedValue({
      feature: { id: 'saved-1', name: 'x', featureTypeCode: 'no-such-type', description: null, properties: null,
        locationProtected: false, cavingGroupId: null, visibility: 'private' },
    } as never);

    fireEvent.click(screen.getByRole('button', { name: /Save/ }));

    expect(await screen.findByText(/could not be found in the list of feature types/)).toBeInTheDocument();
    expect(updateFeature).not.toHaveBeenCalled();
    expect(real.getPendingEdits().modified.size).toBe(1);
  });
});

describe('EditToolbar discard', () => {
  it('asks before throwing unsaved edits away, and names how many', async () => {
    renderToolbar({ dirty: 2 });

    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }));
    expect(await screen.findByText(/Throw away 2 unsaved edits\?/)).toBeInTheDocument();
    expect(controller.reset).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Discard' }));
    expect(controller.reset).toHaveBeenCalledTimes(1);
  });
});
