// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// The engine library is replaced wholesale: it needs a graphics context the runner does not have,
// and every behaviour under test here belongs to this application's configuration of it rather
// than to the library itself.
vi.mock('cesium', () => import('./cesiumTestDouble.ts'));

// Imported after the mock is declared, so the module under test binds to the double. The scene it
// owns is a per-window singleton held in module scope, exactly like the drawing context it stands
// for — so every test below releases what it acquired, and the module is back to having no scene
// by the time the next one runs.
const engine = await import('./cesiumTestDouble.ts');
const { acquireScene3D } = await import('./scene3dContext.ts');
type Scene3DSession = ReturnType<typeof acquireScene3D>;

function container(): HTMLElement {
  return document.createElement('div');
}

// Every hold taken through this is released after the test, so a failing assertion cannot leave a
// scene attached and take the rest of the file down with it. Releasing twice is a no-op by design.
const held: Scene3DSession[] = [];

function acquire(element: HTMLElement = container()): Scene3DSession {
  const session = acquireScene3D(element);
  held.push(session);
  return session;
}

beforeEach(() => {
  engine.engineState.reset();
});

afterEach(() => {
  for (const session of held.splice(0)) {
    session.release();
  }
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('scene configuration', () => {
  it('never lets the engine build its own basemap from the vendor service', async () => {
    acquire();

    // Omitting this option makes the widget construct hosted vendor imagery, which is both an
    // outbound request and a basemap nobody configured.
    expect(engine.engineState.widgetOptions.at(-1)!.baseLayer).toBe(false);
  });

  it('clears the vendor access token', () => {
    // The double starts out holding the vendor default the real library ships with, and only the
    // scene module can have changed it — at its own module scope, so the token is already gone
    // before any code can reach for a scene.
    expect(engine.VENDOR_DEFAULT_TOKEN).not.toBe('');
    expect(engine.Ion.defaultAccessToken).toBe('');
  });

  it('draws on demand rather than continuously', async () => {
    acquire();
    expect(engine.engineState.widgetOptions.at(-1)!.requestRenderMode).toBe(true);
  });

  it('leaves nothing that would ask for a frame while the view is standing still', async () => {
    acquire();
    const options = engine.engineState.widgetOptions.at(-1)!;

    // These two are what make "draw on demand" mean anything. A non-zero render-time change makes
    // the scene redraw whenever simulation time has moved that far, and a running clock is what
    // moves it — so either one alone turns an idle view into a permanent redraw. Both happen to be
    // the library's own defaults, which is exactly why they are pinned and asserted: a version
    // that changed either would cost every phone its battery and look identical doing it.
    expect(options.maximumRenderTimeChange).toBe(0);
    expect(options.shouldAnimate).toBe(false);
  });

  it('spends its frame budget on the survey rather than on smoothing the whole surface', async () => {
    acquire();
    const options = engine.engineState.widgetOptions.at(-1)!;
    const { scene } = engine.engineState.widgets.at(-1)!;

    // Measured on this scene: the engine's four samples cost a third of the frame, and the
    // post-pass that replaces them costs nothing this instrument could see. The pair is asserted
    // together because taking either one on its own is a bad trade — samples alone loses the
    // smoothing, the post-pass alone pays twice for it.
    expect(options.msaaSamples).toBe(1);
    expect(scene.postProcessStages.fxaa.enabled).toBe(true);
  });

  it('builds no sky, which this view never looks at', async () => {
    acquire();
    const options = engine.engineState.widgetOptions.at(-1)!;

    // False rather than hidden, and that is the point of the assertion: an object that exists and
    // is switched off has already downloaded its textures. These skip creating the star field, the
    // sun, the moon and the atmosphere, and with them 865 KiB fetched on every cold start.
    expect(options.skyBox).toBe(false);
    expect(options.skyAtmosphere).toBe(false);
  });

  it('replaces the engine failure panel rather than showing its untranslated one', async () => {
    acquire();
    expect(engine.engineState.widgetOptions.at(-1)!.showRenderLoopErrors).toBe(false);
  });

  it('asks for no terrain source, so a stock deployment needs none', async () => {
    acquire();
    const options = engine.engineState.widgetOptions.at(-1)!;
    expect(options.terrainProvider).toBeUndefined();
    expect(options.terrain).toBeUndefined();
  });

  it('shows cave data through the terrain instead of behind it', async () => {
    acquire();
    expect(engine.engineState.widgets.at(-1)!.scene.globe.depthTestAgainstTerrain).toBe(false);
  });

  it('moves the near plane close enough to stand inside a passage', async () => {
    acquire();
    // The engine default of 1 m clips the walls away from a camera inside a narrow passage.
    expect(engine.engineState.widgets.at(-1)!.scene.camera.frustum.near).toBe(0.5);
  });

  it('opens over the same ground as the 2D map, looking straight down', async () => {
    acquire();

    const { camera } = engine.engineState.widgets.at(-1)!.scene;
    expect(camera.positionWC.longitudeDegrees).toBeCloseTo(25.3, 6);
    expect(camera.positionWC.latitudeDegrees).toBeCloseTo(45.7, 6);
    expect(camera.positionWC.height).toBeGreaterThan(0);
    expect(engine.CesiumMath.toDegrees(camera.pitch)).toBeCloseTo(-90, 6);
    // The opening view is a jump, not a flight — there is nothing to fly from.
    expect(camera.flightCount).toBe(0);
  });
});

describe('air gap', () => {
  it('makes no request to the engine vendor while a scene is created and torn down', async () => {
    // The library reaches the network through more than one API, so all of them are watched. What
    // this pins down is this application's configuration of the engine, which is where the
    // guarantee actually lives: the library's own code still contains the vendor URLs, so nothing
    // here could be proved by searching the bundle for them.
    const requested: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: unknown) => {
        requested.push(String(input));
        return new Response(null);
      }),
    );
    vi.spyOn(XMLHttpRequest.prototype, 'open').mockImplementation(function stubOpen(
      _method: string,
      url: string | URL,
    ) {
      requested.push(String(url));
    });
    vi.spyOn(HTMLImageElement.prototype, 'src', 'set').mockImplementation((url: string) => {
      requested.push(url);
    });

    const session = acquire();
    session.engine.addImageryLayer('base:1', {
      urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
    });
    session.engine.flyToZoom(25.3, 45.7, 12);
    session.release();

    expect(requested.filter((url) => /(^|\/\/|\.)cesium\.com/.test(url))).toEqual([]);
  });

  it('builds imagery only from the URL it was given', async () => {
    const session = acquire();
    session.engine.addImageryLayer('base:1', {
      urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
      attribution: '© OpenStreetMap contributors',
    });

    expect(engine.engineState.providers).toEqual([
      {
        url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
        // Shown on the scene, not behind the engine's attribution link: the tile licence asks
        // for visible credit, and that is what the 2D map does.
        credit: new engine.Credit('© OpenStreetMap contributors', true),
        // Nothing picks features out of a basemap, and leaving it on invites a request to a
        // feature-info URL the catalog never configured.
        enablePickFeatures: false,
      },
    ]);
  });

  it('gives a layer with no attribution no credit at all, rather than an empty one', async () => {
    const session = acquire();
    // The catalog types attribution as optional, so this is an ordinary administrator-created
    // layer, not a malformed one.
    session.engine.addImageryLayer('base:1', { urlTemplate: 'https://a/{z}/{x}/{y}' });

    // An empty string is not the same as nothing: the engine wraps any string into a credit
    // object, and a credit that is not marked for on-screen display lands in its collapsed
    // attribution list. A single entry there — even a blank one — reveals a hard-coded English
    // link and an untranslated panel, which is exactly the engine chrome this view keeps off
    // the screen. Nothing else in the scene contributes to that list, so this is its only source.
    expect(engine.engineState.providers.at(-1)!.credit).toBeUndefined();
    session.release();
  });
});

describe('holding and releasing the scene', () => {
  it('shares one scene between two holders and tears it down when the last lets go', async () => {
    const element = container();

    const first = acquire(element);
    const second = acquire(element);
    expect(engine.engineState.widgets).toHaveLength(1);
    expect(second.engine).toBe(first.engine);

    first.release();
    expect(second.engine.isDestroyed()).toBe(false);

    second.release();
    expect(engine.engineState.widgets[0].isDestroyed()).toBe(true);
  });

  it('rebuilds cleanly after a development double mount', async () => {
    const element = container();

    // What React does in development: setup, cleanup, setup again, on the same element.
    const first = acquire(element);
    first.release();
    const second = acquire(element);

    expect(engine.engineState.widgets).toHaveLength(2);
    expect(engine.engineState.widgets[0].isDestroyed()).toBe(true);
    expect(second.engine.isDestroyed()).toBe(false);

    second.release();
    expect(engine.engineState.widgets[1].isDestroyed()).toBe(true);
  });

  it('keeps the drawing context when the two mounts overlap', async () => {
    const element = container();

    // The overlapping order: the second setup runs before the first cleanup does.
    const first = acquire(element);
    const second = acquire(element);
    first.release();

    expect(engine.engineState.widgets).toHaveLength(1);
    expect(second.engine.isDestroyed()).toBe(false);

    second.release();
    expect(engine.engineState.widgets[0].isDestroyed()).toBe(true);
  });

  it('ignores a release that runs twice, so the count cannot go negative', async () => {
    const element = container();

    const first = acquire(element);
    const second = acquire(element);
    first.release();
    first.release();

    // Had the second release counted, the scene would be gone with a holder still using it.
    expect(second.engine.isDestroyed()).toBe(false);
    second.release();
    expect(engine.engineState.widgets[0].destroyCount).toBe(1);
  });

  it('tears the scene down exactly once even if destroy is also called directly', async () => {
    const session = acquire();

    session.engine.destroy();
    session.engine.destroy();
    session.release();

    // The real widget throws on a second teardown; only one call ever reaches it.
    expect(engine.engineState.widgets[0].destroyCount).toBe(1);
  });

  it('refuses to attach one scene to two different elements', async () => {
    const session = acquire();

    expect(() => acquire()).toThrow(/different container/);

    session.release();
    // Once released, another element may have the scene.
    expect(() => acquire()).not.toThrow();
  });
});

describe('imagery layers', () => {
  it('adds, lists and removes layers by the id the caller chose', async () => {
    const session = acquire();

    session.engine.addImageryLayer('base:1', { urlTemplate: 'https://a/{z}/{x}/{y}' });
    session.engine.addImageryLayer('base:2', { urlTemplate: 'https://b/{z}/{x}/{y}' });

    expect(session.engine.getImageryLayerIds()).toEqual(['base:1', 'base:2']);
    expect(session.engine.hasImageryLayer('base:1')).toBe(true);

    session.engine.removeImageryLayer('base:1');
    expect(session.engine.getImageryLayerIds()).toEqual(['base:2']);
    expect(engine.engineState.widgets[0].scene.imageryLayers.layers).toHaveLength(1);
    session.release();
  });

  it('ignores an id that is already in the scene', async () => {
    const session = acquire();

    session.engine.addImageryLayer('base:1', { urlTemplate: 'https://a/{z}/{x}/{y}' });
    session.engine.addImageryLayer('base:1', { urlTemplate: 'https://other/{z}/{x}/{y}' });

    expect(engine.engineState.widgets[0].scene.imageryLayers.layers).toHaveLength(1);
    expect(engine.engineState.providers).toHaveLength(1);
    session.release();
  });

  it('applies visibility and opacity to the layer they name', async () => {
    const session = acquire();

    session.engine.addImageryLayer('base:1', {
      urlTemplate: 'https://a/{z}/{x}/{y}',
      visible: false,
    });
    session.engine.addImageryLayer('base:2', { urlTemplate: 'https://b/{z}/{x}/{y}' });

    session.engine.setImageryLayerVisible('base:1', true);
    session.engine.setImageryLayerOpacity('base:2', 0.25);

    const [first, second] = engine.engineState.widgets[0].scene.imageryLayers.layers;
    expect(first.show).toBe(true);
    expect(second.alpha).toBe(0.25);
    session.release();
  });

  it('does nothing for an id the scene does not have', async () => {
    const session = acquire();

    expect(() => {
      session.engine.setImageryLayerVisible('missing', true);
      session.engine.setImageryLayerOpacity('missing', 0.5);
      session.engine.removeImageryLayer('missing');
    }).not.toThrow();
    session.release();
  });
});

describe('camera', () => {
  it('round-trips a camera state', async () => {
    const session = acquire();

    const state = { longitude: 22.5, latitude: 45.1, height: 1800, heading: 30, pitch: -45, roll: 0 };
    session.engine.setCamera(state);

    // Compared field by field: angles go out as radians and come back as degrees, so an exact
    // equality would be asserting the floating-point conversion rather than the round trip.
    const read = session.engine.getCamera();
    for (const key of Object.keys(state) as (keyof typeof state)[]) {
      expect(read[key]).toBeCloseTo(state[key], 9);
    }
    session.release();
  });

  it('reports a heading of a full turn as north', async () => {
    const session = acquire();
    const { camera } = engine.engineState.widgets[0].scene;

    // What the engine reports for a camera pointing straight down.
    camera.heading = 2 * Math.PI;

    // 360 and 0 are the same bearing, and a saved view comparing the two would see a change
    // where the camera never moved.
    expect(session.engine.getCamera().heading).toBe(0);
    session.release();
  });

  it('reports a level camera as level at a tilt, not as tipped right over', async () => {
    const session = acquire();

    // Placed level and tilted a little below the horizon — which is what each of the standard
    // compass views is. The engine measures roll from the camera's own axes and folds the answer
    // into a whole turn, so what it holds for this camera is a whole turn rather than nothing.
    session.engine.setCamera({
      longitude: 22.5, latitude: 45.1, height: 900, heading: 180, pitch: -10, roll: 0,
    });
    expect(engine.engineState.widgets[0].scene.camera.roll).toBeCloseTo(2 * Math.PI, 9);

    // Passed on raw, that value says the camera is upside down, and everything downstream that
    // asks whether a camera is level would answer no for every camera not pointing straight down.
    expect(session.engine.getCamera().roll).toBeCloseTo(0, 9);
    session.release();
  });

  it('animates only when asked to', async () => {
    const session = acquire();
    const { camera } = engine.engineState.widgets[0].scene;
    const before = camera.flightCount;

    session.engine.setCamera(
      { longitude: 1, latitude: 2, height: 3, heading: 0, pitch: -90, roll: 0 },
      { animate: true },
    );

    expect(camera.flightCount).toBe(before + 1);
    session.release();
  });

  it('reports back the map zoom it was flown to', async () => {
    const session = acquire();

    session.engine.flyToZoom(25.3, 45.7, 14);

    // The zoom a camera height was derived from is the zoom read back out of it, which is what
    // lets a 3D view ask the map endpoints for the same representation the 2D map would get.
    expect(session.engine.getPseudoZoom()).toBeCloseTo(14, 6);
    session.release();
  });

  it('measures zoom from the ground being looked at, not from sea level', async () => {
    const session = acquire();

    session.engine.flyToZoom(25.3, 45.7, 14);
    const overSeaLevel = session.engine.getPseudoZoom();

    // The same camera with a mountain under it sees less ground, so it is a closer zoom.
    engine.engineState.widgets[0].scene.globe.terrainHeight = 1500;
    expect(session.engine.getPseudoZoom()).toBeGreaterThan(overSeaLevel);
    session.release();
  });

  it('does not believe a ground height the earth does not have', async () => {
    const session = acquire();

    session.engine.flyToZoom(25.3, 45.7, 14);
    const overSeaLevel = session.engine.getPseudoZoom();

    // The globe answers about the ground from whatever surface tile it is holding, and just after
    // the camera arrives somewhere new that is a coarse one whose flat mesh runs tens of kilometres
    // under the curve it stands for. Believed, it makes a camera nine hundred metres over a cave
    // report a zoom thirty-seven kilometres up — a wide-area request, answered with counts instead
    // of names, that nothing afterwards corrects because nothing afterwards moves the camera.
    engine.engineState.widgets[0].scene.globe.terrainHeight = -35_966;
    expect(session.engine.getPseudoZoom()).toBeCloseTo(overSeaLevel, 6);

    // A height the earth does have is still honoured, in both directions.
    engine.engineState.widgets[0].scene.globe.terrainHeight = -400;
    expect(session.engine.getPseudoZoom()).toBeLessThan(overSeaLevel);
    session.release();
  });

  it('says how high to be for a map zoom, which is the inverse of the zoom it reports', async () => {
    const session = acquire();

    const height = session.engine.cameraHeightForZoom(14, 45.7);
    session.engine.setCamera({
      longitude: 25.3,
      latitude: 45.7,
      height,
      heading: 0,
      pitch: -90,
      roll: 0,
    });

    expect(session.engine.getPseudoZoom()).toBeCloseTo(14, 6);
    session.release();
  });

  it('reports the ground the middle of the screen is showing', async () => {
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    scene.pickedPosition = { longitudeDegrees: 25.44, latitudeDegrees: 45.53, height: 800 };

    // What a preset turns around and what a saved view is really about: a view is remembered as a
    // place seen from a direction, and only the place survives being reopened in a different
    // window shape.
    const target = session.engine.getCameraTarget()!;
    expect(target.longitude).toBeCloseTo(25.44, 9);
    expect(target.latitude).toBeCloseTo(45.53, 9);
    expect(target.height).toBeCloseTo(800, 6);
    session.release();
  });

  it('has no target when the middle of the screen is sky', async () => {
    const session = acquire();
    engine.engineState.widgets[0].scene.pickedPosition = undefined;

    expect(session.engine.getCameraTarget()).toBeUndefined();
    session.release();
  });

  it('frames a bounding box, and falls back to a close-up for a single point', async () => {
    const session = acquire();
    const { camera } = engine.engineState.widgets[0].scene;

    session.engine.fitBounds([22, 45, 23, 46]);
    expect(camera.framed).toEqual([{ west: 22, south: 45, east: 23, north: 46 }]);

    session.engine.fitBounds([22.5, 45.5, 22.5, 45.5]);
    // A point has no rectangle to frame; the camera goes to a fixed close-up over it instead.
    expect(camera.framed).toHaveLength(1);
    expect(camera.positionWC.longitudeDegrees).toBeCloseTo(22.5, 6);
    expect(session.engine.getPseudoZoom()).toBeCloseTo(17, 6);
    session.release();
  });

  it('frames a box that is flat in one axis instead of collapsing it to a point', async () => {
    const session = acquire();
    const { camera } = engine.engineState.widgets[0].scene;

    // Two entrances on the same meridian: no longitude width at all, but 55 km north to south.
    // Treating that as a point would put the camera a few hundred metres up over the midpoint
    // and leave all but half a kilometre of the extent off screen.
    session.engine.fitBounds([25, 45, 25, 45.5]);
    expect(camera.framed).toEqual([{ west: 25, south: 45, east: 25, north: 45.5 }]);

    // And the same the other way round: a survey line running due east.
    session.engine.fitBounds([25, 45, 25.5, 45]);
    expect(camera.framed).toEqual([
      { west: 25, south: 45, east: 25, north: 45.5 },
      { west: 25, south: 45, east: 25.5, north: 45 },
    ]);
    session.release();
  });
});

describe('coordinates', () => {
  it('puts what the camera is over in the middle, and moves it when the camera moves', async () => {
    const session = acquire();
    const { longitude, latitude, height } = session.engine.getCamera();

    const middle = session.engine.positionToScreen({ longitude, latitude, height: 0 })!;
    expect(middle.x).toBeCloseTo(600, 6);
    expect(middle.y).toBeCloseTo(400, 6);

    // East is to the right and level with it, on a camera looking straight down and facing north.
    const east = session.engine.positionToScreen({ longitude: longitude + 0.01, latitude, height: 0 })!;
    expect(east.x).toBeGreaterThan(middle.x);
    expect(east.y).toBeCloseTo(middle.y, 6);

    // North is UP the screen. That is the axis flip the engine applies as its very last step, and
    // it is what anything placed from this would otherwise ship upside down.
    const north = session.engine.positionToScreen({ longitude, latitude: latitude + 0.01, height: 0 })!;
    expect(north.y).toBeLessThan(middle.y);

    // The same place seen from somewhere else is somewhere else on the screen. Without this a
    // label pinned to a cave would sit where it was first drawn while the viewer flew away.
    session.engine.setCamera({
      longitude: longitude + 0.02,
      latitude,
      height,
      heading: 0,
      pitch: -90,
      roll: 0,
    });
    expect(session.engine.positionToScreen({ longitude, latitude, height: 0 })!.x).toBeLessThan(
      middle.x,
    );
    session.release();
  });

  it('places a position underground, which is where this application looks', async () => {
    const session = acquire();
    const { longitude, latitude } = session.engine.getCamera();

    // Not a formality. Everything this view exists to show is below the surface, so a projection
    // that treated "below the ellipsoid" as unanswerable would make every cave label unplaceable
    // while every test of one still passed.
    expect(session.engine.positionToScreen({ longitude, latitude, height: -800 })).toBeDefined();
    session.release();
  });

  it('reports nothing for a position behind the camera', async () => {
    const session = acquire();
    const camera = session.engine.getCamera();

    expect(
      session.engine.positionToScreen({
        longitude: camera.longitude,
        latitude: camera.latitude,
        height: camera.height + 1000,
      }),
    ).toBeUndefined();
    session.release();
  });

  it('reports nothing for a position behind the camera without perspective either', async () => {
    const session = acquire();
    const camera = session.engine.getCamera();
    session.engine.setProjection('orthographic');

    // A box frustum places a point from where it is sideways and from nothing else, so the library
    // answers with an ordinary-looking pixel — usually the middle of the view — for something that
    // is behind the viewer and drawn nowhere at all. Under perspective the same question is
    // answered with nothing, and the rest of this application is written against that answer, so
    // the two projections have to agree: otherwise taking the perspective out of the view and
    // descending past a cave leaves its name pinned in the middle of an empty screen.
    expect(
      session.engine.positionToScreen({
        longitude: camera.longitude,
        latitude: camera.latitude,
        height: camera.height + 1000,
      }),
    ).toBeUndefined();

    // And it still answers for what is in front of the camera, which is the whole of the view.
    expect(
      session.engine.positionToScreen({
        longitude: camera.longitude,
        latitude: camera.latitude,
        height: 0,
      }),
    ).toBeDefined();
    session.release();
  });

  it('answers with a pixel off the surface, not with nothing, for a position out of view', async () => {
    const session = acquire();
    const camera = session.engine.getCamera();

    // The engine tests no bounds. That is why the contract says so and why chrome placing itself
    // from this has to check for itself; a test expecting undefined here would enshrine a promise
    // the library does not make.
    const far = session.engine.positionToScreen({
      longitude: camera.longitude + 20,
      latitude: camera.latitude,
      height: 0,
    });
    expect(far).toBeDefined();
    expect(far!.x).toBeGreaterThan(1200);
    session.release();
  });

  it('answers nothing rather than failing once the scene has been torn down', async () => {
    const session = acquire();
    const scene = session.engine;
    session.release();

    // Chrome pinned to the globe asks on every drawn frame and again on every resize, and a resize
    // arriving between the scene's teardown and the observer's is ordinary rather than exotic. A
    // destroyed widget has no scene at all, so without the guard this fails inside the library.
    expect(() => scene.positionToScreen({ longitude: 25, latitude: 45, height: 0 })).not.toThrow();
    expect(scene.positionToScreen({ longitude: 25, latitude: 45, height: 0 })).toBeUndefined();
  });

  it('converts a screen pixel to the ground under it', async () => {
    const session = acquire();
    engine.engineState.widgets[0].scene.pickedPosition = {
      longitudeDegrees: 25,
      latitudeDegrees: 45,
      height: 900,
    };

    expect(session.engine.screenToPosition({ x: 100, y: 200 })).toEqual({
      longitude: 25,
      latitude: 45,
      height: 900,
    });
    session.release();
  });

  it('reports nothing for a pixel pointing at the sky', async () => {
    const session = acquire();

    expect(session.engine.screenToPosition({ x: 100, y: 200 })).toBeUndefined();
    session.release();
  });
});

describe('render errors', () => {
  it('hands a lost graphics context to its subscribers, and stops on unsubscribe', async () => {
    const session = acquire();
    const seen: string[] = [];
    const unsubscribe = session.engine.subscribeRenderError((message) => seen.push(message));
    const { scene } = engine.engineState.widgets[0];

    // Raised exactly the way the engine raises it: the scene first, then the failure. A listener
    // that took only one argument would bind the scene and report "[object Object]" instead of
    // the diagnostic — which is the whole reason this path exists.
    scene.renderError.raise(scene, new Error('context lost'));
    expect(seen).toEqual(['context lost']);

    unsubscribe();
    scene.renderError.raise(scene, new Error('again'));
    expect(seen).toEqual(['context lost']);
    session.release();
  });

  it('stops listening to the engine once the scene is gone', async () => {
    const session = acquire();
    session.engine.subscribeRenderError(() => {});
    const { renderError } = engine.engineState.widgets[0].scene;

    session.release();

    expect(renderError.listeners.size).toBe(0);
  });

  it('draws a frame when asked, because it draws none on its own', async () => {
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    const before = scene.renderRequests;

    session.engine.requestRender();

    expect(scene.renderRequests).toBe(before + 1);
    session.release();
  });
});

describe('the box a loader should ask about', () => {
  it('is centred on the ground the middle of the screen is showing', async () => {
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    // The camera is at the default opening view; the ground under the screen centre is not.
    scene.pickedPosition = { longitudeDegrees: 22.7, latitudeDegrees: 46.5, height: 1100 };

    const [west, south, east, north] = session.engine.getVisibleBounds()!;

    expect((west + east) / 2).toBeCloseTo(22.7, 6);
    expect((south + north) / 2).toBeCloseTo(46.5, 6);
    session.release();
  });

  it('falls back to the ground beneath the camera when the screen centre is sky', async () => {
    const session = acquire();
    session.engine.setCamera({
      longitude: 25.3,
      latitude: 45.7,
      height: 4000,
      heading: 0,
      pitch: -90,
      roll: 0,
    });

    const [west, south, east, north] = session.engine.getVisibleBounds()!;

    expect((west + east) / 2).toBeCloseTo(25.3, 6);
    expect((south + north) / 2).toBeCloseTo(45.7, 6);
    session.release();
  });

  it('shrinks as the camera comes down, so a close view asks about a cave and not a county', async () => {
    const session = acquire();

    session.engine.flyToZoom(8, 45.7, 8);
    const wide = session.engine.getVisibleBounds()!;
    session.engine.flyToZoom(8, 45.7, 18);
    const close = session.engine.getVisibleBounds()!;

    expect(close[2] - close[0]).toBeLessThan((wide[2] - wide[0]) / 100);
    session.release();
  });
});

describe('being told the camera moved', () => {
  it('reports the camera coming to rest, which is when data is refetched', async () => {
    const session = acquire();
    const { camera } = engine.engineState.widgets[0].scene;
    let settled = 0;

    const unsubscribe = session.engine.onViewChanged(() => {
      settled += 1;
    });
    camera.moveEnd.raise();

    expect(settled).toBe(1);
    unsubscribe();
    camera.moveEnd.raise();
    expect(settled).toBe(1);
    session.release();
  });

  it('stops listening once the scene is gone', async () => {
    const session = acquire();
    session.engine.onViewChanged(() => {});
    const { camera } = engine.engineState.widgets[0].scene;

    session.release();

    expect(camera.moveEnd.listeners.size).toBe(0);
  });
});

describe('being told a frame is about to be drawn', () => {
  it('reports every drawn frame, and stops when the listener goes', async () => {
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    let frames = 0;

    const unsubscribe = session.engine.onBeforeRender(() => {
      frames += 1;
    });
    scene.render();
    scene.render();
    expect(frames).toBe(2);

    unsubscribe();
    scene.render();
    expect(frames).toBe(2);
    session.release();
  });

  it('costs nothing while nothing is being drawn', async () => {
    const session = acquire();
    let frames = 0;

    session.engine.onBeforeRender(() => {
      frames += 1;
    });

    // The whole reason chrome pinned to the globe hangs off this event rather than off an
    // animation-frame loop of its own: an idle scene draws nothing, so an idle scene costs the
    // chrome nothing. A loop would run at the display's refresh rate for ever and undo the one
    // property that makes this view affordable on a phone.
    expect(frames).toBe(0);
    session.release();
  });

  it('asks the scene for no extra frames of its own', async () => {
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    session.engine.onBeforeRender(() => {});
    const before = scene.renderRequests;

    scene.render();
    scene.render();

    // A listener that requested a redraw from inside a redraw would keep the scene drawing for
    // ever, which is the failure this whole arrangement exists to avoid and which looks identical
    // on screen to it working.
    expect(scene.renderRequests).toBe(before);
    session.release();
  });

  it('lets go of its listeners when the scene is torn down', async () => {
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    let frames = 0;
    session.engine.onBeforeRender(() => {
      frames += 1;
    });

    session.release();

    expect(scene.preRender.listeners.size).toBe(0);
    expect(frames).toBe(0);
  });
});

describe('vector sources', () => {
  function polylines(session: Scene3DSession) {
    return session.engine.createPolylineSource('centerlines');
  }

  it('draws lines straight between the positions it was given', async () => {
    const session = acquire();
    const source = polylines(session);
    const { primitives } = engine.engineState.widgets[0].scene;

    source.replace([
      {
        positions: [
          { longitude: 25.44, latitude: 45.53, height: 700 },
          { longitude: 25.441, latitude: 45.53, height: 690 },
        ],
        widthPixels: 2,
        color: '#7a1f1f',
        id: { kind: 'centerline' },
      },
    ]);

    const collection = primitives.items[0] as InstanceType<typeof engine.PolylineCollection>;
    expect(collection.polylines).toHaveLength(1);
    expect(collection.polylines[0].positions).toEqual([
      { longitudeDegrees: 25.44, latitudeDegrees: 45.53, height: 700 },
      { longitudeDegrees: 25.441, latitudeDegrees: 45.53, height: 690 },
    ]);
    expect(collection.polylines[0].width).toBe(2);
    session.release();
  });

  it('gives each line its own material, because clearing the batch destroys them', async () => {
    // A single material shared between lines is destroyed once per line when the batch is
    // cleared, and every line after the first fails on an object that is already gone.
    const session = acquire();
    const source = polylines(session);
    const { primitives } = engine.engineState.widgets[0].scene;
    const line = (id: string) => ({
      positions: [
        { longitude: 25, latitude: 45, height: 0 },
        { longitude: 25.1, latitude: 45, height: 0 },
      ],
      widthPixels: 2,
      color: '#7a1f1f',
      id,
    });

    source.replace([line('a'), line('b')]);

    const collection = primitives.items[0] as InstanceType<typeof engine.PolylineCollection>;
    expect(collection.polylines[0].material).not.toBe(collection.polylines[1].material);
    session.release();
  });

  it('keeps markers hittable whatever the ground in front of them is doing', async () => {
    const session = acquire();
    const source = session.engine.createMarkerSource('entrances');
    const { primitives } = engine.engineState.widgets[0].scene;

    source.replace([
      {
        position: { longitude: 25.44, latitude: 45.53, height: 0 },
        clampToGround: true,
        image: 'data:image/svg+xml;utf8,<svg/>',
        id: { kind: 'entrance' },
      },
    ]);

    const collection = primitives.items[0] as InstanceType<typeof engine.BillboardCollection>;
    // Without this a marker standing in a valley is drawn and cannot be clicked, which reads as
    // an unresponsive map rather than as a depth problem.
    expect(collection.billboards[0].disableDepthTestDistance).toBe(Number.POSITIVE_INFINITY);
    expect(collection.billboards[0].heightReference).toBe(engine.HeightReference.CLAMP_TO_GROUND);
    session.release();
  });

  it('asks for a frame on every change, because nothing else will', async () => {
    // This is the failure the whole arrangement exists to prevent: a batch added without a redraw
    // request appears only when the camera happens to move, which looks exactly like data that
    // never arrived.
    const session = acquire();
    const source = polylines(session);
    const { scene } = engine.engineState.widgets[0];
    const item = {
      positions: [
        { longitude: 25, latitude: 45, height: 0 },
        { longitude: 25.1, latitude: 45, height: 0 },
      ],
      widthPixels: 2,
      color: '#7a1f1f',
      id: 'a',
    };

    for (const change of [
      () => source.replace([item]),
      () => source.setOpacity(0.4),
      () => source.clear(),
      () => source.setVisible(false),
      () => source.remove(),
    ]) {
      const before = scene.renderRequests;
      change();
      expect(scene.renderRequests).toBe(before + 1);
    }
    session.release();
  });

  it('replaces the whole batch rather than merging into it', async () => {
    const session = acquire();
    const source = polylines(session);
    const { primitives } = engine.engineState.widgets[0].scene;
    const line = (id: string) => ({
      positions: [
        { longitude: 25, latitude: 45, height: 0 },
        { longitude: 25.1, latitude: 45, height: 0 },
      ],
      widthPixels: 2,
      color: '#7a1f1f',
      id,
    });

    source.replace([line('a'), line('b')]);
    source.replace([line('c')]);

    const collection = primitives.items[0] as InstanceType<typeof engine.PolylineCollection>;
    expect(collection.polylines.map((p) => p.id)).toEqual(['c']);
    session.release();
  });

  it('takes the batch out of the scene when it is removed, and ignores later use', async () => {
    const session = acquire();
    const source = polylines(session);
    const { primitives } = engine.engineState.widgets[0].scene;

    source.remove();

    expect(primitives.items).toHaveLength(0);
    // A handle that outlives its batch must go quiet rather than reach into a destroyed scene.
    expect(() => source.replace([])).not.toThrow();
    expect(() => source.remove()).not.toThrow();
    session.release();
  });

  it('replaces a batch built twice under one id, so nothing is left drawing unowned', async () => {
    const session = acquire();
    const { primitives } = engine.engineState.widgets[0].scene;

    polylines(session);
    polylines(session);

    expect(primitives.items).toHaveLength(1);
    session.release();
  });

  it('goes quiet once the scene it belongs to is destroyed', async () => {
    const session = acquire();
    const source = polylines(session);

    session.release();

    expect(() => source.replace([])).not.toThrow();
    expect(() => source.setVisible(true)).not.toThrow();
    expect(() => source.setOpacity(0.5)).not.toThrow();
  });

  it('fades lines by the transparency they asked for rather than flattening them to one value', async () => {
    const session = acquire();
    const source = polylines(session);
    const { primitives } = engine.engineState.widgets[0].scene;
    const line = (id: string) => ({
      positions: [
        { longitude: 25, latitude: 45, height: 0 },
        { longitude: 25.1, latitude: 45, height: 0 },
      ],
      widthPixels: 2,
      color: '#7a1f1f',
      id,
    });

    source.replace([line('a'), line('b')]);
    source.setOpacity(0.25);

    const collection = primitives.items[0] as InstanceType<typeof engine.PolylineCollection>;
    for (const drawn of collection.polylines) {
      expect((drawn.material.uniforms.color as InstanceType<typeof engine.Color>).alpha).toBe(0.25);
    }
    session.release();
  });

  it('keeps a fade when the data behind it is reloaded, because the viewer asked for it', async () => {
    // The camera settling refetches the whole batch. A fade that came back to full every time the
    // viewer panned would be a setting that only holds while nothing is happening.
    const session = acquire();
    const source = polylines(session);
    const { primitives } = engine.engineState.widgets[0].scene;
    const line = (id: string) => ({
      positions: [
        { longitude: 25, latitude: 45, height: 0 },
        { longitude: 25.1, latitude: 45, height: 0 },
      ],
      widthPixels: 2,
      color: '#7a1f1f',
      id,
    });

    source.setOpacity(0.5);
    source.replace([line('a')]);

    const collection = primitives.items[0] as InstanceType<typeof engine.PolylineCollection>;
    expect((collection.polylines[0].material.uniforms.color as InstanceType<typeof engine.Color>).alpha).toBe(0.5);
    session.release();
  });

  it('fades markers through their icon rather than through a second set of images', async () => {
    const session = acquire();
    const source = session.engine.createMarkerSource('entrances');
    const { primitives } = engine.engineState.widgets[0].scene;

    source.replace([
      {
        position: { longitude: 25.44, latitude: 45.53, height: 0 },
        clampToGround: true,
        image: 'data:image/svg+xml;utf8,<svg/>',
        id: { kind: 'entrance' },
      },
    ]);
    source.setOpacity(0.3);

    const collection = primitives.items[0] as InstanceType<typeof engine.BillboardCollection>;
    expect(collection.billboards[0].color.alpha).toBe(0.3);
    session.release();
  });
});

describe('lines that belong on the ground', () => {
  const scene = () => engine.engineState.widgets[0].scene;

  const onTheGround = (id: string, color = '#2f6f4f') => ({
    positions: [
      { longitude: 22.7, latitude: 46.5, height: 0 },
      { longitude: 22.71, latitude: 46.51, height: 0 },
    ],
    widthPixels: 3,
    color,
    clampToGround: true,
    id,
  });

  const inTheAir = (id: string) => ({
    positions: [
      { longitude: 22.7, latitude: 46.5, height: 640 },
      { longitude: 22.71, latitude: 46.51, height: 610 },
    ],
    widthPixels: 2,
    color: '#7a1f1f',
    id,
  });

  const flatBatch = () =>
    scene().primitives.items.find(
      (item): item is InstanceType<typeof engine.PolylineCollection> =>
        item instanceof engine.PolylineCollection,
    )!;

  const drapedBatches = () =>
    scene().groundPrimitives.items as InstanceType<typeof engine.GroundPolylinePrimitive>[];

  it('draws them in the ordinary batch while the globe has no relief on it', async () => {
    // The ellipsoid IS height zero, which is where these positions already are, so the ordinary
    // batch puts them in exactly the right place. This is the shipped state and it must stay
    // free: the draped shape additionally pulls a third of a megabyte of terrain reference data
    // into an installation that has no elevation model at all.
    const session = acquire();
    const source = session.engine.createPolylineSource('surface-feature-lines');

    source.replace([onTheGround('fault-1')]);

    expect(flatBatch().polylines).toHaveLength(1);
    expect(drapedBatches()).toHaveLength(0);
    session.release();
  });

  it('lays them on the ground once an elevation model is attached, with nothing replacing them', async () => {
    // Nothing put a new batch into the scene, and yet where these lines belong has just moved by
    // the whole height of the landscape. Left where they were they would be drawn a hillside
    // under the ground they describe, and under the markers of the same overlay.
    const session = acquire();
    const source = session.engine.createPolylineSource('surface-feature-lines');
    source.replace([onTheGround('fault-1'), inTheAir('survey-1')]);

    await session.engine.setTerrainSource({ url: '/terrain/' });

    expect(drapedBatches()).toHaveLength(1);
    expect(drapedBatches()[0].geometryInstances.map((instance) => instance.id)).toEqual(['fault-1']);
    // The line that has a height of its own is left exactly where it was.
    expect(flatBatch().polylines.map((line) => line.id)).toEqual(['survey-1']);
    session.release();
  });

  it('asks for a frame once the reference draping needs has arrived', async () => {
    // The engine starts that load from inside a frame, gives up on that frame, and asks for no
    // other when it lands. On a scene that draws only when it is asked to, the lines would then
    // sit invisible until the viewer next touched the camera — which looks exactly like data that
    // never loaded.
    const session = acquire();
    const source = session.engine.createPolylineSource('surface-feature-lines');
    source.replace([onTheGround('fault-1')]);
    await session.engine.setTerrainSource({ url: '/terrain/' });
    expect(engine.GroundPolylinePrimitive.terrainHeightRequests).toBeGreaterThan(0);
    const before = scene().renderRequests;

    engine.GroundPolylinePrimitive.deliverTerrainHeights();
    await Promise.resolve();
    await Promise.resolve();

    expect(scene().renderRequests).toBeGreaterThan(before);
    session.release();
  });

  it('draws them flat rather than not at all when that reference cannot be fetched', async () => {
    // Without it nothing can be draped, which is the same position a browser without the
    // extension is in and is answered the same way. Real data drawn a little out of place is a
    // great deal better than real data quietly not drawn.
    const session = acquire();
    const source = session.engine.createPolylineSource('surface-feature-lines');
    source.replace([onTheGround('fault-1')]);
    await session.engine.setTerrainSource({ url: '/terrain/' });
    expect(drapedBatches()).toHaveLength(1);

    engine.GroundPolylinePrimitive.deliverTerrainHeights(new Error('offline'));
    await Promise.resolve();
    await Promise.resolve();

    expect(drapedBatches()).toHaveLength(0);
    expect(flatBatch().polylines).toHaveLength(1);
    session.release();
  });

  it('brings them back to the ordinary batch when the elevation model is taken away', async () => {
    const session = acquire();
    const source = session.engine.createPolylineSource('surface-feature-lines');
    source.replace([onTheGround('fault-1')]);
    await session.engine.setTerrainSource({ url: '/terrain/' });

    await session.engine.setTerrainSource(undefined);

    expect(drapedBatches()).toHaveLength(0);
    expect(flatBatch().polylines).toHaveLength(1);
    session.release();
  });

  it('draws them flat where the browser cannot drape a line at all', async () => {
    // Draping needs a depth-texture extension a few drivers do not publish. Drawing the line at
    // the positions given is the bare-ellipsoid rendering, which is legible; dropping it would
    // take real data off the map over a capability the viewer has no say in.
    engine.engineState.depthTexture = false;
    const session = acquire();
    const source = session.engine.createPolylineSource('surface-feature-lines');
    source.replace([onTheGround('fault-1')]);

    await session.engine.setTerrainSource({ url: '/terrain/' });

    expect(drapedBatches()).toHaveLength(0);
    expect(flatBatch().polylines).toHaveLength(1);
    session.release();
  });

  it('groups them by colour, so a fade costs a number rather than a rebuild', async () => {
    const session = acquire();
    const source = session.engine.createPolylineSource('surface-feature-lines');
    source.replace([
      onTheGround('a', '#2f6f4f'),
      onTheGround('b', '#2f6f4f'),
      onTheGround('c', '#8c2f6b'),
    ]);
    await session.engine.setTerrainSource({ url: '/terrain/' });

    expect(drapedBatches()).toHaveLength(2);

    source.setOpacity(0.25);

    for (const batch of drapedBatches()) {
      const color = batch.appearance!.material!.uniforms.color as InstanceType<typeof engine.Color>;
      expect(color.alpha).toBe(0.25);
    }
    session.release();
  });

  it('hides and shows both halves of a batch together', async () => {
    const session = acquire();
    const source = session.engine.createPolylineSource('surface-feature-lines');
    source.replace([onTheGround('fault-1'), inTheAir('survey-1')]);
    await session.engine.setTerrainSource({ url: '/terrain/' });

    source.setVisible(false);

    expect(drapedBatches()[0].show).toBe(false);
    expect(flatBatch().show).toBe(false);
    session.release();
  });

  it('takes them out of the scene with the batch that owns them', async () => {
    const session = acquire();
    const source = session.engine.createPolylineSource('surface-feature-lines');
    source.replace([onTheGround('fault-1')]);
    await session.engine.setTerrainSource({ url: '/terrain/' });

    source.remove();

    expect(drapedBatches()).toHaveLength(0);
    // And a later change of ground does not resurrect a batch that is gone.
    await session.engine.setTerrainSource(undefined);
    expect(drapedBatches()).toHaveLength(0);
    session.release();
  });
});

describe('drawing without perspective', () => {
  it('starts with the ordinary projection and switches on request', async () => {
    const session = acquire();
    expect(session.engine.getProjection()).toBe('perspective');
    expect(session.engine.getOrthoHalfWidth()).toBeUndefined();

    session.engine.setProjection('orthographic');

    expect(session.engine.getProjection()).toBe('orthographic');
    expect(session.engine.getOrthoHalfWidth()).toBeGreaterThan(0);
    session.release();
  });

  it('adopts a width it is given, which is how a saved view is reopened as it was saved', async () => {
    const session = acquire();

    session.engine.setProjection('orthographic', 640);

    expect(session.engine.getOrthoHalfWidth()).toBe(640);
    session.release();
  });

  it('puts back the near plane a cave view needs when perspective returns', async () => {
    // Switching builds a fresh frustum carrying the library's own default, which clips the walls
    // away when the camera is inside a narrow passage — precisely where this view is most useful.
    const session = acquire();
    session.engine.setProjection('orthographic');

    session.engine.setProjection('perspective');

    const { frustum } = engine.engineState.widgets[0].scene.camera;
    expect(frustum).toBeInstanceOf(engine.PerspectiveFrustum);
    expect((frustum as InstanceType<typeof engine.PerspectiveFrustum>).near).toBe(0.5);
    session.release();
  });

  it('measures zoom from the width of the box on screen, not from the perspective arithmetic', async () => {
    // A box frustum has no cone that widens with distance, so what is on screen is its width and
    // nothing else. Left on the perspective arithmetic, a scene that had switched would report a
    // zoom it is not at and ask the server for a different patch of ground than the one drawn.
    const session = acquire();
    session.engine.flyToZoom(25.3, 45.7, 14);
    session.engine.setProjection('orthographic');
    const zoomAtWidth = session.engine.getPseudoZoom();
    // And the width is a reading off the camera rather than a setting of its own: the engine sizes
    // the box by how far the eye is from the ground it is looking at.
    expect(session.engine.getOrthoHalfWidth()).toBeCloseTo(session.engine.getCamera().height / 2, 6);

    const camera = session.engine.getCamera();
    session.engine.setCamera({ ...camera, height: camera.height * 2 });

    // Twice as far back is twice as much ground across the screen: exactly one zoom level out.
    expect(session.engine.getPseudoZoom()).toBeCloseTo(zoomAtWidth - 1, 6);
    session.release();
  });

  it('keeps a width it was given when the camera is next written to', async () => {
    // A saved plan view, reopened. The width is honoured by standing where it comes from, so the
    // next camera move — the flat map beside it reporting where it has been panned to, a preset
    // button, the depth clamp — carries it along instead of resizing the view from a distance.
    const session = acquire();
    session.engine.setProjection('orthographic', 640);
    expect(session.engine.getOrthoHalfWidth()).toBe(640);

    const camera = session.engine.getCamera();
    session.engine.setCamera({ ...camera, longitude: camera.longitude + 0.01 });

    expect(session.engine.getOrthoHalfWidth()).toBeCloseTo(640, 6);
    session.release();
  });

  it('resizes the view when it is flown to a zoom, not just moved', async () => {
    const session = acquire();
    session.engine.setProjection('orthographic');

    session.engine.flyToZoom(25.3, 45.7, 16);

    expect(session.engine.getPseudoZoom()).toBeCloseTo(16, 6);
    session.release();
  });

  it('lands an animated flight at the zoom it was asked for, which the flight cannot then undo', async () => {
    // The engine puts the camera through a view change on every frame of a flight, and each one
    // resizes the box from where the camera has got to. A width written down after the flight was
    // started therefore lives exactly one frame, so the view has to be framed by where it flies to.
    const session = acquire();
    session.engine.setProjection('orthographic');

    session.engine.flyToZoom(25.3, 45.7, 16, { animate: true });
    engine.engineState.widgets[0].scene.camera.finishFlight();

    expect(session.engine.getPseudoZoom()).toBeCloseTo(16, 6);
    session.release();
  });

  it('reopens a written-down plan view exactly as it was written down', async () => {
    // What a saved view is worth: the whole round trip through the neutral state, back into a
    // scene the viewer has since moved somewhere else entirely.
    const { applyCamera3D, readCamera3D } = await import('./camera3d.ts');
    const session = acquire();
    session.engine.flyToZoom(25.44, 45.53, 17);
    session.engine.setProjection('orthographic');
    const saved = readCamera3D(session.engine);
    expect(saved.projection).toBe('orthographic');
    expect(saved.orthoHalfWidth).toBeGreaterThan(0);

    session.engine.flyToZoom(22.1, 46.9, 9);

    applyCamera3D(session.engine, saved);

    const reopened = readCamera3D(session.engine);
    expect(reopened.eye.lon).toBeCloseTo(saved.eye.lon, 9);
    expect(reopened.eye.lat).toBeCloseTo(saved.eye.lat, 9);
    expect(reopened.eye.height).toBeCloseTo(saved.eye.height, 6);
    expect(reopened.orthoHalfWidth).toBeCloseTo(saved.orthoHalfWidth!, 6);
    session.release();
  });

  it('refuses to touch a scene that has been torn down', async () => {
    const session = acquire();
    session.release();

    expect(() => session.engine.setProjection('orthographic')).not.toThrow();
  });
});

describe('the camera below the ground', () => {
  /** Stands in for the scene drawing a frame, which is when the camera is looked at. */
  const drawFrame = () => engine.engineState.widgets[0].scene.render();

  const putCameraAt = (height: number) => {
    engine.engineState.widgets[0].scene.camera.positionWC = {
      longitudeDegrees: 25.3,
      latitudeDegrees: 45.7,
      height,
    };
  };

  it('lets the camera through the surface, because the cave is under it', async () => {
    acquire();

    expect(
      engine.engineState.widgets[0].scene.screenSpaceCameraController.enableCollisionDetection,
    ).toBe(false);
  });

  it('stops the descent at the floor, which nothing else does once it is let through', async () => {
    // With the engine's own collision detection off, both its "stop at the ground" behaviour and
    // its minimum zoom distance are skipped: a viewer who keeps zooming goes through the planet.
    const session = acquire();
    putCameraAt(-40000);

    drawFrame();

    expect(session.engine.getCamera().height).toBe(-2000);
    session.release();
  });

  it('leaves a camera above the floor exactly where the viewer put it', async () => {
    const session = acquire();
    putCameraAt(-500);

    drawFrame();

    expect(session.engine.getCamera().height).toBe(-500);
    session.release();
  });

  it('follows the data down but never fences a viewer out of a cave', async () => {
    const session = acquire();

    session.engine.setCameraFloorHeight(-5000);
    putCameraAt(-40000);
    drawFrame();
    expect(session.engine.getCamera().height).toBe(-5000);

    // A caller that under-reports how deep its data goes must not be able to raise the floor into
    // the caves that are already drawn.
    session.engine.setCameraFloorHeight(-10);
    putCameraAt(-1500);
    drawFrame();
    expect(session.engine.getCamera().height).toBe(-1500);
    session.release();
  });

  it('takes a descent limit handed back after the scene has gone, rather than throwing', async () => {
    // The scene is shared and reference counted, so a data loader can be told to let go after the
    // drawing surface it was reading has already been torn down — which is exactly the order a
    // page unmount produces. The engine no longer has a scene at that point, and a throw here
    // escapes into the unmount and takes the rest of the teardown with it.
    const session = acquire();
    // A floor different from the standing default, so the call cannot leave through the
    // no-change guard rather than through the one being tested.
    session.engine.setCameraFloorHeight(-4000);

    session.release();

    expect(() => session.engine.setCameraFloorHeight(-2000)).not.toThrow();
    expect(() => session.engine.requestRender()).not.toThrow();
  });

  it('puts rock under the surface rather than the basemap seen from its back', async () => {
    acquire();
    const { globe } = engine.engineState.widgets[0].scene;

    // The engine's default fades the underground colour out at close range, which is exactly the
    // range a cave is looked at from, leaving coastlines and lake outlines overhead.
    expect(globe.undergroundColor?.css).toBe('#3a332c');
    expect(globe.undergroundColorAlphaByDistance?.nearValue).toBe(1);
    // And the far end is rock too. The pair is measured from the eye to each patch of surface, not
    // from the camera to the region, so a value that fell away with distance would leave a viewer
    // underground looking at the basemap from behind — coastlines overhead — everywhere except
    // the nearest kilometre.
    expect(globe.undergroundColorAlphaByDistance?.farValue).toBe(1);
  });
});

describe('the ground over the cave', () => {
  const drawFrame = () => engine.engineState.widgets[0].scene.render();

  const footprint = () => ({
    ring: [
      { longitude: 25.3, latitude: 45.7, height: 0 },
      { longitude: 25.4, latitude: 45.7, height: 0 },
      { longitude: 25.4, latitude: 45.8, height: 0 },
      { longitude: 25.3, latitude: 45.8, height: 0 },
    ],
    floorHeight: -600,
  });

  /** Points the camera the way a viewer looking into a cave from above would have it. */
  const lookDownFromAbove = () => {
    const { camera } = engine.engineState.widgets[0].scene;
    camera.positionWC = { longitudeDegrees: 25.35, latitudeDegrees: 45.75, height: 3000 };
    camera.pitch = -Math.PI / 4;
  };

  it('starts by drawing the cave over the ground, which is legible from everywhere', async () => {
    const session = acquire();

    expect(session.engine.getSurfaceState().effective).toBe('overlay');
    expect(engine.engineState.widgets[0].scene.globe.depthTestAgainstTerrain).toBe(false);
    session.release();
  });

  it('cuts the ground away over the cave when that is asked for and the camera is over it', async () => {
    const session = acquire();
    lookDownFromAbove();
    session.engine.setCutawayFootprint(footprint());

    session.engine.setSurfaceMode('cutaway');

    const { globe } = engine.engineState.widgets[0].scene;
    expect(session.engine.getSurfaceState().effective).toBe('cutaway');
    expect(globe.clippingPolygons?.enabled).toBe(true);
    expect(globe.clippingPolygons?.length).toBe(1);
    // The ground in front of the cave has genuinely gone, so what is left is allowed to hide it.
    expect(globe.depthTestAgainstTerrain).toBe(true);
    session.release();
  });

  it('fills the opening rather than leaving the hole it cut', async () => {
    // Cutting ground away draws nothing in its place: without this the opening is a hard black
    // void, which reads as a broken renderer rather than as a hole in a hillside.
    const session = acquire();
    lookDownFromAbove();
    session.engine.setCutawayFootprint(footprint());
    session.engine.setSurfaceMode('cutaway');

    const { primitives } = engine.engineState.widgets[0].scene;
    const fill = primitives.items.find(
      (item): item is InstanceType<typeof engine.Primitive> =>
        item instanceof engine.Primitive,
    )!;
    expect(fill.show).toBe(true);
    // A wall around the opening and a floor under it.
    expect(fill.geometryInstances).toHaveLength(2);
    const wall = fill.geometryInstances[0].geometry as InstanceType<typeof engine.WallGeometry>;
    // The wall is a closed loop: as many points as the ring, plus the first one again.
    expect(wall.options.positions).toHaveLength(5);
    expect(wall.options.minimumHeights.every((height) => height === -600)).toBe(true);
    // Carried just past the ground so no hairline of background shows along the rim.
    expect(wall.options.maximumHeights.every((height) => height === 2)).toBe(true);
    // Nothing in the excavation is a thing a viewer can select, and it is built on the spot
    // rather than by a worker, so a scene that only draws when asked does not have to keep asking.
    expect(fill.allowPicking).toBe(false);
    expect(fill.asynchronous).toBe(false);
    session.release();
  });

  it('goes back to the overlay when the camera drops too near the horizon', async () => {
    // The opening is a vertical shaft: from a shallow angle a viewer sees its near wall and
    // under a third of the cave, whatever shape the outline is. Nothing fixes that but not
    // being there.
    const session = acquire();
    lookDownFromAbove();
    session.engine.setCutawayFootprint(footprint());
    session.engine.setSurfaceMode('cutaway');
    const reported: string[] = [];
    session.engine.onSurfaceStateChanged((state) => reported.push(state.effective));

    engine.engineState.widgets[0].scene.camera.pitch = (-5 * Math.PI) / 180;
    drawFrame();

    expect(session.engine.getSurfaceState()).toMatchObject({
      requested: 'cutaway',
      effective: 'overlay',
    });
    // Said out loud, so the chrome can explain a mode the viewer asked for and is not getting.
    expect(reported).toEqual(['overlay']);
    expect(engine.engineState.widgets[0].scene.globe.clippingPolygons?.enabled).toBe(false);
    session.release();
  });

  it('asks for a steeper view into a deep narrow shaft than into a broad shallow one', async () => {
    // The angle a viewer has to be at is a property of the excavation, not a constant. A survey a
    // few hundred metres across but half a kilometre deep is a shaft: from a third of the way up
    // it shows a lid of rock and none of the cave, while the overlay at the same angle shows all
    // of it — so the mode has to hand back long before the ground-level limit a broad opening uses.
    const session = acquire();
    const { camera } = engine.engineState.widgets[0].scene;
    camera.positionWC = { longitudeDegrees: 25.35, latitudeDegrees: 45.75, height: 3000 };
    camera.pitch = (-30 * Math.PI) / 180;

    // Kilometres across, six hundred metres deep: legible from thirty degrees.
    session.engine.setCutawayFootprint(footprint());
    session.engine.setSurfaceMode('cutaway');
    drawFrame();
    expect(session.engine.getSurfaceState().effective).toBe('cutaway');

    // A few hundred metres across, the same depth: not legible from thirty degrees.
    session.engine.setCutawayFootprint({
      ring: [
        { longitude: 25.35, latitude: 45.75, height: 0 },
        { longitude: 25.353, latitude: 45.75, height: 0 },
        { longitude: 25.353, latitude: 45.752, height: 0 },
        { longitude: 25.35, latitude: 45.752, height: 0 },
      ],
      floorHeight: -600,
    });
    drawFrame();
    expect(session.engine.getSurfaceState().effective).toBe('overlay');

    // Straight down it is legible again, and the mode comes back without being asked for twice.
    camera.pitch = (-80 * Math.PI) / 180;
    drawFrame();
    expect(session.engine.getSurfaceState()).toMatchObject({
      requested: 'cutaway',
      effective: 'cutaway',
    });
    session.release();
  });

  it('goes back to the overlay once the camera is under the ground itself', async () => {
    // From below there is no ground between the viewer and the cave to remove, and the opening
    // only adds a view of the underside of the surrounding terrain with the basemap on it.
    const session = acquire();
    lookDownFromAbove();
    session.engine.setCutawayFootprint(footprint());
    session.engine.setSurfaceMode('cutaway');

    engine.engineState.widgets[0].scene.camera.positionWC = {
      longitudeDegrees: 25.35,
      latitudeDegrees: 45.75,
      height: -200,
    };
    drawFrame();

    expect(session.engine.getSurfaceState().effective).toBe('overlay');
    session.release();
  });

  it('has nothing to cut when no survey has been drawn', async () => {
    const session = acquire();
    lookDownFromAbove();

    session.engine.setSurfaceMode('cutaway');

    expect(session.engine.getSurfaceState()).toMatchObject({
      requested: 'cutaway',
      effective: 'overlay',
      hasFootprint: false,
    });
    session.release();
  });

  it('takes the opening away with the survey it belonged to', async () => {
    const session = acquire();
    lookDownFromAbove();
    session.engine.setCutawayFootprint(footprint());
    session.engine.setSurfaceMode('cutaway');
    const { scene } = engine.engineState.widgets[0];
    const cut = scene.globe.clippingPolygons!;

    session.engine.setCutawayFootprint(undefined);

    // Taken off the globe rather than left on it holding nothing, and destroyed on the way out so
    // the textures a cut needs are not kept for a scene that is no longer cutting anything.
    expect(scene.globe.clippingPolygons).toBeUndefined();
    expect(cut.destroyed).toBe(true);
    expect(scene.primitives.items.some((item) => item instanceof engine.Primitive)).toBe(false);
    expect(session.engine.getSurfaceState().effective).toBe('overlay');
    session.release();
  });

  it('moves the opening to the survey the viewer moved to', async () => {
    // The outline handed over second has exactly as many points as the first, which is true of
    // every outline this application produces. An engine only rebuilds what it cuts with when
    // that count changes, so refilling one collection in place would leave the ground cut where
    // the first survey was — the second cave would sit under unbroken hillside, with the walls
    // and floor of its excavation drawn standing on top of the ground.
    const session = acquire();
    lookDownFromAbove();
    session.engine.setCutawayFootprint(footprint());
    session.engine.setSurfaceMode('cutaway');
    drawFrame();
    expect(
      engine.engineState.widgets[0].scene.globe.clippingPolygons?.packed?.[0].longitudeDegrees,
    ).toBeCloseTo(25.3, 6);

    const elsewhere = {
      ring: footprint().ring.map((position) => ({ ...position, longitude: position.longitude + 1 })),
      floorHeight: -600,
    };
    session.engine.setCutawayFootprint(elsewhere);
    drawFrame();

    const { globe } = engine.engineState.widgets[0].scene;
    expect(globe.clippingPolygons?.packed?.[0].longitudeDegrees).toBeCloseTo(26.3, 6);
    // Still cutting, and still cutting exactly one hole.
    expect(globe.clippingPolygons?.enabled).toBe(true);
    expect(globe.clippingPolygons?.length).toBe(1);
    session.release();
  });

  it('leaves the opening alone when the survey has not moved', async () => {
    // The outline is recomputed every time the camera comes to rest, and a viewer looking at one
    // cave gets an equal-but-new outline each time. Rebuilding for those would throw away and
    // re-upload the excavation and the textures behind the cut for no change at all.
    const session = acquire();
    lookDownFromAbove();
    session.engine.setCutawayFootprint(footprint());
    session.engine.setSurfaceMode('cutaway');
    const { scene } = engine.engineState.widgets[0];
    const cut = scene.globe.clippingPolygons;
    const fill = scene.primitives.items.find((item) => item instanceof engine.Primitive);

    session.engine.setCutawayFootprint(footprint());

    expect(scene.globe.clippingPolygons).toBe(cut);
    expect(cut?.destroyed).toBe(false);
    expect(scene.primitives.items.find((item) => item instanceof engine.Primitive)).toBe(fill);
    session.release();
  });

  it('tells a viewer under the ground something they can act on', async () => {
    // Two unrelated reasons a cutaway is not on screen, and the way out of one is not the way out
    // of the other: from below the surface no tilt in any direction brings the opening back.
    const session = acquire();
    lookDownFromAbove();
    session.engine.setCutawayFootprint(footprint());
    session.engine.setSurfaceMode('cutaway');
    const reported: (string | undefined)[] = [];
    session.engine.onSurfaceStateChanged((state) => reported.push(state.pausedBy));

    const { camera } = engine.engineState.widgets[0].scene;
    camera.pitch = (-5 * Math.PI) / 180;
    drawFrame();
    expect(session.engine.getSurfaceState().pausedBy).toBe('angle');

    // Under the ground and steeply pitched: nothing about the angle is wrong any more.
    camera.positionWC = { longitudeDegrees: 25.35, latitudeDegrees: 45.75, height: -200 };
    camera.pitch = (-80 * Math.PI) / 180;
    drawFrame();
    expect(session.engine.getSurfaceState().pausedBy).toBe('belowSurface');

    // A camera that is both under the ground and shallowly pitched has one thing to do about it.
    camera.pitch = (-5 * Math.PI) / 180;
    drawFrame();
    expect(session.engine.getSurfaceState().pausedBy).toBe('belowSurface');

    // Back over the cave, and the reason goes away with the pause.
    lookDownFromAbove();
    drawFrame();
    expect(session.engine.getSurfaceState()).toMatchObject({ effective: 'cutaway' });
    expect(session.engine.getSurfaceState().pausedBy).toBeUndefined();

    // Every one of those is a change the chrome has to be able to report.
    expect(reported).toEqual(['angle', 'belowSurface', undefined]);
    session.release();
  });

  it('refuses the cutaway on a browser that cannot draw one, and says so', async () => {
    // Reported rather than silently ignored: a control that quietly does nothing is worse than
    // one that says why it is unavailable.
    engine.engineState.webgl2 = false;
    const session = acquire();
    lookDownFromAbove();
    session.engine.setCutawayFootprint(footprint());

    session.engine.setSurfaceMode('cutaway');

    expect(session.engine.getSurfaceState()).toMatchObject({
      requested: 'cutaway',
      effective: 'overlay',
      cutawayAvailable: false,
    });
    // Nothing was cut and nothing was drawn into the ground either.
    expect(engine.engineState.widgets[0].scene.globe.clippingPolygons).toBeUndefined();
    expect(
      engine.engineState.widgets[0].scene.primitives.items.some(
        (item) => item instanceof engine.Primitive,
      ),
    ).toBe(false);
    session.release();
  });
});

describe('picking', () => {
  const clickAt = (x: number, y: number) => {
    engine.engineState.eventHandlers[0].raise('leftClick', {
      position: new engine.Cartesian2(x, y),
    });
  };

  it('hands back the payload the caller attached, untouched and by reference', async () => {
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    const payload = { kind: 'centerline', caveId: 'cave-1', centerlineId: 'line-1' };
    scene.pickResult = { id: payload };
    const picks: unknown[] = [];

    session.engine.onClick((pick) => picks.push(pick?.id));
    clickAt(10, 20);

    // Identity, not equality: the payload is the database key the caller is holding, and a copy
    // would force a lookup table alongside the geometry to get back to it.
    expect(picks[0]).toBe(payload);
    session.release();
  });

  it('reports the pixel the hit test was made at, which is where the pointer was', async () => {
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    scene.pickResult = { id: { kind: 'entrance' } };
    const screens: ({ x: number; y: number } | undefined)[] = [];

    session.engine.onClick((pick) => screens.push(pick?.screen));
    clickAt(10, 20);

    // A tooltip that follows the pointer cannot recover this from anything else it is given: the
    // item's own position projects to the middle of an icon the pointer is merely somewhere
    // within, and the hit test reaches several pixels further out again.
    expect(screens[0]).toEqual({ x: 10, y: 20 });
    session.release();
  });

  it('reaches further for a finger than for a cursor', async () => {
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: query.includes('coarse') }));
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    session.engine.onClick(() => {});

    clickAt(10, 20);

    // A finger lands nowhere near as precisely as a cursor, and a marker is a few pixels of ink.
    expect(scene.pickCalls[0]).toEqual({ x: 10, y: 20, width: 12, height: 12 });
    session.release();
  });

  it('uses the tighter cursor tolerance when the pointer is a mouse', async () => {
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    session.engine.onClick(() => {});

    clickAt(10, 20);

    expect(scene.pickCalls[0]).toEqual({ x: 10, y: 20, width: 6, height: 6 });
    session.release();
  });

  it('reports where a click landed on the ground when it hit nothing drawn', async () => {
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    scene.pickResult = undefined;
    scene.pickedPosition = { longitudeDegrees: 25, latitudeDegrees: 45, height: 900 };
    const picks: unknown[] = [];

    session.engine.onClick((pick) => picks.push(pick));
    clickAt(10, 20);

    // The hit test never reports the globe itself, so this is the only way to answer "did the
    // viewer click the ground?".
    expect(picks[0]).toEqual({
      id: undefined,
      position: { longitude: 25, latitude: 45, height: 900 },
      screen: { x: 10, y: 20 },
    });
    session.release();
  });

  it('reports nothing at all for a click on the sky', async () => {
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    scene.pickResult = undefined;
    scene.pickedPosition = undefined;
    const picks: unknown[] = [];

    session.engine.onClick((pick) => picks.push(pick));
    clickAt(10, 20);

    expect(picks).toEqual([null]);
    session.release();
  });

  it('stops calling a listener that unsubscribed', async () => {
    const session = acquire();
    let seen = 0;

    const unsubscribe = session.engine.onClick(() => {
      seen += 1;
    });
    clickAt(10, 20);
    unsubscribe();
    clickAt(10, 20);

    expect(seen).toBe(1);
    session.release();
  });

  it('hit tests hover at most once per drawn frame, however fast the pointer moves', async () => {
    const frames: FrameRequestCallback[] = [];
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      frames.push(callback);
      return frames.length;
    });
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    const hovers: unknown[] = [];
    session.engine.onHover((pick) => hovers.push(pick));

    const handler = engine.engineState.eventHandlers[0];
    for (let x = 0; x < 20; x += 1) {
      handler.raise('mouseMove', { endPosition: new engine.Cartesian2(x, 5) });
    }

    // An unthrottled hit test on every pointer move costs more than a frame's whole budget.
    expect(scene.pickCalls).toHaveLength(0);
    expect(frames).toHaveLength(1);

    frames[0](0);
    expect(scene.pickCalls).toHaveLength(1);
    // The newest position is the only one worth answering about.
    expect(scene.pickCalls[0].x).toBe(19);
    expect(hovers).toHaveLength(1);
    session.release();
  });

  it('says the pointer is over nothing once it has left the drawing surface', async () => {
    const frames: FrameRequestCallback[] = [];
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      frames.push(callback);
      return frames.length;
    });
    const session = acquire();
    const widget = engine.engineState.widgets[0];
    widget.scene.pickResult = { id: { kind: 'entrance', entranceId: 'e1', caveId: 'c1' } };
    const hovers: unknown[] = [];
    session.engine.onHover((pick) => hovers.push(pick));

    engine.engineState.eventHandlers[0].raise('mouseMove', {
      endPosition: new engine.Cartesian2(3, 4),
    });
    frames[0](0);
    expect(hovers).toHaveLength(1);
    expect(hovers[0]).not.toBeNull();

    // The engine's pointer listeners are all on the drawing surface, so once the pointer is off it
    // no further move ever arrives: without this the last thing hovered stays named on the screen,
    // and the pointer cursor stays with it, while the viewer is somewhere else entirely.
    widget.canvas.dispatchEvent(new Event('pointerleave'));

    expect(hovers).toHaveLength(2);
    expect(hovers[1]).toBeNull();
    session.release();
  });

  it('drops a hit test still waiting for a frame when the pointer leaves', async () => {
    const frames: FrameRequestCallback[] = [];
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      frames.push(callback);
      return frames.length;
    });
    const cancelled: number[] = [];
    vi.stubGlobal('cancelAnimationFrame', (handle: number) => cancelled.push(handle));
    const session = acquire();
    const widget = engine.engineState.widgets[0];
    widget.scene.pickResult = { id: { kind: 'entrance', entranceId: 'e1', caveId: 'c1' } };
    const hovers: unknown[] = [];
    session.engine.onHover((pick) => hovers.push(pick));

    // The move that leaves the surface is also a move, so a hit test is already queued for the
    // next frame when the pointer goes. Left to run, it would put the name straight back.
    engine.engineState.eventHandlers[0].raise('mouseMove', {
      endPosition: new engine.Cartesian2(3, 4),
    });
    widget.canvas.dispatchEvent(new Event('pointerleave'));

    expect(cancelled).toEqual([1]);
    expect(hovers).toEqual([null]);
    session.release();
  });

  it('takes its listener off the drawing surface when the scene goes', async () => {
    const session = acquire();
    const widget = engine.engineState.widgets[0];
    const hovers: unknown[] = [];
    session.engine.onHover((pick) => hovers.push(pick));

    session.release();
    widget.canvas.dispatchEvent(new Event('pointerleave'));

    // A listener left on an element that outlives the scene runs against a destroyed one.
    expect(hovers).toHaveLength(0);
  });

  it('does not read the depth buffer on hover, which is the expensive half of a click', async () => {
    const frames: FrameRequestCallback[] = [];
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      frames.push(callback);
      return frames.length;
    });
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    scene.pickResult = { id: { kind: 'entrance' } };
    scene.pickedPosition = { longitudeDegrees: 25, latitudeDegrees: 45, height: 900 };
    const hovers: { position?: unknown }[] = [];
    session.engine.onHover((pick) => hovers.push(pick!));

    engine.engineState.eventHandlers[0].raise('mouseMove', {
      endPosition: new engine.Cartesian2(3, 4),
    });
    frames[0](0);

    expect(hovers[0].position).toBeUndefined();
    session.release();
  });

  it('does not hit test hover on a touch device, where the answer cannot be shown', async () => {
    // A finger rests on nothing, and the only thing hover produces is a cursor. The engine
    // synthesises pointer moves from a one-finger drag, so without this guard every frame of every
    // pan on a phone would buy a hit test — a whole render pass of its own — for an answer the
    // device has no way to display.
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: query.includes('coarse') }));
    const frames: FrameRequestCallback[] = [];
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      frames.push(callback);
      return frames.length;
    });
    const session = acquire();
    const { scene } = engine.engineState.widgets[0];
    const hovers: unknown[] = [];
    session.engine.onHover((pick) => hovers.push(pick));

    const handler = engine.engineState.eventHandlers[0];
    for (let x = 0; x < 20; x += 1) {
      handler.raise('mouseMove', { endPosition: new engine.Cartesian2(x, 5) });
    }

    expect(frames).toHaveLength(0);
    expect(scene.pickCalls).toHaveLength(0);
    expect(hovers).toHaveLength(0);

    // Clicking is untouched: a finger still selects what it lands on.
    handler.raise('leftClick', { position: new engine.Cartesian2(10, 20) });
    expect(scene.pickCalls).toHaveLength(1);
    session.release();
  });

  it('does no hover work at all while nothing is listening for it', async () => {
    const frames: FrameRequestCallback[] = [];
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      frames.push(callback);
      return frames.length;
    });
    const session = acquire();
    session.engine.onClick(() => {});

    engine.engineState.eventHandlers[0].raise('mouseMove', {
      endPosition: new engine.Cartesian2(3, 4),
    });

    expect(frames).toHaveLength(0);
    session.release();
  });

  it('lets go of the pointer handler when the scene is destroyed', async () => {
    const session = acquire();
    session.engine.onClick(() => {});
    const handler = engine.engineState.eventHandlers[0];

    session.release();

    expect(handler.destroyed).toBe(true);
  });
});

describe("the ground's elevation", () => {
  const scene = () => engine.engineState.widgets[0].scene;

  /** A hillside: 1100 m along one edge of the outline and 1400 m along the other. */
  const apuseniRelief = (longitude: number) => (longitude < 25.35 ? 1100 : 1400);

  const footprint = () => ({
    ring: [
      { longitude: 25.3, latitude: 45.7, height: 0 },
      { longitude: 25.4, latitude: 45.7, height: 0 },
      { longitude: 25.4, latitude: 45.8, height: 0 },
      { longitude: 25.3, latitude: 45.8, height: 0 },
    ],
    floorHeight: -600,
  });

  const currentFill = () =>
    scene().primitives.items.filter(
      (item): item is InstanceType<typeof engine.Primitive> => item instanceof engine.Primitive,
    ).at(-1);

  const wallTops = () => {
    const wall = currentFill()!.geometryInstances[0].geometry as InstanceType<
      typeof engine.WallGeometry
    >;
    return wall.options.maximumHeights as number[];
  };

  it('draws the smooth ellipsoid until an installation says otherwise', async () => {
    const session = acquire();

    expect(session.engine.hasTerrain()).toBe(false);
    expect(scene().terrainProvider).toBeInstanceOf(engine.EllipsoidTerrainProvider);
    // Nothing was fetched, so a stock deployment needs no elevation server and no pre-baking.
    expect(engine.engineState.terrainRequests).toEqual([]);
    session.release();
  });

  it('reads a configured pyramid, asking for the normals that make relief visible', async () => {
    const session = acquire();

    await session.engine.setTerrainSource({ url: '/terrain/', attribution: '© Copernicus' });

    expect(engine.engineState.terrainRequests).toHaveLength(1);
    const request = engine.engineState.terrainRequests[0];
    expect(request.url).toBe('/terrain/');
    // Without these the hillside is drawn as an unlit wash of basemap with no relief in it at
    // all, which is most of what attaching an elevation model was for.
    expect(request.requestVertexNormals).toBe(true);
    // Shown on the scene rather than filed behind the engine's collapsed attribution control:
    // the licences of the freely available elevation models ask for visible credit.
    expect((request.credit as InstanceType<typeof engine.Credit>).showOnScreen).toBe(true);
    expect(session.engine.hasTerrain()).toBe(true);
    expect(scene().terrainProvider).toBeInstanceOf(engine.CesiumTerrainProvider);
    session.release();
  });

  it('asks for a frame, because changing the ground moves no camera', async () => {
    const session = acquire();
    const before = scene().renderRequests;

    await session.engine.setTerrainSource({ url: '/terrain/' });

    // The scene draws only when asked. Without this the globe keeps showing the ellipsoid until
    // the viewer happens to touch it.
    expect(scene().renderRequests).toBeGreaterThan(before);
    session.release();
  });

  it('reads a pyramid once, however often it is told to use the same one', async () => {
    // Attaching one discards every surface tile the globe is holding, so repeating it because a
    // view re-rendered would empty and refill the globe for nothing.
    const session = acquire();

    await session.engine.setTerrainSource({ url: '/terrain/' });
    await session.engine.setTerrainSource({ url: '/terrain/' });

    expect(engine.engineState.terrainRequests).toHaveLength(1);
    session.release();
  });

  it('goes back to the ellipsoid when the source is taken away', async () => {
    const session = acquire();
    await session.engine.setTerrainSource({ url: '/terrain/' });

    await session.engine.setTerrainSource(undefined);

    expect(session.engine.hasTerrain()).toBe(false);
    expect(scene().terrainProvider).toBeInstanceOf(engine.EllipsoidTerrainProvider);
    session.release();
  });

  it('drops a pyramid that was taken away while it was still being read', async () => {
    // Reading a pyramid is a network round trip, and for the whole of it the model in force and
    // the model wanted are two different things. On a FIRST attach nothing is in force yet, so a
    // request to go back to the smooth globe arriving in that window looks exactly like a request
    // for what is already there — and if it is treated as one, the read it was meant to call off
    // lands afterwards. The globe then draws a hillside nothing asked for while everything placed
    // against it was placed for a smooth one, which is the whole failure the placement exists to
    // prevent, and nothing ever notices: no further change is coming to put it right.
    const session = acquire();
    const pending = session.engine.setTerrainSource({ url: '/terrain/' });

    await session.engine.setTerrainSource(undefined);
    await pending;

    expect(session.engine.hasTerrain()).toBe(false);
    expect(scene().terrainProvider).toBeInstanceOf(engine.EllipsoidTerrainProvider);
    session.release();
  });

  it('reads it again after a retry of a read that failed', async () => {
    // A read that failed put nothing in force, so what it recorded has to go with it — otherwise
    // asking for the same address again is mistaken for asking for what is already there and the
    // retry quietly does nothing at all.
    const session = acquire();
    engine.engineState.terrainFailures.add('/terrain/');
    await expect(session.engine.setTerrainSource({ url: '/terrain/' })).rejects.toThrow();

    engine.engineState.terrainFailures.delete('/terrain/');
    await session.engine.setTerrainSource({ url: '/terrain/' });

    expect(session.engine.hasTerrain()).toBe(true);
    session.release();
  });

  it('reports a pyramid it could not read rather than leaving the globe blank', async () => {
    const session = acquire();
    engine.engineState.terrainFailures.add('/nowhere/');

    await expect(session.engine.setTerrainSource({ url: '/nowhere/' })).rejects.toThrow();

    // The caller turns this into something a person can read. What must not happen is the scene
    // believing it has ground: that draws a black void with no error anywhere.
    expect(session.engine.hasTerrain()).toBe(false);
    expect(scene().terrainProvider).toBeInstanceOf(engine.EllipsoidTerrainProvider);
    session.release();
  });

  it('ignores a pyramid that finished reading after the scene was torn down', async () => {
    const session = acquire();
    const pending = session.engine.setTerrainSource({ url: '/terrain/' });

    session.release();
    await pending;

    // Nothing to assert on the destroyed scene beyond its not having thrown: the point is that a
    // network answer arriving into a scene that no longer exists is a no-op rather than a
    // rejection nobody is holding.
    expect(session.engine.hasTerrain()).toBe(false);
  });

  it('answers how high the drawn ground is, point by point', async () => {
    const session = acquire();
    scene().globe.terrainHeightAt = apuseniRelief;

    expect(session.engine.groundHeight(25.3, 45.7)).toBe(1100);
    expect(session.engine.groundHeight(25.4, 45.7)).toBe(1400);
    session.release();
  });

  it('does not believe a ground height the earth does not have', async () => {
    // Just after the camera arrives somewhere the globe answers from a very coarse tile, whose
    // mesh is a flat chord across many degrees of a curved planet — tens of kilometres below the
    // surface it stands for. Attaching an elevation model makes that more frequent, not less.
    const session = acquire();
    scene().globe.terrainHeight = -35_966;

    expect(session.engine.groundHeight(25.3, 45.7)).toBe(0);
    session.release();
  });

  it('cuts the excavation into the hillside rather than into a flat disc', async () => {
    const session = acquire();
    scene().globe.terrainHeightAt = apuseniRelief;

    session.engine.setCutawayFootprint(footprint());

    // Two points of the outline stand on 1100 m ground and two on 1400 m, and the wall reaches
    // just past the ground at each of them: an excavation cut into real relief has a rim that
    // follows the hillside, not a flat lid at one height.
    // Five, not four: a wall is a path and the ring is an outline, so the first point closes it.
    expect(wallTops()).toEqual([1102, 1402, 1402, 1102, 1102]);
    session.release();
  });

  it('rebuilds the excavation when elevation data arrives under an outline that has not moved', async () => {
    // The defect this exists for: the walls are built from the ground of the moment, and the
    // moment they are built is the moment the surface is at its coarsest. The outline is compared
    // point by point and never changes for a cave the viewer is still looking at, so without this
    // the excavation stays a wall stopping hundreds of metres under its own hillside for the life
    // of the scene.
    const session = acquire();
    session.engine.setCutawayFootprint(footprint());
    expect(wallTops()).toEqual([2, 2, 2, 2, 2]);

    scene().globe.terrainHeightAt = apuseniRelief;
    scene().globe.tileLoadProgressEvent.raise(0);

    expect(wallTops()).toEqual([1102, 1402, 1402, 1102, 1102]);
    session.release();
  });

  it('leaves the excavation alone while elevation tiles are still arriving', async () => {
    const session = acquire();
    session.engine.setCutawayFootprint(footprint());
    const built = currentFill();

    scene().globe.terrainHeightAt = apuseniRelief;
    scene().globe.tileLoadProgressEvent.raise(7);

    // Rebuilding on every progress report would discard and re-upload the geometry dozens of
    // times while a region loads. Only a settled surface is worth rebuilding for.
    expect(currentFill()).toBe(built);
    session.release();
  });

  it('does not rebuild the excavation when the ground has not really moved', async () => {
    const session = acquire();
    scene().globe.terrainHeightAt = apuseniRelief;
    session.engine.setCutawayFootprint(footprint());
    const built = currentFill();

    scene().globe.tileLoadProgressEvent.raise(0);

    expect(currentFill()).toBe(built);
    session.release();
  });

  it('keeps a rebuilt excavation hidden while the scene is showing the overlay', async () => {
    // The cutaway is a mode the viewer chooses. A rebuild that put the shaft back on screen would
    // stand a brown pit on unbroken hillside in the middle of a view nobody asked to cut.
    const session = acquire();
    session.engine.setCutawayFootprint(footprint());

    scene().globe.terrainHeightAt = apuseniRelief;
    scene().globe.tileLoadProgressEvent.raise(0);

    expect(currentFill()!.show).toBe(false);
    session.release();
  });

  it('keeps a rebuilt excavation on screen while the scene is showing the cutaway', async () => {
    const session = acquire();
    const { camera } = scene();
    camera.positionWC = { longitudeDegrees: 25.35, latitudeDegrees: 45.75, height: 9000 };
    camera.pitch = -Math.PI / 2.2;
    session.engine.setCutawayFootprint(footprint());
    session.engine.setSurfaceMode('cutaway');
    expect(session.engine.getSurfaceState().effective).toBe('cutaway');

    scene().globe.terrainHeightAt = apuseniRelief;
    scene().globe.tileLoadProgressEvent.raise(0);

    expect(currentFill()!.show).toBe(true);
    session.release();
  });

  it('hands the cutaway back once the camera turns out to be inside the hillside', async () => {
    // A camera at 900 m over ground that was the ellipsoid a moment ago and is 1400 m now is
    // under the surface, where no angle recovers the opening — only climbing back above it.
    const session = acquire();
    const { camera } = scene();
    camera.positionWC = { longitudeDegrees: 25.4, latitudeDegrees: 45.75, height: 900 };
    camera.pitch = -Math.PI / 2.2;
    session.engine.setCutawayFootprint(footprint());
    session.engine.setSurfaceMode('cutaway');
    expect(session.engine.getSurfaceState().effective).toBe('cutaway');

    scene().globe.terrainHeightAt = apuseniRelief;
    scene().render();

    expect(session.engine.getSurfaceState().effective).toBe('overlay');
    expect(session.engine.getSurfaceState().pausedBy).toBe('belowSurface');
    session.release();
  });
});
