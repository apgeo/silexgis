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
  it('converts a position to a screen pixel, and reports nothing when it is off screen', async () => {
    const session = acquire();

    expect(session.engine.positionToScreen({ longitude: 10, latitude: 20, height: 0 })).toEqual({
      x: 10,
      y: 20,
    });
    expect(
      session.engine.positionToScreen({ longitude: 10, latitude: 20, height: -1 }),
    ).toBeUndefined();
    session.release();
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
