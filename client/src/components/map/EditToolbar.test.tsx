// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import MapContext from '@terrestris/react-util/dist/Context/MapContext/MapContext';
import OlMap from 'ol/Map';
import View from 'ol/View';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
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
  createFeature: vi.fn(),
  fetchFeature: vi.fn(),
  updateFeature: vi.fn(),
}));

vi.mock('../../map/featureLayer.ts', () => ({ reloadSurfaceFeatures: vi.fn() }));

// The toolbar's own layout is what is under test; the dialogs it hosts pull in the whole
// cave/feature form stack and render nothing while closed.
vi.mock('./CaveAddModal.tsx', () => ({ default: () => null }));
vi.mock('../features/FeatureEditModal.tsx', () => ({ default: () => null }));

let removedVertex = true;
let finishedDrawing = true;

function fakeController(over: Partial<EditState> = {}): MapEditController {
  const state: EditState = {
    mode: 'none', snap: true, canUndo: false, canRedo: false, dirty: 0, sketchActive: false, ...over,
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

function renderToolbar(over: Partial<EditState> = {}) {
  const map = new OlMap({ view: new View({ center: [0, 0], zoom: 2 }) });
  controller = fakeController(over);
  return render(
    <MapContext.Provider value={map}>
      <App>
        <EditToolbar controller={controller} />
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
