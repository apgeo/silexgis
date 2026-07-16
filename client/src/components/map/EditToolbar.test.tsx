// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import MapContext from '@terrestris/react-util/dist/Context/MapContext/MapContext';
import OlMap from 'ol/Map';
import View from 'ol/View';
import { cleanup, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { EditState, MapEditController } from '../../map/mapEdit.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import EditToolbar from './EditToolbar.tsx';

let mobile = false;

vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => mobile }));

vi.mock('../../api/hooks.ts', () => ({
  useFeatureTypes: () => ({
    data: [
      { id: 1, name: 'Sinkhole', code: 'sinkhole', geometryKind: 'point', symbolFile: null, sortOrder: 0 },
    ],
  }),
  createSurfaceFeature: vi.fn(),
  fetchSurfaceFeature: vi.fn(),
  updateSurfaceFeature: vi.fn(),
}));

vi.mock('../../map/featureLayer.ts', () => ({ reloadSurfaceFeatures: vi.fn() }));

// The toolbar's own layout is what is under test; the dialogs it hosts pull in the whole
// cave/feature form stack and render nothing while closed.
vi.mock('./CaveAddModal.tsx', () => ({ default: () => null }));
vi.mock('../features/FeatureEditModal.tsx', () => ({ default: () => null }));

function fakeController(over: Partial<EditState> = {}): MapEditController {
  const state: EditState = { mode: 'none', snap: true, canUndo: false, canRedo: false, dirty: 0, ...over };
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
    getPendingEdits: () => ({ created: [], modified: new Map() }),
  } as unknown as MapEditController;
}

function renderToolbar(over: Partial<EditState> = {}) {
  const map = new OlMap({ view: new View({ center: [0, 0], zoom: 2 }) });
  return render(
    <MapContext.Provider value={map}>
      <App>
        <EditToolbar controller={fakeController(over)} />
      </App>
    </MapContext.Provider>,
  );
}

beforeEach(() => {
  mobile = false;
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
