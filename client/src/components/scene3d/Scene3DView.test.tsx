// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import {
  getFeatureTypeNameByCode,
  setFeatureTypeCatalog,
} from '../../map/featureTypeCatalog.ts';
import type { FeatureType } from '../../api/hooks.ts';
import { surfaceFeaturesChanged } from '../../workspace/surfaceFeatureRefresh.ts';
import { setActiveViewCamera, viewFlyTo } from '../../workspace/viewCamera.ts';

// The engine library is replaced by the same double the scene module's own suite uses; the test
// runner has no graphics context to give it.
vi.mock('cesium', () => import('../../scene3d/cesiumTestDouble.ts'));
vi.mock('../../api/hooks.ts', () => ({
  useMapLayers: () => ({ data: mapLayers }),
  useMapConfig: () => ({ data: mapConfig }),
  useFeatureTypes: () => ({ data: featureTypes }),
  // Named for the same reason as fetchSurveyModels below: the factory replaces the whole module,
  // so an unnamed export is not an empty answer but a property access that raises during render.
  // This installation has no georeferenced maps in these tests, which is the case the scene must
  // handle without asking for one.
  useRasterMaps: () => ({ data: undefined }),
  fetchCenterlineFeatures: (...args: unknown[]) => {
    centerlineRequests.push(args);
    return Promise.resolve(centerlineResponse);
  },
  fetchEntranceFeatures: () => Promise.resolve(emptyCollection),
  fetchMapFeatures: () => Promise.resolve(featureResponse),
  // Named here because leaving it out is not an empty answer: the mock factory replaces the whole
  // module, so the loader's call would raise on the property access, be swallowed by its own
  // catch, and every selection in this file would silently exercise the failure path alone.
  fetchSurveyModels: (caveId: string) => {
    surveyModelRequests.push(caveId);
    return Promise.resolve(surveyModels);
  },
}));

const engine = await import('../../scene3d/cesiumTestDouble.ts');
const { useWorkspaceStore } = await import('../../stores/workspaceStore.ts');
const { default: Scene3DView } = await import('./Scene3DView.tsx');

let mapLayers: unknown[] | undefined;
/** What the server publishes about this installation, including its elevation model if it has one. */
let mapConfig: Record<string, unknown> | undefined;
/** What each kind of surface feature is called; this view fills the catalog the labels read. */
const featureTypes = [{ id: 3, code: 'sinkhole', name: 'Sinkhole', symbolFile: null }];
let centerlineRequests: unknown[][] = [];
const emptyCollection = { type: 'FeatureCollection', features: [] };
let centerlineResponse: unknown = {
  type: 'FeatureCollection',
  features: [],
  withheldCount: 0,
  detail: false,
  flatCount: 0,
};
/** What the cross-kind overlay answers with; a test that edits a feature changes it in place. */
let featureResponse: unknown = emptyCollection;
/** Which caves the wall-mesh loader asked about, and what it was told they hold. */
let surveyModelRequests: string[] = [];
let surveyModels: unknown[] = [];

/** A converted wall mesh of cave-1, whose own zero plane sits at 500 m. */
function aWallMesh(overrides: Record<string, unknown> = {}) {
  return {
    id: 'model-1',
    caveId: 'cave-1',
    name: 'Pereți',
    format: 'stl',
    fileId: 'file-1',
    description: null,
    surveyedAt: null,
    modelUrl: '/files/source.stl',
    status: 'ready',
    processingError: null,
    meshUrl: '/files/walls.glb',
    anchorLongitude: 25.44,
    anchorLatitude: 45.53,
    anchorHeightM: 500,
    triangleCount: 1_234_567,
    sourcePrecisionLost: false,
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

/** One surface feature, whose name an edit beside the scene can change. */
function featureCollection(name: string) {
  return {
    type: 'FeatureCollection',
    features: [
      {
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [25.5, 45.5] },
        properties: { id: 'f1', name },
      },
    ],
  };
}

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

/**
 * Everything the view needs around it. The query client is here because the scene registers
 * itself as somewhere a link can be sent, and answering one means asking where the thing it
 * names is — a read, through the same cache every other read goes through.
 */
function Around({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  );
}

function renderView() {
  return render(
    <Around>
      <Scene3DView />
    </Around>,
  );
}

beforeEach(() => {
  engine.engineState.reset();
  mapLayers = undefined;
  mapConfig = undefined;
  centerlineRequests = [];
  featureResponse = emptyCollection;
  surveyModelRequests = [];
  surveyModels = [];
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
      <Around>
        <Scene3DView />
        <Scene3DView />
      </Around>,
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

  it('names what was picked over the scene, and takes the name down when the pick is dismissed', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(engine.engineState.eventHandlers).toHaveLength(1));

    const handler = engine.engineState.eventHandlers[0];
    const scene = engine.engineState.widgets[0].scene;
    scene.pickResult = {
      id: {
        kind: 'entrance',
        entranceId: 'e1',
        caveId: 'cave-1',
        label: 'Intrarea Mică',
        anchor: { longitude: 25, latitude: 45, height: 0 },
      },
    };
    act(() => handler.raise('leftClick', { position: new engine.Cartesian2(10, 20) }));

    expect(await screen.findByTestId('scene3d-callout')).toHaveTextContent('Intrarea Mică');

    fireEvent.click(screen.getByTestId('scene3d-callout-close'));
    await waitFor(() => expect(screen.queryByTestId('scene3d-callout')).toBeNull());
    // Closing a label is not deselecting: the detail panel beside the scene is still showing it.
    expect(useWorkspaceStore.getState().selection).toEqual({
      kind: 'entrance',
      entranceId: 'e1',
      caveId: 'cave-1',
    });
  });

  it('takes the name down when the selection moves on somewhere else entirely', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(engine.engineState.eventHandlers).toHaveLength(1));

    const handler = engine.engineState.eventHandlers[0];
    const scene = engine.engineState.widgets[0].scene;
    scene.pickResult = {
      id: {
        kind: 'entrance',
        entranceId: 'e1',
        caveId: 'cave-1',
        label: 'Intrarea Mică',
        anchor: { longitude: 25, latitude: 45, height: 0 },
      },
    };
    act(() => handler.raise('leftClick', { position: new engine.Cartesian2(10, 20) }));
    expect(await screen.findByTestId('scene3d-callout')).toBeInTheDocument();

    // Selecting on the flat map, in a table, or in another window reaches this view only as a
    // change of selection — there is no other signal, and without acting on it the callout would
    // sit over the scene naming something the viewer has moved on from.
    act(() => useWorkspaceStore.setState({ selection: { kind: 'cave', caveId: 'cave-9' } }));

    await waitFor(() => expect(screen.queryByTestId('scene3d-callout')).toBeNull());
  });

  it('learns what each kind of feature is called, without the flat map being opened first', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(engine.engineState.widgets).toHaveLength(1));

    // The flat map fills the same catalog when it opens. A session that goes straight to the 3D
    // route never opens it, and without this every feature here would be named as its kind alone
    // or not at all.
    await waitFor(() => expect(getFeatureTypeNameByCode('sinkhole')).toBe('Sinkhole'));
  });

  it('names what the pointer is resting on, once the scene has drawn a frame', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(engine.engineState.eventHandlers).toHaveLength(1));

    const handler = engine.engineState.eventHandlers[0];
    const scene = engine.engineState.widgets[0].scene;
    scene.pickResult = { id: { kind: 'feature', featureId: 'f1', label: 'Dolina Demo' } };
    // Hover is throttled to one hit test per drawn frame, so the move alone answers nothing.
    handler.raise('mouseMove', { endPosition: new engine.Cartesian2(40, 50) });
    await act(async () => {
      await new Promise((resolve) => requestAnimationFrame(() => resolve(undefined)));
    });

    expect(await screen.findByTestId('scene3d-hover-tooltip')).toHaveTextContent('Dolina Demo');
  });

  it('stops naming what the pointer was on once the pointer has left the scene', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(engine.engineState.eventHandlers).toHaveLength(1));

    const handler = engine.engineState.eventHandlers[0];
    const widget = engine.engineState.widgets[0];
    widget.scene.pickResult = { id: { kind: 'feature', featureId: 'f1', label: 'Dolina Demo' } };
    handler.raise('mouseMove', { endPosition: new engine.Cartesian2(40, 50) });
    await act(async () => {
      await new Promise((resolve) => requestAnimationFrame(() => resolve(undefined)));
    });
    expect(await screen.findByTestId('scene3d-hover-tooltip')).toBeInTheDocument();

    // Nothing else can say so. The scene's pointer listeners are on its own drawing surface, so
    // once the pointer is off it — onto the callout, onto the controls, onto the browser's own
    // chrome, into another window — no further move ever arrives, and the last name stays pinned
    // over the middle of the scene with the pointer cursor stuck under it.
    act(() => {
      widget.canvas.dispatchEvent(new Event('pointerleave'));
    });

    await waitFor(() => expect(screen.queryByTestId('scene3d-hover-tooltip')).toBeNull());
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

  it('draws the features again once it learns what their types are called', async () => {
    withWebGl2(true);
    renderView();
    await waitFor(() => expect(centerlineRequests).toHaveLength(1));

    // What a feature is called is composed into the item as it is built, so anything drawn before
    // the taxonomy answered carries no type name at all — an unnamed sinkhole ends up labelled
    // "surface features" wherever it is named, and stays that way until the camera happens to
    // move. The taxonomy is a request of its own and can perfectly well answer after the features
    // do, all the more so on a cold load of this route where the scene is a megabyte of engine.
    // Only the three fields the catalog reads are given; the rest of what the taxonomy endpoint
    // serves has no bearing on what a feature is called.
    act(() =>
      setFeatureTypeCatalog([
        { id: 4, code: 'cave', name: 'Cave', symbolFile: null } as FeatureType,
      ]),
    );

    await waitFor(() => expect(centerlineRequests).toHaveLength(2));
  });

  it('shows the name a picked feature has now, not the one it had when it was clicked', async () => {
    withWebGl2(true);
    featureResponse = featureCollection('Doline veche');
    renderView();
    await waitFor(() => expect(engine.engineState.eventHandlers).toHaveLength(1));

    const handler = engine.engineState.eventHandlers[0];
    const scene = engine.engineState.widgets[0].scene;
    scene.pickResult = {
      id: {
        kind: 'feature',
        featureId: 'f1',
        label: 'Doline veche',
        anchor: { longitude: 25.5, latitude: 45.5, height: 0 },
      },
    };
    act(() => handler.raise('leftClick', { position: new engine.Cartesian2(10, 20) }));
    expect(await screen.findByTestId('scene3d-callout')).toHaveTextContent('Doline veche');

    // Renamed in the panel beside the scene, which announces and this refetches. The callout holds
    // the payload it was handed at the moment of the click, and every item in the scene has just
    // been replaced by one carrying the new name — so without finding its own thing again among
    // them it would state the old name beside a panel stating the new one.
    featureResponse = featureCollection('Doline nouă');
    act(() => surfaceFeaturesChanged());

    await waitFor(() =>
      expect(screen.getByTestId('scene3d-callout')).toHaveTextContent('Doline nouă'),
    );
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

describe('the ground the caves are drawn against', () => {
  /** A survey whose top is at 700 m and which drops to 420 m. */
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
        properties: { id: 'line-1', caveId: 'cave-1', topAltitudeM: 700, hasZ: true },
      },
    ],
    withheldCount: 0,
    detail: true,
    flatCount: 0,
  };

  const LAYER_JSON = {
    format: 'quantized-mesh-1.0',
    available: [[{ startX: 0, endX: 2, startY: 0, endY: 1 }]],
  };

  /**
   * A web server holding a pyramid at `/terrain/`, or — with `tile` set to a gzip stream — one
   * serving it in the form that draws a globe with no ground on it and reports nothing.
   */
  function servingTerrain(tile = new Uint8Array([0x8d, 0x97, 0x6e, 0x3f])) {
    vi.stubGlobal('fetch', async (input: RequestInfo | URL) => {
      const url = String(input);
      const bytes = url.endsWith('layer.json')
        ? new TextEncoder().encode(JSON.stringify(LAYER_JSON))
        : tile;
      return { ok: true, status: 200, arrayBuffer: async () => bytes.buffer } as Response;
    });
  }

  /** The heights the survey was actually drawn at, out of every line handed to the scene. */
  function drawnHeights(): number[] {
    return engine.engineState.widgets[0].scene.primitives.items
      .filter(
        (item): item is InstanceType<typeof engine.PolylineCollection> =>
          item instanceof engine.PolylineCollection,
      )
      .flatMap((collection) => collection.polylines)
      .flatMap((line) => line.positions.map((position) => position.height));
  }

  it('draws a smooth globe, and hangs the caves from it, when nothing is configured', async () => {
    withWebGl2(true);
    centerlineResponse = aSurvey;
    renderView();
    await waitFor(() => expect(centerlineRequests).toHaveLength(1));

    // Nothing was fetched and nothing was read: the shipped installation needs no elevation
    // server, no download and no pre-baking.
    expect(engine.engineState.terrainRequests).toEqual([]);
    await waitFor(() => expect(drawnHeights()).toContain(0));
    // The cave's top on the surface, everything else at its depth below it.
    expect(Math.min(...drawnHeights())).toBe(-280);
  });

  it('reads the configured pyramid and then puts the caves at their real altitude', async () => {
    withWebGl2(true);
    centerlineResponse = aSurvey;
    servingTerrain();
    mapConfig = {
      centerlineDetailZoom: 18,
      centerlineMaxPaths: 25000,
      terrain: { url: '/terrain/', attribution: '© Copernicus', surveyHeightOffsetM: 0 },
    };

    renderView();

    await waitFor(() => expect(engine.engineState.terrainRequests).toHaveLength(1));
    expect(engine.engineState.terrainRequests[0].url).toBe('/terrain/');
    // The survey moves from hanging off the ellipsoid to sitting where it was surveyed, which
    // with a hillside drawn is inside it.
    await waitFor(() => expect(drawnHeights()).toContain(700));
    expect(Math.min(...drawnHeights())).toBe(420);
  });

  it('raises the caves by the correction the source declares, and by nothing else', async () => {
    withWebGl2(true);
    centerlineResponse = aSurvey;
    servingTerrain();
    mapConfig = {
      centerlineDetailZoom: 18,
      centerlineMaxPaths: 25000,
      // A pyramid converted to heights above the ellipsoid when it was baked. The server worked
      // this number out from the datum the source declares; the client is handed the answer.
      terrain: { url: '/terrain/', attribution: null, surveyHeightOffsetM: 43.03 },
    };

    renderView();

    await waitFor(() => expect(Math.max(...drawnHeights())).toBeCloseTo(743.03, 6));
  });

  it('refuses a pyramid served in a form that cannot be drawn, and says so', async () => {
    // The whole reason the check exists. Handing this to the engine would resolve, answer 200 to
    // every tile, raise no error anywhere, and draw a globe with no ground on it — so the caves
    // stay hung from the surface and a sentence on screen says what is wrong.
    withWebGl2(true);
    centerlineResponse = aSurvey;
    servingTerrain(new Uint8Array([0x1f, 0x8b, 0x08, 0x00]));
    mapConfig = {
      centerlineDetailZoom: 18,
      centerlineMaxPaths: 25000,
      terrain: { url: '/terrain/', attribution: null, surveyHeightOffsetM: 0 },
    };

    renderView();

    expect(await screen.findByText(/compression that does not match/)).toBeInTheDocument();
    expect(engine.engineState.terrainRequests).toEqual([]);
    expect(drawnHeights()).toContain(0);
  });

  it('takes a pyramid it can no longer verify off the globe, not just off the caves', async () => {
    // What is on screen and what is said about it are one statement. Refusing a source without
    // detaching the one already drawing leaves the globe with a hillside on it while every cave
    // is placed for a smooth one — each survey drawn a hillside below its own entrance marker —
    // under a sentence saying the ground is a smooth globe, which sends the operator to check
    // something that is not wrong. Reached by re-pointing the address at a directory that is not
    // there yet, which is an ordinary step of a re-bake.
    withWebGl2(true);
    centerlineResponse = aSurvey;
    servingTerrain();
    mapConfig = {
      centerlineDetailZoom: 18,
      centerlineMaxPaths: 25000,
      terrain: { url: '/terrain/', attribution: null, surveyHeightOffsetM: 0 },
    };

    const view = renderView();
    await waitFor(() => expect(drawnHeights()).toContain(700));
    expect(engine.engineState.widgets[0].scene.terrainProvider).toBeInstanceOf(
      engine.CesiumTerrainProvider,
    );

    // A web server told to fall back to a single-page application answers 200 with HTML for a
    // path that does not exist, which is what a re-pointed pyramid looks like before it arrives.
    vi.stubGlobal(
      'fetch',
      async () =>
        ({
          ok: true,
          status: 200,
          arrayBuffer: async () => new TextEncoder().encode('<!doctype html>').buffer,
        }) as Response,
    );
    mapConfig = {
      ...mapConfig,
      terrain: { url: '/elevation/', attribution: null, surveyHeightOffsetM: 0 },
    };
    view.rerender(
      <Around>
        <Scene3DView />
      </Around>,
    );

    expect(await screen.findByText(/does not hold a terrain tile set/)).toBeInTheDocument();
    await waitFor(() =>
      expect(engine.engineState.widgets[0].scene.terrainProvider).toBeInstanceOf(
        engine.EllipsoidTerrainProvider,
      ),
    );
    await waitFor(() => expect(drawnHeights()).toContain(0));
  });

  it('says which way to look when the address holds no pyramid at all', async () => {
    withWebGl2(true);
    vi.stubGlobal('fetch', async () =>
      ({
        ok: true,
        status: 200,
        arrayBuffer: async () => new TextEncoder().encode('<!doctype html>').buffer,
      }) as Response,
    );
    mapConfig = {
      centerlineDetailZoom: 18,
      centerlineMaxPaths: 25000,
      terrain: { url: '/terrain/', attribution: null, surveyHeightOffsetM: 0 },
    };

    renderView();

    expect(await screen.findByText(/does not hold a terrain tile set/)).toBeInTheDocument();
    expect(engine.engineState.terrainRequests).toEqual([]);
  });
});

describe('the walls of the selected cave', () => {
  /** A survey of cave-1 whose top the server reports at 700 m. */
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
        properties: { id: 'line-1', caveId: 'cave-1', topAltitudeM: 700, hasZ: true },
      },
    ],
    withheldCount: 0,
    detail: true,
    flatCount: 0,
  };

  /** Where the scene actually put the model it was handed, in metres above the ellipsoid. */
  function meshHeight(): number | undefined {
    return engine.engineState.modelRequests.at(-1)?.modelMatrix.origin.height;
  }

  function selectCave(caveId: string) {
    act(() => {
      useWorkspaceStore.setState({ selection: { kind: 'cave', caveId } });
    });
  }

  it('reads a mesh only once a cave is picked, and hangs it from that cave’s survey top', async () => {
    withWebGl2(true);
    centerlineResponse = aSurvey;
    surveyModels = [aWallMesh()];
    renderView();
    await waitFor(() => expect(centerlineRequests).toHaveLength(1));

    // Nothing is read for a scene nobody has picked a cave in. This overlay costs tens of
    // megabytes of graphics memory, so it is not driven by what the camera can see.
    expect(surveyModelRequests).toEqual([]);

    selectCave('cave-1');
    await waitFor(() => expect(surveyModelRequests).toEqual(['cave-1']));
    await waitFor(() => expect(engine.engineState.modelRequests).toHaveLength(1));
    expect(engine.engineState.modelRequests[0].url).toBe('/files/walls.glb');

    // The mesh declares its own zero plane at 500 m, and the cave's survey top is 700 m. On the
    // bare ellipsoid the cave hangs from the surface by its top, so the mesh's origin sits 200 m
    // below it — the same answer the survey lines drawn inside it get. Placing it by its own
    // origin would put it on the surface, 200 m above the lines it belongs with.
    expect(meshHeight()).toBe(-200);
  });

  it('says that the walls are on their way, and how big they are, without the panel being opened', async () => {
    withWebGl2(true);
    centerlineResponse = aSurvey;
    surveyModels = [aWallMesh()];
    renderView();
    await waitFor(() => expect(centerlineRequests).toHaveLength(1));
    selectCave('cave-1');

    // The layer panel opens on a click. The acceptance case is a mesh of some tens of megabytes,
    // and a viewer staring at an unchanged scene has no reason to go looking behind a button for
    // the news that anything is happening at all.
    expect(await screen.findByText(/1,234,567/)).toBeInTheDocument();
    expect(screen.queryByTestId('scene3d-layer-panel')).toBeNull();
    await waitFor(() =>
      expect(screen.getByTestId('scene3d-data')).toHaveAttribute('data-loading', 'true'),
    );

    act(() => engine.Model.deliver('/files/walls.glb'));

    await waitFor(() =>
      expect(screen.getByTestId('scene3d-data')).toHaveAttribute('data-loading', 'false'),
    );
  });

  it('says why the walls are missing rather than leaving the scene quietly empty', async () => {
    withWebGl2(true);
    centerlineResponse = aSurvey;
    // The conversion ran and could not read the file. Its own words are the useful ones: they name
    // a file the uploader can re-export.
    surveyModels = [
      aWallMesh({ status: 'failed', meshUrl: null, processingError: 'Not a binary STL.' }),
    ];
    renderView();
    await waitFor(() => expect(centerlineRequests).toHaveLength(1));
    selectCave('cave-1');

    expect(await screen.findByText(/Not a binary STL\./)).toBeInTheDocument();
    expect(engine.engineState.modelRequests).toEqual([]);
  });

  it('releases the walls when the layer is switched off, and reads them again when it is back on', async () => {
    withWebGl2(true);
    centerlineResponse = aSurvey;
    surveyModels = [aWallMesh()];
    renderView();
    await waitFor(() => expect(centerlineRequests).toHaveLength(1));
    selectCave('cave-1');
    await waitFor(() => expect(engine.engineState.modelRequests).toHaveLength(1));
    act(() => engine.Model.deliver('/files/walls.glb'));

    // Switching the layer off is what a viewer does to get the graphics memory back, so hiding
    // the model rather than dropping it would be the one thing they asked for not happening.
    act(() => {
      useWorkspaceStore.setState({ overlayVisible: { 'survey-mesh': false } });
    });
    await waitFor(() =>
      expect(
        engine.engineState.widgets[0].scene.primitives.items.filter(
          (item): item is InstanceType<typeof engine.Model> => item instanceof engine.Model,
        ),
      ).toHaveLength(0),
    );

    act(() => {
      useWorkspaceStore.setState({ overlayVisible: { 'survey-mesh': true } });
    });
    await waitFor(() => expect(engine.engineState.modelRequests).toHaveLength(2));
  });
});
