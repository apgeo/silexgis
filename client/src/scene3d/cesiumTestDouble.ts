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
  /** Zero, together with a stopped clock, is what makes an idle scene draw no frames at all. */
  maximumRenderTimeChange?: number;
  shouldAnimate?: boolean;
  /** `false` skips creating the star field, sun and moon rather than hiding them. */
  skyBox?: unknown;
  skyAtmosphere?: unknown;
  msaaSamples?: number;
  terrainProvider?: unknown;
  terrain?: unknown;
}

/** One `CesiumTerrainProvider.fromUrl` the scene made, with the options it passed. */
export interface FakeTerrainRequest {
  url: string;
  requestVertexNormals: boolean | undefined;
  credit: Credit | string | undefined;
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
  /**
   * What the graphics context a new scene gets will admit to. Cleared before building a scene, it
   * stands in for a browser that draws the view perfectly well but cannot cut a hole in the
   * ground — which is asked once, when the scene is built, and so cannot be arranged afterwards.
   */
  webgl2: true,
  /**
   * Whether the graphics context a new scene gets publishes the depth-texture extension a line
   * laid along the ground needs. Independent of the flag above because a browser really can have
   * one without the other, and the fallback for a missing one — the line drawn at the positions
   * given — has to be reachable in a test.
   */
  depthTexture: true,
  /**
   * Every elevation model the scene asked to read, in order, and how each one answered.
   *
   * A test arranges the answer before the scene asks: `terrainFailures` names URLs whose read
   * rejects, standing in for a pyramid that is not there or whose `layer.json` cannot be parsed.
   * The real read is a network round trip, which is why the calls are recorded rather than
   * awaited — a test that cares about ordering can see two of them in flight.
   */
  terrainRequests: [] as FakeTerrainRequest[],
  terrainFailures: new Set<string>(),
  /**
   * Every model file the scene asked to read, in order, with where it said the model goes.
   *
   * Unlike the elevation reads, none of these answers on its own: a test lands one with
   * `Model.deliver(url)` or refuses it with `Model.deliver(url, error)`. That is not ceremony —
   * loading a survey mesh takes long enough for the scene to be torn down or told to load
   * something else underneath it, and those are the cases worth having a test for.
   */
  modelRequests: [] as FakeModelRequest[],
  reset() {
    engineState.widgets = [];
    engineState.widgetOptions = [];
    engineState.providers = [];
    engineState.eventHandlers = [];
    engineState.webgl2 = true;
    engineState.depthTexture = true;
    GroundPolylinePrimitive.forget();
    engineState.terrainRequests = [];
    engineState.terrainFailures = new Set();
    engineState.modelRequests = [];
    Model.forget();
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

/**
 * The real parser normalises to floats; the string is enough to tell two colours apart here. The
 * alpha channel is modelled properly, though, because fading a layer is a behaviour under test:
 * the scene module multiplies a colour's own transparency by the opacity a viewer set.
 */
export class Color {
  css: string;
  alpha: number;

  constructor(css: string, alpha = 1) {
    this.css = css;
    this.alpha = alpha;
  }

  withAlpha(alpha: number) {
    return new Color(this.css, alpha);
  }

  static readonly WHITE = new Color('#ffffff', 1);

  static fromCssColorString(css: string) {
    return new Color(css, 1);
  }
}

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

/** Metres per degree of latitude, near enough for a stand-in that never leaves one country. */
const METERS_PER_DEGREE = 111320;

export const SceneTransforms = {
  /**
   * Where a position lands on the screen, modelled as a camera looking straight down.
   *
   * The three behaviours this exists to reproduce are the ones the contract warns callers about,
   * and each of them has caught something:
   *
   *   * the answer MOVES with the camera, so a test can show that chrome pinned to the globe
   *     follows the scene rather than sitting where it was first placed;
   *   * a point off the sides of the view projects to a pixel OUTSIDE the drawing surface rather
   *     than to nothing, because the real library tests no bounds and whatever places chrome from
   *     this has to do it itself;
   *   * the origin is the TOP LEFT — the real library flips the y axis as its last statement, and
   *     a stand-in that did not would let a tooltip ship upside down.
   *
   * Nothing undergroud is unprojectable here. That matters more than it sounds: the previous
   * stand-in answered "behind the camera" for anything below the ellipsoid, which is where this
   * application's whole subject matter lives, so every test of a cave label would have agreed that
   * cave labels cannot be drawn.
   *
   * What it does NOT model: tilt, roll, the curve of the globe, or a perspective camera's
   * convergence. Under a box frustum how much ground is on screen is the frustum's width and
   * nothing else, which is exact; under a perspective one the width is taken from the height, which
   * is exact only looking straight down. Anything that depends on tilt has to be tested against
   * the real engine.
   */
  worldToWindowCoordinates(scene: FakeScene | undefined, position: FakeCartesian3) {
    if (!scene) {
      return undefined;
    }
    const { camera } = scene;
    const eye = camera.positionWC;
    const orthographic = camera.frustum instanceof OrthographicFrustum;
    const widthPixels = 1200;
    const heightPixels = 800;

    if (!orthographic && position.height > eye.height) {
      // A nadir camera's "behind" is above it. A box frustum has no behind at all, which is why
      // the real library never answers undefined under one.
      return undefined;
    }

    const groundWidthMeters = orthographic
      ? (camera.frustum as OrthographicFrustum).width
      : 2 * Math.max(eye.height, 1) * Math.tan(Math.PI / 6);
    const metersPerPixel = groundWidthMeters / widthPixels;
    if (!(metersPerPixel > 0)) {
      return undefined;
    }

    const east =
      (position.longitudeDegrees - eye.longitudeDegrees) *
      METERS_PER_DEGREE *
      Math.cos((eye.latitudeDegrees * Math.PI) / 180);
    const north = (position.latitudeDegrees - eye.latitudeDegrees) * METERS_PER_DEGREE;
    const bearing = camera.heading; // radians, as the real camera reports it
    // Turning the compass one way turns the world under it the other.
    const screenRight = east * Math.cos(bearing) - north * Math.sin(bearing);
    const screenUp = north * Math.cos(bearing) + east * Math.sin(bearing);

    return new Cartesian2(
      widthPixels / 2 + screenRight / metersPerPixel,
      heightPixels / 2 - screenUp / metersPerPixel,
    );
  },
};

/** The only direction this stand-in's camera ever looks; see `FakeCamera.directionWC`. */
const STRAIGHT_DOWN = { straightDown: true } as const;

/**
 * The plane through a point, facing a direction — used to ask which side of the camera something
 * is on.
 *
 * Only the camera's own plane is ever built from this, and this stand-in's camera only ever looks
 * straight down, so the arithmetic reduces to comparing heights: a point below the camera is in
 * front of it, a point above it is behind. It refuses any other direction rather than answering
 * something plausible, because a caller that started asking about a tilted camera would need a
 * stand-in that models tilt and would otherwise get a confident wrong answer.
 */
export const Plane = {
  fromPointNormal(point: FakeCartesian3, normal: unknown) {
    if (normal !== STRAIGHT_DOWN) {
      throw new Error('this stand-in only models the plane of a camera looking straight down');
    }
    return { height: point.height };
  },
  getPointDistance(plane: { height: number }, point: FakeCartesian3) {
    return plane.height - point.height;
  },
};

export class PerspectiveFrustum {
  near = 1;
  fovy = Math.PI / 3;
}

/**
 * A frustum with no convergence: how much ground is on screen is its `width` and nothing else.
 *
 * Modelled because the scene module branches on which frustum the camera is holding — the zoom it
 * reports and the box it asks the server for are computed differently under each — and because the
 * real library replaces the frustum object outright when the projection is switched. A double that
 * merely flipped a flag would let a switch that never happened pass.
 */
export class OrthographicFrustum {
  near = 1;
  width = 0;
}

export class NearFarScalar {
  near: number;
  nearValue: number;
  far: number;
  farValue: number;

  constructor(near: number, nearValue: number, far: number, farValue: number) {
    this.near = near;
    this.nearValue = nearValue;
    this.far = far;
    this.farValue = farValue;
  }
}

// ---- cutting a hole in the ground -------------------------------------------
//
// The outline collection and the pieces the excavation inside it is built from. Only their shape
// is modelled: whether a hole is actually cut is a question for a graphics card, but which
// outline was handed over, whether the cut is switched on, and what the scene put inside it are
// all things this application decides and must therefore be checkable here.

export class ClippingPolygon {
  readonly positions: FakeCartesian3[];
  constructor(options: { positions: FakeCartesian3[] }) {
    this.positions = options.positions;
  }
}

export class ClippingPolygonCollection {
  readonly polygons: ClippingPolygon[] = [];
  enabled: boolean;
  inverse: boolean;
  destroyed = false;

  /**
   * The outline the cut is actually being made against — what the last `update` packed into the
   * textures the renderer reads, which is not necessarily the outline the collection is holding.
   */
  packed: FakeCartesian3[] | undefined = undefined;
  private packedPositionCount = 0;

  constructor(
    options: { polygons?: ClippingPolygon[]; enabled?: boolean; inverse?: boolean } = {},
  ) {
    this.enabled = options.enabled ?? true;
    this.inverse = options.inverse ?? false;
    for (const polygon of options.polygons ?? []) {
      this.add(polygon);
    }
  }

  get length() {
    return this.polygons.length;
  }

  add(polygon: ClippingPolygon) {
    this.polygons.push(polygon);
    return polygon;
  }

  removeAll() {
    this.polygons.length = 0;
  }

  /**
   * Repacks the outlines for the renderer, on every drawn frame the cut is switched on for.
   *
   * The guard is the real one, and it is the whole reason this method is modelled at all: judging
   * whether an outline has moved would mean comparing every point of it every frame, so the real
   * collection compares the TOTAL number of points it is holding instead and does nothing when
   * that has not changed. Emptying a collection and refilling it with a different outline of the
   * same length is therefore silently ignored — the cut stays where the first outline put it —
   * and neither emptying nor refilling resets the count.
   */
  update() {
    const total = this.polygons.reduce((sum, polygon) => sum + polygon.positions.length, 0);
    if (total === this.packedPositionCount) {
      return;
    }
    this.packedPositionCount = total;
    this.packed = this.polygons[0]?.positions;
  }

  destroy() {
    this.destroyed = true;
    return undefined;
  }

  /**
   * The real check asks the graphics context for floating-point texture support, which is a
   * WebGL 2 feature — so it reads exactly this flag off the scene, and a test can clear it to
   * stand in for a browser that cannot cut a hole in the ground.
   */
  static isSupported(scene: { context?: { webgl2?: boolean } } | undefined) {
    return scene?.context?.webgl2 === true;
  }
}

export class PolygonHierarchy {
  positions: FakeCartesian3[];
  constructor(positions: FakeCartesian3[]) {
    this.positions = positions;
  }
}

export interface FakePolygonOptions {
  polygonHierarchy: PolygonHierarchy;
  height: number;
  vertexFormat: unknown;
}

export class PolygonGeometry {
  options: FakePolygonOptions;
  constructor(options: FakePolygonOptions) {
    this.options = options;
  }
}

export interface FakeWallOptions {
  positions: FakeCartesian3[];
  maximumHeights: number[];
  minimumHeights: number[];
  vertexFormat: unknown;
}

export class WallGeometry {
  options: FakeWallOptions;
  constructor(options: FakeWallOptions) {
    this.options = options;
  }
}

export class GeometryInstance {
  geometry: unknown;
  attributes: { color: unknown } | undefined;
  id: unknown;
  constructor(options: { geometry: unknown; attributes?: { color: unknown }; id?: unknown }) {
    this.geometry = options.geometry;
    this.attributes = options.attributes;
    this.id = options.id;
  }
}

export const ColorGeometryInstanceAttribute = {
  fromColor(color: Color) {
    return { color };
  },
};

export class PerInstanceColorAppearance {
  readonly flat: boolean;
  readonly translucent: boolean;
  readonly closed: boolean;
  static readonly FLAT_VERTEX_FORMAT = { position: true };

  constructor(options: { flat?: boolean; translucent?: boolean; closed?: boolean } = {}) {
    this.flat = options.flat ?? false;
    this.translucent = options.translucent ?? true;
    this.closed = options.closed ?? false;
  }
}

export class Primitive {
  readonly geometryInstances: GeometryInstance[];
  readonly appearance: PerInstanceColorAppearance | undefined;
  readonly asynchronous: boolean;
  readonly allowPicking: boolean;
  show: boolean;
  destroyed = false;

  constructor(options: {
    geometryInstances: GeometryInstance[];
    appearance?: PerInstanceColorAppearance;
    asynchronous?: boolean;
    allowPicking?: boolean;
    show?: boolean;
  }) {
    this.geometryInstances = options.geometryInstances;
    this.appearance = options.appearance;
    this.asynchronous = options.asynchronous ?? true;
    this.allowPicking = options.allowPicking ?? true;
    this.show = options.show ?? true;
  }
}

// Lines laid along the ground. The real library draws these with a different shape from an
// ordinary line — a volume projected onto the terrain by the graphics card — so the two cannot
// share a batch, and which of them a line ended up in is exactly what a test about placement has
// to be able to see.

/** A line whose heights are ignored because it follows whatever the ground turns out to be. */
export class GroundPolylineGeometry {
  readonly positions: FakeCartesian3[];
  readonly width: number;
  constructor(options: { positions: FakeCartesian3[]; width?: number }) {
    this.positions = options.positions;
    this.width = options.width ?? 1;
  }
}

export class PolylineMaterialAppearance {
  readonly material: { type: string; uniforms: Record<string, unknown> } | undefined;
  constructor(
    options: { material?: { type: string; uniforms: Record<string, unknown> } } = {},
  ) {
    this.material = options.material;
  }
}

export class GroundPolylinePrimitive {
  readonly geometryInstances: GeometryInstance[];
  readonly appearance: PolylineMaterialAppearance | undefined;
  show: boolean;
  destroyed = false;

  constructor(
    options: {
      geometryInstances?: GeometryInstance[];
      appearance?: PolylineMaterialAppearance;
      show?: boolean;
    } = {},
  ) {
    this.geometryInstances = options.geometryInstances ?? [];
    this.appearance = options.appearance;
    this.show = options.show ?? true;
  }

  /**
   * The real check asks the graphics context for the depth-texture extension. It reads the scene's
   * own flag here for the same reason the hole-cutting check does: it is asked once, when the
   * scene is built, so a test cannot arrange it any later than that.
   */
  static isSupported(scene: { context?: { depthTexture?: boolean } } | undefined) {
    return scene?.context?.depthTexture === true;
  }

  /**
   * Loads the coarse worldwide height reference draping is projected against.
   *
   * The real one fetches a third of a megabyte over the network, so it lands well after the frame
   * that asked for it — and the scene only draws when it is asked to. That timing is the whole
   * behaviour worth modelling, so the promise stays pending until a test lets it land.
   */
  static terrainHeightRequests = 0;
  private static terrainHeightsArrived: ((error?: unknown) => void) | undefined;

  static initializeTerrainHeights() {
    GroundPolylinePrimitive.terrainHeightRequests += 1;
    return new Promise<void>((resolve, reject) => {
      GroundPolylinePrimitive.terrainHeightsArrived = (error) =>
        error === undefined ? resolve() : reject(error);
    });
  }

  /** Lets the load above finish, standing in for the reference file arriving — or not. */
  static deliverTerrainHeights(error?: unknown) {
    GroundPolylinePrimitive.terrainHeightsArrived?.(error);
    GroundPolylinePrimitive.terrainHeightsArrived = undefined;
  }

  static forget() {
    GroundPolylinePrimitive.terrainHeightRequests = 0;
    GroundPolylinePrimitive.terrainHeightsArrived = undefined;
  }
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
  frustum: PerspectiveFrustum | OrthographicFrustum = new PerspectiveFrustum();
  positionWC: FakeCartesian3 = { longitudeDegrees: 0, latitudeDegrees: 0, height: 0 };
  /**
   * Which way the camera looks, which in this stand-in is always straight down.
   *
   * It is the same simplification the projection below makes — nothing here models tilt — and it
   * is enough for the one thing it is used for: deciding whether a position is in front of the
   * camera or behind it. Looking down, "in front" is "lower than the camera", which is exactly the
   * distinction that decides whether a label about a cave entrance can be drawn at all once the
   * viewer has descended past it.
   */
  readonly directionWC = STRAIGHT_DOWN;
  heading = 0;
  pitch = 0;
  private rollRadians = 0;
  /** Where the ground is, so the distance a box frustum is sized from can be worked out. */
  private readonly groundHeight: () => number;

  constructor(groundHeight: () => number = () => 0) {
    this.groundHeight = groundHeight;
  }

  /**
   * The roll the real library reports, quirk included.
   *
   * It does not hand back the roll it was given: it measures the angle from the camera's own axes
   * and folds the answer into a whole turn, so a camera placed level comes back as a *whole turn*
   * rather than as nothing — at every tilt except straight down, where the frame it measures
   * against degenerates and the answer really is nothing. Modelled here because reading that value
   * raw says a level camera is upside down, and nothing but a stand-in that reproduces it can
   * catch what follows from that without a graphics card.
   */
  get roll(): number {
    if (this.rollRadians !== 0) {
      return this.rollRadians;
    }
    return this.pitch <= -Math.PI / 2 + 1e-9 ? 0 : Math.PI * 2;
  }

  set roll(value: number) {
    this.rollRadians = value;
  }

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

  /**
   * Stands in for the last tick of a flight, which a test raises by hand.
   *
   * The real library animates a flight by putting the camera through `setView` on every frame of
   * it, so anything a caller does to the frustum after starting the flight — rather than to the
   * camera the flight is heading for — is undone by the next frame. The destination is applied
   * here at once, as it is by `flyTo` above, so the only thing this changes is the box frustum's
   * width, which is exactly the behaviour it exists to expose.
   */
  finishFlight() {
    this.adjustOrthographicFrustum();
  }

  /**
   * Swaps in a box frustum, sized from how far the camera is from the ground, which is what the
   * real library does — the switch is meant to keep roughly the framing the viewer already had
   * rather than jumping to some default.
   */
  switchToOrthographicFrustum() {
    if (this.frustum instanceof OrthographicFrustum) {
      return;
    }
    this.frustum = new OrthographicFrustum();
    this.adjustOrthographicFrustum();
  }

  switchToPerspectiveFrustum() {
    if (this.frustum instanceof PerspectiveFrustum) {
      return;
    }
    // A fresh object carrying the library's defaults, so a scene that does not put its own near
    // plane back is caught rather than quietly inheriting the one it set before the switch.
    this.frustum = new PerspectiveFrustum();
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
    this.adjustOrthographicFrustum();
  }

  /**
   * Resizes a box frustum from where the camera now stands, which the real library does on every
   * camera move — placing it, flying it, dragging it, zooming it.
   *
   * Modelled because it is the difference between a width being a property a caller can set and a
   * width being a reading off the camera's position, and the whole of how this application frames
   * a view without perspective turns on which of those it is. A stand-in that quietly let an
   * assigned width persist would certify a behaviour the library does not have.
   */
  private adjustOrthographicFrustum() {
    if (!(this.frustum instanceof OrthographicFrustum)) {
      return;
    }
    this.frustum.width = this.distanceToGround();
  }

  /**
   * Distance from the eye to the ground under the middle of the screen — the real measure the
   * library sizes a box frustum by. With the middle of the screen showing sky (or the camera under
   * the ground) there is nothing to measure to, and the library falls back to the height above the
   * ellipsoid, so this does the same.
   */
  private distanceToGround(): number {
    const above = this.positionWC.height - this.groundHeight();
    const sinPitch = Math.abs(Math.sin(this.pitch));
    if (above <= 0 || sinPitch < 1e-9) {
      return Math.max(this.positionWC.height, 0);
    }
    return above / sinPitch;
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
  /** Multiplies the icon; its alpha is how a marker batch is faded. */
  color: Color;
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

/**
 * A placement frame, as the double models it: the point it was built at.
 *
 * The real thing is a 4×4 matrix carrying a rotation as well, and it is not reproduced here — the
 * rotation the scene asks for is always the local east-north-up one, so there is nothing about it
 * for a test to distinguish. Where a model was put is the whole of what is under test, and that is
 * exactly what this keeps.
 */
export interface FakeMatrix4 {
  origin: FakeCartesian3;
}

export const Transforms = {
  eastNorthUpToFixedFrame(origin: FakeCartesian3): FakeMatrix4 {
    return { origin };
  },
};

/** One `Model.fromGltfAsync` the scene made, with the options it passed. */
export interface FakeModelRequest {
  url: string;
  modelMatrix: FakeMatrix4;
  allowPicking: boolean | undefined;
}

/**
 * A loaded model.
 *
 * Reading one is a network round trip over a file that can be tens of megabytes, so the double
 * keeps every load pending until a test lands it by hand: the states worth testing — asked for but
 * not here, here, and refused — only exist while that is true. Landing is by URL because a scene
 * can legitimately have two reads in the air at once.
 */
export class Model {
  readonly url: string;
  readonly allowPicking: boolean | undefined;
  modelMatrix: FakeMatrix4;
  show = true;
  /** Set when the scene takes the model out of the primitives list, or destroys it directly. */
  destroyed = false;

  // Public only so a test can name the instance type; the scene reaches one solely through
  // `fromGltfAsync`, which is the whole of how the real library hands a model over.
  constructor(request: FakeModelRequest) {
    this.url = request.url;
    this.modelMatrix = request.modelMatrix;
    this.allowPicking = request.allowPicking;
  }

  private static waiting = new Map<
    string,
    { request: FakeModelRequest; resolve: (model: Model) => void; reject: (error: unknown) => void }
  >();

  static fromGltfAsync(options: {
    url: string;
    modelMatrix: FakeMatrix4;
    allowPicking?: boolean;
  }): Promise<Model> {
    const request: FakeModelRequest = {
      url: options.url,
      modelMatrix: options.modelMatrix,
      allowPicking: options.allowPicking,
    };
    engineState.modelRequests.push(request);
    return new Promise<Model>((resolve, reject) => {
      Model.waiting.set(options.url, { request, resolve, reject });
    });
  }

  /** Lands a read the scene is waiting on; with an error, refuses it instead. */
  static deliver(url: string, error?: unknown) {
    const pending = Model.waiting.get(url);
    if (!pending) {
      throw new Error(`no model read is waiting for ${url}`);
    }
    Model.waiting.delete(url);
    if (error === undefined) {
      pending.resolve(new Model(pending.request));
    } else {
      pending.reject(error);
    }
  }

  /** Whether a read for this URL is still in the air. */
  static isWaitingFor(url: string): boolean {
    return Model.waiting.has(url);
  }

  static forget() {
    Model.waiting.clear();
  }

  destroy() {
    this.destroyed = true;
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

/**
 * The globe's default surface: the smooth reference ellipsoid, which needs no server and answers
 * zero everywhere. What a scene with no elevation model configured draws.
 */
export class EllipsoidTerrainProvider {
  readonly kind = 'ellipsoid';
}

/**
 * A read of a pre-baked tile pyramid. Only the static factory is modelled, because that is the
 * whole of the surface the scene uses — and it is asynchronous in the real library, which is the
 * behaviour that matters here: a scene can be torn down, or pointed at a different pyramid, while
 * one of these is still in the air.
 */
export class CesiumTerrainProvider {
  readonly url: string;

  private constructor(url: string) {
    this.url = url;
  }

  static async fromUrl(
    url: string,
    options?: { requestVertexNormals?: boolean; credit?: Credit | string },
  ): Promise<CesiumTerrainProvider> {
    engineState.terrainRequests.push({
      url,
      requestVertexNormals: options?.requestVertexNormals,
      credit: options?.credit,
    });
    // A microtask rather than a timer: it is enough to make every caller go through the
    // asynchronous path, and it keeps tests free of fake clocks.
    await Promise.resolve();
    if (engineState.terrainFailures.has(url)) {
      throw new Error(`no terrain at ${url}`);
    }
    return new CesiumTerrainProvider(url);
  }
}

class FakeGlobe {
  /** Opposite of what the scene module sets, so the test proves the module set it. */
  depthTestAgainstTerrain = true;
  terrainHeight: number | undefined = 0;
  /**
   * Ground that varies from place to place, which is the whole difference an elevation model
   * makes and the one thing a single number cannot stand in for. Set it and `terrainHeight` is
   * ignored; leave it and every point answers the same, as the bare ellipsoid does.
   */
  terrainHeightAt: ((longitudeDegrees: number, latitudeDegrees: number) => number) | undefined =
    undefined;
  /**
   * Raised with the number of surface tiles still being fetched. The real globe raises it as
   * tiles arrive, and zero is the surface having settled — which is the only moment anything
   * built from ground heights can find out that the ground has changed under it.
   */
  readonly tileLoadProgressEvent = new FakeEvent();
  /** The engine's own defaults, so a test can see the scene replace them. */
  undergroundColor: Color | undefined = new Color('#000000', 1);
  undergroundColorAlphaByDistance: NearFarScalar | undefined = undefined;

  private outlines: ClippingPolygonCollection | undefined = undefined;

  get clippingPolygons() {
    return this.outlines;
  }

  /** The globe takes ownership: whatever it was holding is destroyed, as the real one does. */
  set clippingPolygons(value: ClippingPolygonCollection | undefined) {
    if (value === this.outlines) {
      return;
    }
    this.outlines?.destroy();
    this.outlines = value;
  }

  getHeight(cartographic: { longitudeDegrees: number; latitudeDegrees: number } | undefined) {
    if (this.terrainHeightAt && cartographic) {
      return this.terrainHeightAt(cartographic.longitudeDegrees, cartographic.latitudeDegrees);
    }
    return this.terrainHeight;
  }
}

/**
 * The engine's navigation controller. Only the two settings the scene changes are here — and
 * both start at the engine's own value, so a test that reads them back is reading a decision this
 * application made rather than a default it happened to agree with.
 */
class FakeScreenSpaceCameraController {
  enableCollisionDetection = true;
  minimumZoomDistance = 1;
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
  // The camera is given the ground rather than the scene: sizing a box frustum is the one thing it
  // does that depends on anything outside itself.
  readonly camera = new FakeCamera(() => this.globe.terrainHeight ?? 0);
  readonly imageryLayers = new FakeImageryLayerCollection();
  readonly primitives = new FakePrimitiveCollection();
  /** Where anything drawn against the ground rather than in the air goes; a separate list. */
  readonly groundPrimitives = new FakePrimitiveCollection();
  readonly renderError = new FakeEvent();
  readonly screenSpaceCameraController = new FakeScreenSpaceCameraController();
  /** Raised before each drawn frame; a test raises it to stand in for the scene drawing one. */
  readonly preRender = new FakeEvent();
  /** What the graphics context admits to, fixed when the scene is built, as the real one is. */
  readonly context = { webgl2: engineState.webgl2, depthTexture: engineState.depthTexture };
  /**
   * The full-screen passes. Only edge smoothing is modelled, and only because the engine ships it
   * switched off while this scene switches it on — a default the tests have to be able to see
   * being changed rather than merely restated.
   */
  readonly postProcessStages = { fxaa: { enabled: false } };
  /**
   * What the ground is drawn from. The real scene starts on the bare ellipsoid and hands whatever
   * is assigned here down to its globe, so this is where a test sees an elevation model arrive.
   */
  terrainProvider: EllipsoidTerrainProvider | CesiumTerrainProvider = new EllipsoidTerrainProvider();
  renderRequests = 0;
  pickedPosition: FakeCartesian3 | undefined = undefined;
  /** What the next hit test answers with; the real one returns undefined when it finds nothing. */
  pickResult: { id?: unknown } | undefined = undefined;
  readonly pickCalls: FakePickCall[] = [];

  requestRender() {
    this.renderRequests += 1;
  }

  /**
   * Draws a frame, which is how a test stands in for the viewer's screen refreshing.
   *
   * It is a method rather than a bare event so it can do the two things a drawn frame does in the
   * order the real one does them: raise the pre-render event, then let the ground's outline
   * collection repack itself — which only happens while the cut is switched on.
   */
  render() {
    this.preRender.raise();
    if (this.globe.clippingPolygons?.enabled) {
      this.globe.clippingPolygons.update();
    }
  }

  /**
   * Ground per screen point, for a test that cares WHERE on the screen the question was asked.
   *
   * The flat `pickedPosition` answers every pixel with one place, which is right for "what is under
   * the cursor" and useless for anything that searches the screen: a probe running down the middle
   * column looking for ground below the horizon gets the same answer at every height, so sky and
   * ground become indistinguishable and the search cannot be exercised at all. Setting this instead
   * builds a screen that actually has a horizon in it.
   */
  pickedPositionAt: ((x: number, y: number) => FakeCartesian3 | undefined) | undefined = undefined;

  pickPosition(windowPosition: Cartesian2) {
    return this.pickedPositionAt
      ? this.pickedPositionAt(windowPosition.x, windowPosition.y)
      : this.pickedPosition;
  }

  pick(windowPosition: Cartesian2, width?: number, height?: number) {
    this.pickCalls.push({ x: windowPosition.x, y: windowPosition.y, width, height });
    return this.pickResult;
  }
}

export class CesiumWidget {
  private liveScene: FakeScene | undefined = new FakeScene();

  /**
   * The real widget destroys its scene on teardown and its accessor then answers with nothing,
   * while its published type still promises a scene — so code that reads `widget.scene` after a
   * teardown does not fail on a destroyed object, it fails on `undefined`. The cast keeps that
   * exact shape here, because a double that kept the scene alive would let an unguarded call
   * survive the test suite and throw for the first viewer who navigated away.
   */
  get scene(): FakeScene {
    return this.liveScene as FakeScene;
  }

  /**
   * The element the scene draws into.
   *
   * A real event target rather than a bag of numbers, because the scene module puts a listener of
   * its own on it — the engine's pointer handler only reports moves *inside* the surface, so
   * leaving it is something only the element itself can tell anyone about. A plain object would
   * make that listener unattachable here and the behaviour untestable.
   */
  readonly canvas = Object.assign(new EventTarget(), {
    clientWidth: 1200,
    width: 1200,
    clientHeight: 800,
    height: 800,
  }) as unknown as HTMLCanvasElement;
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
    this.liveScene = undefined;
  }
}
