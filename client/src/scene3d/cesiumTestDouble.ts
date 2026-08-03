// SPDX-License-Identifier: AGPL-3.0-or-later

// A stand-in for the 3D engine library, used by the scene module's unit tests.
//
// The engine needs a real graphics context and the test runner has none, so the tests drive this
// instead. It is deliberately not a recording mock that returns undefined from everything: it
// models the handful of behaviours the tests are actually about — that positions round-trip
// through the camera, that a destroyed widget refuses further use the way the real one does, and
// that layers added to the scene can be read back. Anything the scene module does not touch is
// absent, so a new call site fails loudly here instead of silently passing.
//
// It sits beside the module it doubles rather than inside a test file because more than one suite
// needs it, and because test files are excluded from the module-boundary graph while this one is
// not — so this file must never import the real library, and it does not.

export interface FakeCartesian3 {
  longitudeDegrees: number;
  latitudeDegrees: number;
  height: number;
}

export interface FakeImageryProviderOptions {
  url: string;
  /** Undefined means "no credit at all"; the real library turns any string, '' included, into
   *  a credit object, so the distinction is behavioural rather than cosmetic. */
  credit: Credit | string | undefined;
  enablePickFeatures: boolean;
}

export interface FakeWidgetOptions {
  baseLayer?: unknown;
  showRenderLoopErrors?: boolean;
  requestRenderMode?: boolean;
  terrainProvider?: unknown;
  terrain?: unknown;
}

/** The vendor default the real library ships with, so a test can see it being cleared. */
export const VENDOR_DEFAULT_TOKEN = 'a-vendor-default-token';

export const Ion = { defaultAccessToken: VENDOR_DEFAULT_TOKEN };

/**
 * Everything a test wants to look at afterwards. Reset between tests.
 *
 * `Ion.defaultAccessToken` is deliberately not reset: the scene module clears it once, when it is
 * first imported, and putting the vendor default back between tests would erase the very thing
 * one of them is checking.
 */
export const engineState = {
  widgets: [] as CesiumWidget[],
  widgetOptions: [] as FakeWidgetOptions[],
  providers: [] as FakeImageryProviderOptions[],
  /** Pointer handlers the scene built, so a test can drive a click or a move through one. */
  eventHandlers: [] as ScreenSpaceEventHandler[],
  reset() {
    engineState.widgets = [];
    engineState.widgetOptions = [];
    engineState.providers = [];
    engineState.eventHandlers = [];
  },
};

export const CesiumMath = {
  toDegrees: (radians: number) => (radians * 180) / Math.PI,
  toRadians: (degrees: number) => (degrees * Math.PI) / 180,
};

export const HeightReference = {
  NONE: 'none',
  CLAMP_TO_GROUND: 'clampToGround',
} as const;

export const VerticalOrigin = {
  CENTER: 'center',
  BOTTOM: 'bottom',
  TOP: 'top',
} as const;

export const ScreenSpaceEventType = {
  LEFT_CLICK: 'leftClick',
  MOUSE_MOVE: 'mouseMove',
} as const;

export const Color = {
  /** The real parser normalises to floats; the string is enough to tell two colours apart here. */
  fromCssColorString(css: string) {
    return { css };
  },
};

export const Material = {
  ColorType: 'Color',
  fromType(type: string, uniforms: Record<string, unknown>) {
    return { type, uniforms, destroyed: false, destroy() { this.destroyed = true; } };
  },
};

// The library exports its own maths helpers under the name `Math`. Exported as an alias rather
// than declared under that name, so the global one stays reachable inside this file.
export { CesiumMath as Math };

export const Cartesian3 = {
  fromDegrees(longitudeDegrees: number, latitudeDegrees: number, height = 0): FakeCartesian3 {
    return { longitudeDegrees, latitudeDegrees, height };
  },
};

export class Cartesian2 {
  x: number;
  y: number;
  constructor(x: number, y: number) {
    this.x = x;
    this.y = y;
  }
}

export const Cartographic = {
  fromCartesian(position: FakeCartesian3 | undefined) {
    return position
      ? {
          longitude: CesiumMath.toRadians(position.longitudeDegrees),
          latitude: CesiumMath.toRadians(position.latitudeDegrees),
          height: position.height,
        }
      : undefined;
  },
  fromDegrees(longitudeDegrees: number, latitudeDegrees: number, height = 0): FakeCartesian3 {
    return { longitudeDegrees, latitudeDegrees, height };
  },
};

export const Ellipsoid = { WGS84: { name: 'WGS84' } };

export class Credit {
  html: string;
  showOnScreen: boolean;
  constructor(html: string, showOnScreen = false) {
    this.html = html;
    this.showOnScreen = showOnScreen;
  }
}

export const Rectangle = {
  fromDegrees(west: number, south: number, east: number, north: number) {
    return { west, south, east, north };
  },
};

export const SceneTransforms = {
  worldToWindowCoordinates(_scene: unknown, position: FakeCartesian3) {
    // Enough to tell "on screen" from "behind the camera" apart in a test.
    return position.height < 0
      ? undefined
      : new Cartesian2(position.longitudeDegrees, position.latitudeDegrees);
  },
};

export class PerspectiveFrustum {
  near = 1;
  fovy = Math.PI / 3;
}

export class UrlTemplateImageryProvider {
  options: FakeImageryProviderOptions;
  constructor(options: FakeImageryProviderOptions) {
    this.options = options;
    engineState.providers.push(options);
  }
}

class FakeImageryLayer {
  show = true;
  alpha = 1;
  provider: UrlTemplateImageryProvider;
  constructor(provider: UrlTemplateImageryProvider) {
    this.provider = provider;
  }
}

class FakeImageryLayerCollection {
  readonly layers: FakeImageryLayer[] = [];
  addImageryProvider(provider: UrlTemplateImageryProvider) {
    const layer = new FakeImageryLayer(provider);
    this.layers.push(layer);
    return layer;
  }
  remove(layer: FakeImageryLayer) {
    const index = this.layers.indexOf(layer);
    if (index >= 0) {
      this.layers.splice(index, 1);
    }
  }
}

// The real events forward every argument they were raised with, positionally, to each listener.
// That matters here because the render-error event is raised as `(scene, error)` and a double that
// passed only one argument would let a listener with the wrong arity keep passing its test.
class FakeEvent {
  readonly listeners = new Set<(...args: unknown[]) => void>();
  addEventListener(listener: (...args: unknown[]) => void) {
    this.listeners.add(listener);
  }
  removeEventListener(listener: (...args: unknown[]) => void) {
    this.listeners.delete(listener);
  }
  raise(...args: unknown[]) {
    for (const listener of [...this.listeners]) {
      listener(...args);
    }
  }
}

interface FakeViewOptions {
  destination: unknown;
  orientation?: { heading: number; pitch: number; roll: number };
}

class FakeCamera {
  frustum = new PerspectiveFrustum();
  positionWC: FakeCartesian3 = { longitudeDegrees: 0, latitudeDegrees: 0, height: 0 };
  heading = 0;
  pitch = 0;
  roll = 0;
  /** Destinations that were not point positions — how `fitBounds` becomes observable. */
  readonly framed: unknown[] = [];
  flightCount = 0;
  /** Raised once the camera has come to rest; a test raises it to stand in for navigating. */
  readonly moveEnd = new FakeEvent();

  setView(options: FakeViewOptions) {
    this.apply(options);
  }

  flyTo(options: FakeViewOptions) {
    this.flightCount += 1;
    this.apply(options);
  }

  pickEllipsoid(_windowPosition: Cartesian2, _ellipsoid: unknown): FakeCartesian3 | undefined {
    return undefined;
  }

  private apply(options: FakeViewOptions) {
    const destination = options.destination as Partial<FakeCartesian3>;
    if (typeof destination?.longitudeDegrees === 'number') {
      this.positionWC = destination as FakeCartesian3;
    } else {
      this.framed.push(options.destination);
    }
    if (options.orientation) {
      this.heading = options.orientation.heading;
      this.pitch = options.orientation.pitch;
      this.roll = options.orientation.roll;
    }
  }
}

/** A line as the collection stores it — whatever the scene module handed to `add`. */
export interface FakePolyline {
  positions: FakeCartesian3[];
  width: number;
  material: { type: string; uniforms: Record<string, unknown> };
  id: unknown;
}

export class PolylineCollection {
  readonly polylines: FakePolyline[] = [];
  show = true;
  /** Set when the scene takes the collection out of the primitives list. */
  destroyed = false;

  add(options: FakePolyline): FakePolyline {
    this.polylines.push(options);
    return options;
  }

  removeAll() {
    this.polylines.length = 0;
  }
}

/** A marker as the collection stores it. */
export interface FakeBillboard {
  position: FakeCartesian3;
  image: string;
  scale: number;
  heightReference: string;
  verticalOrigin: string;
  disableDepthTestDistance: number;
  id: unknown;
}

export class BillboardCollection {
  readonly billboards: FakeBillboard[] = [];
  readonly scene: unknown;
  show = true;
  destroyed = false;

  // The real collection needs the scene to resolve markers that sit on the terrain, so a test can
  // see that it was given one.
  constructor(options: { scene: unknown }) {
    this.scene = options.scene;
  }

  add(options: FakeBillboard): FakeBillboard {
    this.billboards.push(options);
    return options;
  }

  removeAll() {
    this.billboards.length = 0;
  }
}

class FakePrimitiveCollection {
  readonly items: unknown[] = [];

  add<T>(primitive: T): T {
    this.items.push(primitive);
    return primitive;
  }

  /** Removing a primitive destroys it, the way the real collection does. */
  remove(primitive: unknown): boolean {
    const index = this.items.indexOf(primitive);
    if (index < 0) {
      return false;
    }
    this.items.splice(index, 1);
    (primitive as { destroyed?: boolean }).destroyed = true;
    return true;
  }
}

/** A pointer handler: a registry of actions a test raises by hand, since jsdom has no canvas. */
export class ScreenSpaceEventHandler {
  readonly canvas: unknown;
  readonly actions = new Map<string, (event: unknown) => void>();
  destroyed = false;

  constructor(canvas: unknown) {
    this.canvas = canvas;
    engineState.eventHandlers.push(this);
  }

  setInputAction(action: (event: unknown) => void, type: string) {
    this.actions.set(type, action);
  }

  removeInputAction(type: string) {
    this.actions.delete(type);
  }

  /** How a test delivers a pointer event the way the real handler would. */
  raise(type: string, event: unknown) {
    this.actions.get(type)?.(event);
  }

  destroy() {
    this.destroyed = true;
    this.actions.clear();
  }
}

class FakeGlobe {
  /** Opposite of what the scene module sets, so the test proves the module set it. */
  depthTestAgainstTerrain = true;
  terrainHeight: number | undefined = 0;
  getHeight(_cartographic: unknown) {
    return this.terrainHeight;
  }
}

/** One hit test the scene module asked for, so a test can check the tolerance it used. */
export interface FakePickCall {
  x: number;
  y: number;
  width: number | undefined;
  height: number | undefined;
}

class FakeScene {
  readonly globe = new FakeGlobe();
  readonly camera = new FakeCamera();
  readonly imageryLayers = new FakeImageryLayerCollection();
  readonly primitives = new FakePrimitiveCollection();
  readonly renderError = new FakeEvent();
  renderRequests = 0;
  pickedPosition: FakeCartesian3 | undefined = undefined;
  /** What the next hit test answers with; the real one returns undefined when it finds nothing. */
  pickResult: { id?: unknown } | undefined = undefined;
  readonly pickCalls: FakePickCall[] = [];

  requestRender() {
    this.renderRequests += 1;
  }

  pickPosition(_windowPosition: Cartesian2) {
    return this.pickedPosition;
  }

  pick(windowPosition: Cartesian2, width?: number, height?: number) {
    this.pickCalls.push({ x: windowPosition.x, y: windowPosition.y, width, height });
    return this.pickResult;
  }
}

export class CesiumWidget {
  readonly scene = new FakeScene();
  readonly canvas = {
    clientWidth: 1200,
    width: 1200,
    clientHeight: 800,
    height: 800,
  } as unknown as HTMLCanvasElement;
  readonly container: Element;
  destroyCount = 0;
  private destroyed = false;

  constructor(container: Element, options: FakeWidgetOptions = {}) {
    this.container = container;
    engineState.widgets.push(this);
    engineState.widgetOptions.push(options);
  }

  isDestroyed() {
    return this.destroyed;
  }

  destroy() {
    // The real widget replaces every one of its methods with a thrower once destroyed. That is
    // the part of the behaviour a caller can trip over, so the double reproduces it.
    if (this.destroyed) {
      throw new Error('This object was destroyed, i.e., destroy() was called.');
    }
    this.destroyed = true;
    this.destroyCount += 1;
  }
}
