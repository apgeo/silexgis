// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { surfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';
import { setActiveViewCamera, viewFlyTo } from '../../workspace/viewCamera.ts';

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
  useWorkspaceStore.setState({
    selection: null,
    overlayVisible: {},
    overlayOpacity: {},
    baseOpacity: {},
    scene3dSurfaceMode: 'overlay',
  });
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

  it('builds the scene inside the window\'s one drawing surface, shown in its own box', async () => {
    withWebGl2(true);
    renderView();

    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));
    const surface = screen.getByTestId('scene3d-surface');
    // The scene is always built in the same element, whichever view is showing it, which is what
    // makes a second scene in one window impossible rather than merely unlikely.
    expect(engine.engineState.widgets[0].container).toBe(surface);
    expect(screen.getByTestId('scene3d-container')).toContainElement(surface);
    await waitFor(() => expect(screen.queryByTestId('scene3d-loading')).not.toBeInTheDocument());
  });

  it('lends the one scene to a second view instead of refusing it', async () => {
    // A route and a workspace panel are both mounted for a moment during every route change. A
    // second drawing context is not merely wasteful — a browser drops the oldest once a handful
    // are alive — so the second view joins this one, and says so rather than showing a blank box.
    withWebGl2(true);
    const first = render(
      <MemoryRouter>
        <Scene3DView />
        <Scene3DView />
      </MemoryRouter>,
    );

    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));
    expect(screen.queryByText('The 3D view could not be started')).not.toBeInTheDocument();
    expect(await screen.findByTestId('scene3d-elsewhere')).toBeInTheDocument();
    // The displaced mount is inert: it does not load a second copy of the cave data into the one
    // scene, which would build two batches under each source id and take each other's out of it.
    expect(screen.getAllByTestId('scene3d-camera-controls')).toHaveLength(1);
    const sourceIds = engine.engineState.widgets[0].scene.primitives.items.length;
    expect(sourceIds).toBeGreaterThan(0);
    first.unmount();
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

  it('hands the camera back to the view it was opened beside', async () => {
    // This scene opens as a pane BESIDE the flat map, inside a page that is still mounted when the
    // pane closes and that registers its camera only once, when it mounts. A scene that cleared
    // the registration on its way out would therefore leave the map's own "zoom to" buttons doing
    // nothing at all for the rest of the visit, with nothing to say so.
    withWebGl2(true);
    const flatMap = { flyTo: vi.fn(), fitGeometry: vi.fn() };
    const detachFlatMap = setActiveViewCamera(flatMap);
    const view = renderView();
    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));

    view.unmount();
    viewFlyTo(25.5, 45.5, 16);

    expect(flatMap.flyTo).toHaveBeenCalledWith(25.5, 45.5, 16);
    detachFlatMap();
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

describe('Scene3DView camera controls', () => {
  it('turns the camera to a compass view in one press, and says which one it is at', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));

    fireEvent.click(await screen.findByTestId('scene3d-preset-north'));

    const { camera } = engine.engineState.widgets[0].scene;
    // Standing to the north of what it is looking at, facing south.
    expect((camera.heading * 180) / Math.PI).toBeCloseTo(180, 4);
    await waitFor(() =>
      expect(screen.getByTestId('scene3d-preset-north')).toHaveAttribute('aria-pressed', 'true'),
    );
  });

  it('stops claiming a preset once the viewer moves the camera themselves', async () => {
    // Presets do not latch — nothing re-applies one — so the highlight is read off the camera and
    // goes out the moment it is dragged. A preset that fought the free camera would be unusable.
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));
    fireEvent.click(await screen.findByTestId('scene3d-preset-top'));
    await waitFor(() =>
      expect(screen.getByTestId('scene3d-preset-top')).toHaveAttribute('aria-pressed', 'true'),
    );

    const { camera } = engine.engineState.widgets[0].scene;
    act(() => {
      camera.pitch = (-31 * Math.PI) / 180;
      camera.moveEnd.raise();
    });

    await waitFor(() =>
      expect(screen.getByTestId('scene3d-preset-top')).toHaveAttribute('aria-pressed', 'false'),
    );
  });

  it('takes the perspective out of the view and puts it back', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));
    const { scene } = engine.engineState.widgets[0];

    fireEvent.click(await screen.findByTestId('scene3d-projection-toggle'));
    await waitFor(() => expect(scene.camera.frustum).toBeInstanceOf(engine.OrthographicFrustum));

    fireEvent.click(screen.getByTestId('scene3d-projection-toggle'));
    await waitFor(() => expect(scene.camera.frustum).toBeInstanceOf(engine.PerspectiveFrustum));
    // The near plane a cave view needs inside a narrow passage has to be put back on the frustum
    // the switch built, which carries the library's own default.
    expect((scene.camera.frustum as InstanceType<typeof engine.PerspectiveFrustum>).near).toBe(0.5);
  });

  it('offers nothing to frame until a survey is drawn', async () => {
    withWebGl2(true);
    renderView();

    expect(await screen.findByTestId('scene3d-fit-cave')).toBeDisabled();
  });

  it('frames the cave the view is centred on once one is drawn', async () => {
    withWebGl2(true);
    centerlineResponse = {
      type: 'FeatureCollection',
      features: [
        {
          type: 'Feature',
          geometry: {
            type: 'LineString',
            coordinates: [
              [25.44, 45.53, 700],
              [25.45, 45.535, 420],
            ],
          },
          properties: { id: 'line-1', caveId: 'cave-1', hasZ: true },
        },
      ],
      withheldCount: 0,
      detail: true,
      flatCount: 0,
    };
    renderView();
    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));

    const fit = await screen.findByTestId('scene3d-fit-cave');
    await waitFor(() => expect(fit).not.toBeDisabled());
    const { camera } = engine.engineState.widgets[0].scene;
    const framedBefore = camera.framed.length;
    fireEvent.click(fit);

    // Framing a box is the one camera move the engine does by rectangle, and the double records
    // what it was asked to frame — which is the drawn survey's own extent.
    expect(camera.framed).toHaveLength(framedBefore + 1);
    expect(camera.framed.at(-1)).toMatchObject({ west: 25.44, south: 45.53, east: 25.45, north: 45.535 });
  });
});

describe('Scene3DView layer controls', () => {
  const oneBaseLayer = [
    {
      id: 1,
      name: 'OpenStreetMap',
      layerKind: 'xyz',
      urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
      attribution: null,
      isBase: true,
      isDefault: true,
      sortOrder: 0,
      options: null,
    },
  ];

  const aSurvey = {
    type: 'FeatureCollection',
    features: [
      {
        type: 'Feature',
        geometry: {
          type: 'LineString',
          coordinates: [
            [25.44, 45.53, 700],
            [25.45, 45.535, 420],
          ],
        },
        properties: { id: 'line-1', caveId: 'cave-1', hasZ: true },
      },
    ],
    withheldCount: 0,
    detail: true,
    flatCount: 0,
  };

  it('offers the layer controls once there is a scene and a catalog to name', async () => {
    withWebGl2(true);
    mapLayers = oneBaseLayer;
    renderView();

    const trigger = await screen.findByTestId('scene3d-layers-trigger');
    fireEvent.click(trigger);

    expect(await screen.findByTestId('scene3d-layer-panel')).toBeInTheDocument();
  });

  it('offers them even where the basemap catalog cannot be read', async () => {
    // The catalog is one section of the panel; the layer switches, the fades and the surface mode
    // are about the scene. An installation that refuses the catalog to this viewer — or merely
    // answers slowly — would otherwise leave them with a scene and no way to control it.
    withWebGl2(true);
    mapLayers = undefined;
    renderView();

    const trigger = await screen.findByTestId('scene3d-layers-trigger');
    fireEvent.click(trigger);

    expect(await screen.findByRole('checkbox', { name: 'Cave centerlines' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Cut it away' })).toBeInTheDocument();
  });

  it('stops drawing a layer the viewer turned off', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(centerlineRequests).toHaveLength(1));

    act(() => useWorkspaceStore.getState().setOverlayVisible('centerlines', false));

    const { primitives } = engine.engineState.widgets[0].scene;
    await waitFor(() => {
      const collection = primitives.items[0] as InstanceType<typeof engine.PolylineCollection>;
      expect(collection.show).toBe(false);
    });
  });

  it('offers no cave to frame once the viewer turns the survey layer off', async () => {
    // The control frames the cave the survey draws. With that layer off there is no survey on the
    // screen to frame, and a control left lit would fly the camera to geometry that is not drawn —
    // and go on doing so however far the viewer travelled, since a layer that is off is never
    // fetched again and so nothing would ever correct it.
    withWebGl2(true);
    centerlineResponse = aSurvey;
    renderView();
    const fit = await screen.findByTestId('scene3d-fit-cave');
    await waitFor(() => expect(fit).not.toBeDisabled());

    act(() => useWorkspaceStore.getState().setOverlayVisible('centerlines', false));

    await waitFor(() => expect(screen.getByTestId('scene3d-fit-cave')).toBeDisabled());
  });

  it('fades the basemap without touching what is drawn over it', async () => {
    // Fading the basemap is a legibility control and nothing more: it reveals nothing buried,
    // which is what the surface mode is for.
    withWebGl2(true);
    mapLayers = oneBaseLayer;
    renderView();
    await waitFor(() => expect(engine.engineState.providers).toHaveLength(1));

    act(() => useWorkspaceStore.getState().setBaseOpacity(1, 0.35));

    const { imageryLayers } = engine.engineState.widgets[0].scene;
    await waitFor(() => expect(imageryLayers.layers[0].alpha).toBe(0.35));
  });

  it('cuts the ground away over the survey when the viewer asks for it', async () => {
    withWebGl2(true);
    centerlineResponse = aSurvey;
    renderView();
    await waitFor(() => expect(centerlineRequests).toHaveLength(1));
    const { globe } = engine.engineState.widgets[0].scene;
    await waitFor(() => expect(globe.clippingPolygons?.length).toBe(1));

    act(() => useWorkspaceStore.getState().setScene3dSurfaceMode('cutaway'));

    await waitFor(() => expect(globe.clippingPolygons?.enabled).toBe(true));
  });

  it('says so when the camera angle has taken the cutaway away', async () => {
    withWebGl2(true);
    centerlineResponse = aSurvey;
    renderView();
    await waitFor(() => expect(centerlineRequests).toHaveLength(1));
    const { scene } = engine.engineState.widgets[0];
    await waitFor(() => expect(scene.globe.clippingPolygons?.length).toBe(1));
    act(() => useWorkspaceStore.getState().setScene3dSurfaceMode('cutaway'));

    // The viewer tilts towards the horizon, where the opening is edge-on and shows almost nothing.
    act(() => {
      scene.camera.pitch = (-4 * Math.PI) / 180;
      scene.render();
    });

    expect(await screen.findByText(/tilt the view down towards the cave/)).toBeInTheDocument();
  });

  it('does not tell a viewer under the ground to tilt, which would not help them', async () => {
    // Going below the surface to look up at a cave is what this view exists for. There is no
    // ground left between the camera and the cave down there, so the cutaway has nothing to
    // remove and no angle brings it back — only coming back up does.
    withWebGl2(true);
    centerlineResponse = aSurvey;
    renderView();
    await waitFor(() => expect(centerlineRequests).toHaveLength(1));
    const { scene } = engine.engineState.widgets[0];
    await waitFor(() => expect(scene.globe.clippingPolygons?.length).toBe(1));
    act(() => useWorkspaceStore.getState().setScene3dSurfaceMode('cutaway'));

    act(() => {
      scene.camera.positionWC = {
        longitudeDegrees: 25.445,
        latitudeDegrees: 45.532,
        height: -300,
      };
      scene.render();
    });

    expect(await screen.findByText(/Rise back above it/)).toBeInTheDocument();
    expect(screen.queryByText(/tilt the view down towards the cave/)).not.toBeInTheDocument();
  });
});
