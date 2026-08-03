// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { surfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';
import { viewFlyTo } from '../../workspace/viewCamera.ts';

// The engine library is replaced by the same double the scene module's own suite uses; the test
// runner has no graphics context to give it.
vi.mock('cesium', () => import('../../scene3d/cesiumTestDouble.ts'));
vi.mock('../../api/hooks.ts', () => ({
  useMapLayers: () => ({ data: mapLayers }),
  useMapConfig: () => ({ data: undefined }),
  fetchCenterlineFeatures: (...args: unknown[]) => {
    centerlineRequests.push(args);
    return Promise.resolve(centerlineResponse);
  },
  fetchEntranceFeatures: () => Promise.resolve(emptyCollection),
  fetchMapFeatures: () => Promise.resolve(emptyCollection),
}));

const engine = await import('../../scene3d/cesiumTestDouble.ts');
const { useWorkspaceStore } = await import('../../stores/workspaceStore.ts');
const { default: Scene3DView } = await import('./Scene3DView.tsx');

let mapLayers: unknown[] | undefined;
let centerlineRequests: unknown[][] = [];
const emptyCollection = { type: 'FeatureCollection', features: [] };
let centerlineResponse: unknown = {
  type: 'FeatureCollection',
  features: [],
  withheldCount: 0,
  detail: false,
  flatCount: 0,
};

/** Makes the browser look like one that can run the scene, or one that cannot. */
function withWebGl2(available: boolean) {
  if (!available) {
    // jsdom already has no WebGL 2 constructor, which is exactly the browser being simulated.
    return;
  }
  class FakeWebGl2Context {
    getExtension() {
      return { loseContext() {} };
    }
  }
  vi.stubGlobal('WebGL2RenderingContext', FakeWebGl2Context);
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
    new FakeWebGl2Context() as unknown as CanvasRenderingContext2D,
  );
}

function renderView() {
  return render(
    <MemoryRouter>
      <Scene3DView />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  engine.engineState.reset();
  mapLayers = undefined;
  centerlineRequests = [];
  centerlineResponse = {
    type: 'FeatureCollection',
    features: [],
    withheldCount: 0,
    detail: false,
    flatCount: 0,
  };
  useWorkspaceStore.setState({ selection: null });
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('Scene3DView', () => {
  it('explains itself instead of showing a dead canvas when the browser cannot run it', () => {
    withWebGl2(false);
    renderView();

    expect(screen.getByTestId('scene3d-unsupported')).toBeInTheDocument();
    expect(screen.getByText('This browser cannot show the 3D view')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open the 2D map' })).toBeInTheDocument();
    // Nothing was mounted, so nothing downloaded the engine either.
    expect(screen.queryByTestId('scene3d-container')).not.toBeInTheDocument();
    expect(engine.engineState.widgets).toHaveLength(0);
  });

  it('builds the scene inside its own element when the browser can run it', async () => {
    withWebGl2(true);
    renderView();

    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));
    expect(engine.engineState.widgets[0].container).toBe(screen.getByTestId('scene3d-container'));
    await waitFor(() => expect(screen.queryByTestId('scene3d-loading')).not.toBeInTheDocument());
  });

  it('tears the scene down when it goes away', async () => {
    withWebGl2(true);
    const view = renderView();
    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));

    view.unmount();

    await waitFor(() => expect(engine.engineState.widgets[0].isDestroyed()).toBe(true));
  });

  it('drapes the configured basemaps over the globe and shows the default one', async () => {
    withWebGl2(true);
    mapLayers = [
      {
        id: 1,
        name: 'OpenStreetMap',
        layerKind: 'xyz',
        urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
        attribution: '© OpenStreetMap contributors',
        isBase: true,
        isDefault: true,
        sortOrder: 0,
        options: null,
      },
      {
        id: 2,
        name: 'OpenTopoMap',
        layerKind: 'xyz',
        urlTemplate: 'https://tile.opentopomap.org/{z}/{x}/{y}.png',
        attribution: null,
        isBase: true,
        isDefault: false,
        sortOrder: 1,
        options: null,
      },
    ];
    renderView();

    await waitFor(() => expect(engine.engineState.providers).toHaveLength(2));
    const layers = engine.engineState.widgets[0].scene.imageryLayers.layers;
    expect(layers.map((layer) => layer.show)).toEqual([true, false]);
    expect(engine.engineState.providers[0].url).toBe(
      'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
    );
  });

  it('asks for the caves in view, with the depths they were surveyed at', async () => {
    withWebGl2(true);
    renderView();

    await waitFor(() => expect(centerlineRequests).toHaveLength(1));
    const [bbox, zoom, , , withAltitudes] = centerlineRequests[0];
    expect(typeof bbox).toBe('string');
    expect(String(bbox).split(',')).toHaveLength(4);
    expect(typeof zoom).toBe('number');
    // The whole point of drawing a survey in three dimensions; the flat map leaves this off.
    expect(withAltitudes).toBe(true);
  });

  it('says how many caves could only be drawn flat', async () => {
    withWebGl2(true);
    centerlineResponse = {
      type: 'FeatureCollection',
      features: [],
      withheldCount: 0,
      detail: false,
      flatCount: 2,
    };
    renderView();

    expect(await screen.findByText(/drawn on the surface/)).toBeInTheDocument();
  });

  it('puts a click in the scene into the same selection the flat map writes', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(engine.engineState.eventHandlers).toHaveLength(1));

    const handler = engine.engineState.eventHandlers[0];
    const scene = engine.engineState.widgets[0].scene;
    // Whatever the scene reports is the object the loader attached, handed back untouched.
    scene.pickResult = { id: { kind: 'centerline', caveId: 'cave-1', centerlineId: 'line-1' } };
    handler.raise('leftClick', { position: new engine.Cartesian2(10, 20) });

    // A survey line belongs to a cave, so clicking one selects the cave — the same selection the
    // flat map produces from a cave picked anywhere else.
    expect(useWorkspaceStore.getState().selection).toEqual({ kind: 'cave', caveId: 'cave-1' });
  });

  it('clears the selection when the click lands on bare ground', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(engine.engineState.eventHandlers).toHaveLength(1));
    useWorkspaceStore.setState({ selection: { kind: 'cave', caveId: 'cave-1' } });

    const handler = engine.engineState.eventHandlers[0];
    const scene = engine.engineState.widgets[0].scene;
    scene.pickResult = undefined;
    scene.pickedPosition = { longitudeDegrees: 25, latitudeDegrees: 45, height: 700 };
    handler.raise('leftClick', { position: new engine.Cartesian2(10, 20) });

    expect(useWorkspaceStore.getState().selection).toBeNull();
  });

  it('answers the shared panel\'s "zoom to" with its own camera while it is on screen', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));

    const { camera } = engine.engineState.widgets[0].scene;
    const flightsBefore = camera.flightCount;
    viewFlyTo(25.5, 45.5, 16);

    // The detail panel is mounted beside this scene and beside the flat map, and it names neither:
    // it asks the view the viewer is looking at. Reaching for the flat map's camera from here
    // would move a map that is not on the page, so the button would do nothing visible and then
    // take effect the next time the flat map was opened.
    expect(camera.flightCount).toBe(flightsBefore + 1);
    expect(camera.positionWC.longitudeDegrees).toBeCloseTo(25.5, 6);
    expect(camera.positionWC.latitudeDegrees).toBeCloseTo(45.5, 6);
  });

  it('stops answering camera commands once it is gone', async () => {
    withWebGl2(true);
    const view = renderView();
    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));
    const { camera } = engine.engineState.widgets[0].scene;

    view.unmount();
    const flightsBefore = camera.flightCount;
    viewFlyTo(25.5, 45.5, 16);

    expect(camera.flightCount).toBe(flightsBefore);
  });

  it('refetches the ground it is showing when a feature is written from beside it', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(centerlineRequests).toHaveLength(1));

    // Deleting or editing a feature in the panel invalidates what is drawn here, and nothing else
    // will notice: these overlays ask for the box in view rather than reading a cache, so there is
    // no cached key a write can invalidate. Without this the deleted marker stays drawn and stays
    // clickable until the camera happens to move.
    surfaceFeaturesChanged();

    await waitFor(() => expect(centerlineRequests).toHaveLength(2));
  });

  it('reports a failure to start in the application\'s own words', async () => {
    withWebGl2(true);
    // A second scene on a different element is refused, which is the cheapest way to make the
    // start path fail for real rather than by stubbing the module.
    const { acquireScene3D } = await import('../../scene3d/scene3dContext.ts');
    const other = acquireScene3D(document.createElement('div'));

    renderView();

    expect(await screen.findByText('The 3D view could not be started')).toBeInTheDocument();
    expect(await screen.findByText(/different container/)).toBeInTheDocument();
    other.release();
  });
});
