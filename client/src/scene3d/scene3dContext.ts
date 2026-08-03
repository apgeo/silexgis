// SPDX-License-Identifier: AGPL-3.0-or-later
import 'cesium/Source/Widgets/CesiumWidget/CesiumWidget.css';
import {
  BillboardCollection,
  Cartesian2,
  Cartesian3,
  Cartographic,
  CesiumWidget,
  Color,
  Credit,
  Ellipsoid,
  HeightReference,
  type ImageryLayer,
  Ion,
  Material,
  Math as CesiumMath,
  PerspectiveFrustum,
  PolylineCollection,
  Rectangle,
  SceneTransforms,
  ScreenSpaceEventHandler,
  ScreenSpaceEventType,
  UrlTemplateImageryProvider,
  VerticalOrigin,
} from 'cesium';
import { coarsePointer } from '../map/pointer.ts';
import { cameraGroundSampleDistance, cameraHeightForZoom, zoomForGroundSampleDistance } from './pseudoZoom.ts';
import { viewportBounds } from './viewBounds3d.ts';
import type {
  Scene3DBounds,
  Scene3DCameraOptions,
  Scene3DCameraState,
  Scene3DCore,
  Scene3DImageryOptions,
  Scene3DMarker,
  Scene3DPick,
  Scene3DPolyline,
  Scene3DPosition,
  Scene3DScreenPosition,
  Scene3DVectorSource,
} from './scene3dEngine.ts';

// This is the ONLY module in the application allowed to import the 3D engine library, and the
// build fails if that stops being true (`npm run lint:deps`). Everything else works through the
// engine contract next door. The rule is not stylistic: an engine reached from fifty files is an
// engine that can never be replaced or upgraded across a breaking change, and a 3D library that
// leaks into stores and pages also leaks its ~1 MB of code into bundles that have no 3D in them.
//
// The module is written to be loaded on demand — nothing in the application imports it
// statically — so the engine and its runtime assets are downloaded only by a session that
// actually opens a 3D view.

// ---- air gap ---------------------------------------------------------------
//
// The library ships with a default access token for its vendor's hosted terrain, imagery and
// geocoding services, and several of its constructors reach for those services unless told not
// to. This installation is self-hosted and must work with no route to the internet at all, so the
// token is cleared here, at module scope, before anything can build a scene with it: an empty
// token also suppresses the vendor's "you are using the default token" credit, and it turns any
// future accidental use of a hosted service into an obvious failure rather than a silent outbound
// request carrying which caves are being looked at.
//
// This is a property of how the scene is configured, not of the library lacking those URLs — the
// bundle still contains them. That is why it is covered by a test that watches for requests
// rather than by a comment that hopes for the best.
Ion.defaultAccessToken = '';

/** Southern Carpathians, the same opening view the 2D workspace map uses. */
const DEFAULT_LONGITUDE = 25.3;
const DEFAULT_LATITUDE = 45.7;
const DEFAULT_ZOOM = 8;

/** Matches the 2D map's camera animation, so moving between the two views feels like one app. */
const FLIGHT_SECONDS = 0.5;

/**
 * Closest distance the camera can see. The library's 1 m default clips the walls away when the
 * camera is inside a narrow passage, which is precisely where a cave view is most useful.
 */
const NEAR_PLANE_METERS = 0.5;

/** Fallback vertical field of view when the frustum is not the perspective one (60°, the default). */
const DEFAULT_FOVY_RADIANS = Math.PI / 3;

/** A point box gets this zoom instead of an unframeable rectangle — the 2D map's `fit` maximum. */
const POINT_FIT_ZOOM = 17;

/**
 * How far from the pixel a hit test may reach, in pixels, by pointer type. The same figures the
 * flat map uses: a finger lands nowhere near as precisely as a cursor, and a marker is only a few
 * pixels of ink. Measured on this renderer, a wider tolerance is free — the hit test costs the
 * same at one pixel as at twenty-four — and it never returned a different object than the exact
 * test did, only an object where the exact test found nothing.
 */
const MOUSE_PICK_TOLERANCE_PIXELS = 6;
const TOUCH_PICK_TOLERANCE_PIXELS = 12;

/**
 * Markers are kept hittable no matter what is drawn in front of them. A marker on the far side of
 * a ridge is drawn — the cave data is deliberately not hidden by terrain — but without this the
 * hit test still consults the depth of the ridge and reports nothing, so the marker looks present
 * and simply refuses to be clicked. Measured: a marker in a valley had terrain forty metres nearer
 * the camera at its own pixel, and this is what makes it pickable again.
 */
const MARKER_PICKABLE_AT_ANY_DEPTH = Number.POSITIVE_INFINITY;

class CesiumScene3D implements Scene3DCore {
  private readonly widget: CesiumWidget;
  private readonly imageryById = new Map<string, ImageryLayer>();
  private readonly renderErrorListeners = new Set<(message: string) => void>();
  private readonly removeRenderErrorHandler: () => void;

  /** Every batch currently in the scene, so a source built twice under one id cannot orphan one. */
  private readonly vectorSourceIds = new Map<string, Scene3DVectorSource<never>>();

  private readonly viewChangedListeners = new Set<() => void>();
  private removeMoveEndHandler: (() => void) | undefined;

  private readonly clickListeners = new Set<(pick: Scene3DPick | null) => void>();
  private readonly hoverListeners = new Set<(pick: Scene3DPick | null) => void>();
  private inputHandler: ScreenSpaceEventHandler | undefined;
  private hoverFrame: number | undefined;
  private hoverPosition: Cartesian2 | undefined;

  constructor(container: HTMLElement) {
    this.widget = new CesiumWidget(container, {
      // Without this the widget builds its own basemap from the vendor's hosted imagery service.
      // Base imagery comes from this installation's own layer catalog instead.
      baseLayer: false,
      // The widget's built-in failure panel is an untranslated English overlay containing a link
      // to an external website — wrong on both counts for a self-hosted, bilingual application.
      // Turning it off also silences the panel the widget raises when rendering dies later, so
      // that event is subscribed to below and surfaced through the application's own chrome.
      showRenderLoopErrors: false,
      // Draw only when something changed. Measured: a still 3D view draws zero frames and costs
      // no battery, which is what makes the view affordable on a phone.
      requestRenderMode: true,
      // No `terrainProvider`: the default smooth ellipsoid needs no terrain server, so a stock
      // deployment shows a working globe with nothing to install or pre-bake.
    });

    const { scene } = this.widget;

    // Draw the cave data over the terrain rather than letting the mountain hide it. Measured
    // against the alternatives (a see-through globe, and cutting a hole in the terrain): this is
    // the only one that shows the whole survey from every camera angle, it is the fastest of them
    // at oblique angles, and hit testing agrees with what is on screen. Its honest cost is that
    // there is no depth cue at all — a passage under 400 m of rock looks like one under 10 m — so
    // depth has to be carried by symbology, not by occlusion. A see-through globe was measured and
    // rejected: it leaves line geometry byte-for-byte unchanged while washing out the entire
    // planet, so it is not implemented in any form and there is no switch for it here.
    scene.globe.depthTestAgainstTerrain = false;
    scene.camera.frustum.near = NEAR_PLANE_METERS;

    // Default navigation is kept as shipped: the drag/zoom/tilt gestures are the ones users of
    // other 3D map applications already have in their fingers.

    // Two parameters, and the first one is deliberately unused: the engine raises this event as
    // `(scene, error)`, so a one-parameter listener silently binds the scene and every diagnostic
    // it was written to surface degrades to "[object Object]". The library's typings cannot catch
    // that — the event is declared with an untyped listener signature — so the arity is pinned
    // here and asserted by a test that raises the event the way the engine does.
    const onRenderError = (_scene: unknown, error: unknown) => {
      const message = error instanceof Error ? error.message : String(error);
      for (const listener of this.renderErrorListeners) {
        listener(message);
      }
    };
    scene.renderError.addEventListener(onRenderError);
    this.removeRenderErrorHandler = () => scene.renderError.removeEventListener(onRenderError);

    this.flyToZoom(DEFAULT_LONGITUDE, DEFAULT_LATITUDE, DEFAULT_ZOOM);
  }

  /** The element the widget was told to fill — used to refuse a second, conflicting mount. */
  get container(): Element {
    return this.widget.container;
  }

  // ---- lifecycle ----

  isDestroyed(): boolean {
    return this.widget.isDestroyed();
  }

  destroy(): void {
    if (this.widget.isDestroyed()) {
      return; // The library replaces every method with a thrower once destroyed.
    }
    if (this.hoverFrame !== undefined) {
      window.cancelAnimationFrame(this.hoverFrame);
      this.hoverFrame = undefined;
    }
    this.inputHandler?.destroy();
    this.inputHandler = undefined;
    this.clickListeners.clear();
    this.hoverListeners.clear();
    this.removeMoveEndHandler?.();
    this.removeMoveEndHandler = undefined;
    this.viewChangedListeners.clear();
    this.removeRenderErrorHandler();
    this.renderErrorListeners.clear();
    this.imageryById.clear();
    // The widget takes every batch down with the scene; the map only exists so a source built
    // twice under one id can be found, and it must not outlive the scene it named.
    this.vectorSourceIds.clear();
    this.widget.destroy();
  }

  requestRender(): void {
    this.widget.scene.requestRender();
  }

  subscribeRenderError(listener: (message: string) => void): () => void {
    this.renderErrorListeners.add(listener);
    return () => {
      this.renderErrorListeners.delete(listener);
    };
  }

  // ---- imagery ----

  addImageryLayer(id: string, options: Scene3DImageryOptions): void {
    if (this.imageryById.has(id)) {
      return;
    }
    const provider = new UrlTemplateImageryProvider({
      url: options.urlTemplate,
      // Shown on the scene rather than behind the engine's "data attribution" link, which is
      // where a plain string would land: tile licences ask for visible credit, and the 2D map
      // puts the same text on the map itself.
      //
      // A layer with no attribution must pass `undefined`, not an empty string: the engine wraps
      // any string it is given — including '' — into a credit object, and a credit that is not
      // marked for on-screen display is filed under the engine's collapsed "data attribution"
      // list. One empty entry there is enough to reveal that control, which is a hard-coded
      // English link opening a hard-coded English panel listing a blank line. `undefined` leaves
      // the provider with no credit at all and the control stays hidden.
      credit: options.attribution ? new Credit(options.attribution, true) : undefined,
      // Nothing in this application picks features out of a raster basemap, and leaving it on
      // invites a request to a per-tile feature-info URL the catalog never configured.
      enablePickFeatures: false,
    });
    const layer = this.widget.scene.imageryLayers.addImageryProvider(provider);
    layer.show = options.visible ?? true;
    layer.alpha = options.opacity ?? 1;
    this.imageryById.set(id, layer);
  }

  removeImageryLayer(id: string): void {
    const layer = this.imageryById.get(id);
    if (!layer) {
      return;
    }
    this.imageryById.delete(id);
    this.widget.scene.imageryLayers.remove(layer, true);
  }

  hasImageryLayer(id: string): boolean {
    return this.imageryById.has(id);
  }

  getImageryLayerIds(): string[] {
    return [...this.imageryById.keys()];
  }

  setImageryLayerVisible(id: string, visible: boolean): void {
    const layer = this.imageryById.get(id);
    if (layer) {
      layer.show = visible;
    }
  }

  setImageryLayerOpacity(id: string, opacity: number): void {
    const layer = this.imageryById.get(id);
    if (layer) {
      layer.alpha = opacity;
    }
  }

  // ---- camera ----

  getCamera(): Scene3DCameraState {
    const { camera } = this.widget.scene;
    const carto = Cartographic.fromCartesian(camera.positionWC);
    return {
      longitude: carto ? CesiumMath.toDegrees(carto.longitude) : 0,
      latitude: carto ? CesiumMath.toDegrees(carto.latitude) : 0,
      height: carto ? carto.height : 0,
      // Wrapped into [0, 360): looking straight down the engine reports a heading of exactly one
      // full turn, and a saved view or a 2D handover comparing 360 with 0 would see a change
      // where the camera did not move.
      heading: wrapDegrees(CesiumMath.toDegrees(camera.heading)),
      pitch: CesiumMath.toDegrees(camera.pitch),
      roll: CesiumMath.toDegrees(camera.roll),
    };
  }

  setCamera(state: Scene3DCameraState, options: Scene3DCameraOptions = {}): void {
    const destination = Cartesian3.fromDegrees(state.longitude, state.latitude, state.height);
    const orientation = {
      heading: CesiumMath.toRadians(state.heading),
      pitch: CesiumMath.toRadians(state.pitch),
      roll: CesiumMath.toRadians(state.roll),
    };
    const { camera } = this.widget.scene;
    if (options.animate) {
      camera.flyTo({ destination, orientation, duration: FLIGHT_SECONDS });
    } else {
      camera.setView({ destination, orientation });
    }
  }

  flyToZoom(
    longitude: number,
    latitude: number,
    zoom: number,
    options: Scene3DCameraOptions = {},
  ): void {
    const height = cameraHeightForZoom(zoom, {
      latitudeDegrees: latitude,
      fieldOfViewRadians: this.fieldOfViewRadians(),
      viewportHeightPixels: this.viewportHeightPixels(),
    });
    this.setCamera(
      {
        longitude,
        latitude,
        height: Number.isFinite(height) ? height : 0,
        heading: 0,
        pitch: -90,
        roll: 0,
      },
      options,
    );
  }

  fitBounds(bounds: Scene3DBounds, options: Scene3DCameraOptions = {}): void {
    const [west, south, east, north] = bounds;
    // Only a box degenerate in BOTH axes gets the close-up. The engine frames a rectangle by
    // taking the largest distance over its corners and edge midpoints, with no floor under the
    // result: for a true point every one of those is zero, so it would place the camera exactly
    // on the surface. A box that is flat in one axis still has a real extent in the other and the
    // engine frames it correctly, so a zero-width box — two entrances on the same meridian, a
    // survey line running due north — must be passed through rather than collapsed to its
    // midpoint, which would throw the whole of that extent away. The 2D map behaves the same way:
    // it frames the full extent and merely caps how far in the zoom may go.
    if (west === east && south === north) {
      this.flyToZoom((west + east) / 2, (south + north) / 2, POINT_FIT_ZOOM, options);
      return;
    }
    const destination = Rectangle.fromDegrees(west, south, east, north);
    const { camera } = this.widget.scene;
    if (options.animate) {
      camera.flyTo({ destination, duration: FLIGHT_SECONDS });
    } else {
      camera.setView({ destination });
    }
  }

  getPseudoZoom(): number {
    const camera = this.getCamera();
    return zoomForGroundSampleDistance(this.groundSampleDistance(camera), camera.latitude);
  }

  getVisibleBounds(): Scene3DBounds | undefined {
    const width = this.viewportWidthPixels();
    const height = this.viewportHeightPixels();
    const camera = this.getCamera();
    // The ground under the middle of the screen, which is what the view is about. Falling back to
    // the point directly beneath the camera keeps a tilted view that has the horizon in its centre
    // asking about somewhere real rather than about nothing.
    const center = this.screenToPosition({ x: width / 2, y: height / 2 });
    return viewportBounds({
      centerLongitude: center?.longitude ?? camera.longitude,
      centerLatitude: center?.latitude ?? camera.latitude,
      // The same figure the zoom is derived from, deliberately: the box and the zoom sent with it
      // have to describe one view, and a box scaled off the true distance to a tilted camera's
      // aim point would cover more ground than the zoom it is paired with claims to.
      metersPerPixel: this.groundSampleDistance(camera),
      viewportWidthPixels: width,
      viewportHeightPixels: height,
    });
  }

  onViewChanged(listener: () => void): () => void {
    if (this.widget.isDestroyed()) {
      return () => {};
    }
    // Fanned out from one engine subscription, the same way render errors are: the scene's own
    // event then has exactly one listener, which is removed with the scene rather than left
    // holding a closure over application state after the drawing surface is gone.
    if (!this.removeMoveEndHandler) {
      const onMoveEnd = () => {
        for (const viewListener of [...this.viewChangedListeners]) {
          viewListener();
        }
      };
      const { moveEnd } = this.widget.scene.camera;
      moveEnd.addEventListener(onMoveEnd);
      this.removeMoveEndHandler = () => moveEnd.removeEventListener(onMoveEnd);
    }
    this.viewChangedListeners.add(listener);
    return () => {
      this.viewChangedListeners.delete(listener);
    };
  }

  // ---- coordinates ----

  positionToScreen(position: Scene3DPosition): Scene3DScreenPosition | undefined {
    const screen = SceneTransforms.worldToWindowCoordinates(this.widget.scene, toCartesian(position));
    return screen ? { x: screen.x, y: screen.y } : undefined;
  }

  screenToPosition(screen: Scene3DScreenPosition): Scene3DPosition | undefined {
    const { scene } = this.widget;
    const windowPosition = new Cartesian2(screen.x, screen.y);
    // `pickPosition` reads the depth buffer, so it answers with the terrain surface actually
    // drawn; it returns nothing when the pixel is sky, and also when the last frame's depth is
    // unusable. The ellipsoid intersection is the honest fallback for both, and is exact on the
    // smooth globe a stock deployment runs.
    const world =
      scene.pickPosition(windowPosition) ??
      scene.camera.pickEllipsoid(windowPosition, Ellipsoid.WGS84);
    if (!world) {
      return undefined;
    }
    const carto = Cartographic.fromCartesian(world);
    if (!carto) {
      return undefined;
    }
    return {
      longitude: CesiumMath.toDegrees(carto.longitude),
      latitude: CesiumMath.toDegrees(carto.latitude),
      height: carto.height,
    };
  }

  // ---- picking ----

  onClick(listener: (pick: Scene3DPick | null) => void): () => void {
    this.ensureInputHandler();
    this.clickListeners.add(listener);
    return () => {
      this.clickListeners.delete(listener);
    };
  }

  onHover(listener: (pick: Scene3DPick | null) => void): () => void {
    this.ensureInputHandler();
    this.hoverListeners.add(listener);
    return () => {
      this.hoverListeners.delete(listener);
    };
  }

  // ---- vector sources ----

  createPolylineSource(id: string): Scene3DVectorSource<Scene3DPolyline> {
    // A collection of lines drawn straight between the positions given, which is what a survey
    // leg is. The alternative shape a mapping library offers — a line following a constant compass
    // bearing — would subdivide every metre-scale shot into vertices describing a curve that is
    // not there, on geometry already counted in the tens of thousands of components.
    const collection = new PolylineCollection();
    this.widget.scene.primitives.add(collection);
    return this.registerVectorSource<Scene3DPolyline>(
      id,
      () => collection.removeAll(),
      (items) => {
        for (const item of items) {
          collection.add({
            positions: item.positions.map(toCartesian),
            width: item.widthPixels,
            // One material object per line, not one shared between them: clearing the collection
            // destroys each line's material, so a shared instance would be destroyed once per
            // line and every line after the first would fail. Lines carrying the same colour are
            // still drawn in one batch — the renderer groups them by the material's value, not by
            // its identity — so this costs objects, not draw calls.
            material: Material.fromType(Material.ColorType, {
              color: Color.fromCssColorString(item.color),
            }),
            id: item.id,
          });
        }
      },
      (visible) => {
        collection.show = visible;
      },
      () => this.widget.scene.primitives.remove(collection),
    );
  }

  createMarkerSource(id: string): Scene3DVectorSource<Scene3DMarker> {
    // The scene is handed over so the collection can resolve markers that sit on the terrain.
    const collection = new BillboardCollection({ scene: this.widget.scene });
    this.widget.scene.primitives.add(collection);
    return this.registerVectorSource<Scene3DMarker>(
      id,
      () => collection.removeAll(),
      (items) => {
        for (const item of items) {
          collection.add({
            position: toCartesian(item.position),
            image: item.image,
            scale: item.scale ?? 1,
            heightReference: item.clampToGround
              ? HeightReference.CLAMP_TO_GROUND
              : HeightReference.NONE,
            // The icons are symbols centred on the thing they mark, the way the flat map draws
            // them, rather than pins standing on it.
            verticalOrigin: VerticalOrigin.CENTER,
            disableDepthTestDistance: MARKER_PICKABLE_AT_ANY_DEPTH,
            id: item.id,
          });
        }
      },
      (visible) => {
        collection.show = visible;
      },
      () => this.widget.scene.primitives.remove(collection),
    );
  }

  // ---- internals ----

  /**
   * Wraps one batch in the contract's handle. Every mutating path asks for a frame here rather
   * than at the call site: the scene draws only when asked, so a batch that left the redraw to
   * its caller would appear to work whenever the camera happened to be moving and to do nothing
   * whenever it was not — which is the same symptom as the data never arriving.
   */
  private registerVectorSource<TItem>(
    id: string,
    clearItems: () => void,
    addItems: (items: readonly TItem[]) => void,
    setShow: (visible: boolean) => void,
    removeFromScene: () => void,
  ): Scene3DVectorSource<TItem> {
    // Creating a source under an id already in the scene replaces it. A view that remounts without
    // tearing down would otherwise leave the previous batch drawing forever, with nothing holding
    // a handle to it.
    this.vectorSourceIds.get(id)?.remove();

    let removed = false;
    const alive = () => !removed && !this.widget.isDestroyed();

    const handle: Scene3DVectorSource<TItem> = {
      replace: (items) => {
        if (!alive()) return;
        clearItems();
        addItems(items);
        this.requestRender();
      },
      clear: () => {
        if (!alive()) return;
        clearItems();
        this.requestRender();
      },
      setVisible: (visible) => {
        if (!alive()) return;
        setShow(visible);
        this.requestRender();
      },
      remove: () => {
        if (removed) return;
        removed = true;
        this.vectorSourceIds.delete(id);
        if (this.widget.isDestroyed()) return;
        removeFromScene();
        this.requestRender();
      },
    };
    this.vectorSourceIds.set(id, handle as Scene3DVectorSource<never>);
    return handle;
  }

  /** Builds the pointer handler the first time anything subscribes, and not before. */
  private ensureInputHandler(): void {
    if (this.inputHandler || this.widget.isDestroyed()) {
      return;
    }
    const handler = new ScreenSpaceEventHandler(this.widget.canvas);

    handler.setInputAction((event: { position: Cartesian2 }) => {
      // A click is worth a depth read: knowing where on the ground it landed is what lets a
      // caller act on empty ground rather than only on the things drawn over it.
      const pick = this.pickAt(event.position, true);
      for (const listener of [...this.clickListeners]) {
        listener(pick);
      }
    }, ScreenSpaceEventType.LEFT_CLICK);

    handler.setInputAction((event: { endPosition: Cartesian2 }) => {
      this.scheduleHoverPick(event.endPosition);
    }, ScreenSpaceEventType.MOUSE_MOVE);

    this.inputHandler = handler;
  }

  /**
   * Hit tests at most once per drawn frame, against the latest place the pointer has been.
   *
   * A pointer emits moves far faster than the scene draws, and a hit test costs a render pass of
   * its own — measured at up to fifty milliseconds under a camera below ground, which is several
   * frames' entire budget. Testing the newest position once per frame and discarding the
   * positions in between answers the only question hover asks: what is under the pointer now.
   *
   * On a touch device it answers nothing at all, so it is not asked. Hover describes what a
   * pointer is resting on, and a finger rests on nothing: the engine synthesises pointer moves
   * from a one-finger drag, so without the guard every frame of every pan on a phone would buy an
   * extra render pass to compute something the device cannot show. The pointer type is read here
   * rather than remembered, matching the hit-test tolerance, so a tablet that gains a mouse gets
   * hover from its next movement.
   */
  private scheduleHoverPick(windowPosition: Cartesian2): void {
    if (this.hoverListeners.size === 0) {
      return; // Nobody is listening; a click-only caller must not pay for hit tests it ignores.
    }
    if (coarsePointer()) {
      return;
    }
    this.hoverPosition = new Cartesian2(windowPosition.x, windowPosition.y);
    if (this.hoverFrame !== undefined) {
      return;
    }
    this.hoverFrame = window.requestAnimationFrame(() => {
      this.hoverFrame = undefined;
      const position = this.hoverPosition;
      if (!position || this.widget.isDestroyed()) {
        return;
      }
      // No depth read on hover: what is under the pointer is the whole question, and the ground
      // beneath it would cost a second pass per frame to answer something nobody asked.
      const pick = this.pickAt(position, false);
      for (const listener of [...this.hoverListeners]) {
        listener(pick);
      }
    });
  }

  private pickAt(windowPosition: Cartesian2, includeGround: boolean): Scene3DPick | null {
    const { scene } = this.widget;
    const tolerance = coarsePointer() ? TOUCH_PICK_TOLERANCE_PIXELS : MOUSE_PICK_TOLERANCE_PIXELS;
    const picked = scene.pick(windowPosition, tolerance, tolerance) as
      | { id?: unknown }
      | undefined;
    const id = picked?.id;
    const ground = includeGround
      ? this.screenToPosition({ x: windowPosition.x, y: windowPosition.y })
      : undefined;
    if (id !== undefined && id !== null) {
      return ground ? { id, position: ground } : { id };
    }
    // The hit test never reports the globe itself, so "did this land on the ground?" is a
    // separate question and only the depth read can answer it.
    return ground ? { id: undefined, position: ground } : null;
  }

  /**
   * Ground metres one pixel covers at the point the camera is aimed at. Height above the ground
   * being looked at, not above the ellipsoid: on a smooth globe they are the same number, but with
   * terrain loaded a camera 500 m over a 1500 m ridge is showing the ground detail of 500 m, not
   * of 2000 m.
   */
  private groundSampleDistance(camera: Scene3DCameraState): number {
    const groundHeight =
      this.widget.scene.globe.getHeight(
        Cartographic.fromDegrees(camera.longitude, camera.latitude),
      ) ?? 0;
    return cameraGroundSampleDistance({
      heightMeters: camera.height - groundHeight,
      latitudeDegrees: camera.latitude,
      fieldOfViewRadians: this.fieldOfViewRadians(),
      viewportHeightPixels: this.viewportHeightPixels(),
    });
  }

  private fieldOfViewRadians(): number {
    const { frustum } = this.widget.scene.camera;
    return frustum instanceof PerspectiveFrustum && frustum.fovy > 0
      ? frustum.fovy
      : DEFAULT_FOVY_RADIANS;
  }

  private viewportHeightPixels(): number {
    // Before the first layout the canvas has no CSS size; any positive number keeps the zoom
    // arithmetic finite, and the next camera move recomputes it against the real one.
    const { canvas } = this.widget;
    return canvas.clientHeight || canvas.height || 1;
  }

  private viewportWidthPixels(): number {
    const { canvas } = this.widget;
    return canvas.clientWidth || canvas.width || 1;
  }
}

/** A contract position as the engine's own world coordinate. */
function toCartesian(position: Scene3DPosition): Cartesian3 {
  return Cartesian3.fromDegrees(position.longitude, position.latitude, position.height);
}

/** A compass bearing folded into [0, 360). */
function wrapDegrees(degrees: number): number {
  const wrapped = degrees % 360;
  return wrapped < 0 ? wrapped + 360 : wrapped;
}

/** A hold on the shared scene. Release it exactly once, from the same place that acquired it. */
export interface Scene3DSession {
  engine: Scene3DCore;
  release(): void;
}

// One scene per window, deliberately. A 3D drawing context is an expensive, limited resource — a
// browser will drop the oldest one once a handful are alive — so a route and a workspace panel
// showing the same scene share this one rather than each building their own.
let scene: CesiumScene3D | null = null;
let holders = 0;

/**
 * Creates the scene inside `container`, or joins the one already there, and returns a hold on it.
 * The scene is torn down when the last hold is released.
 *
 * Reference counted with a latched release for the same reason the rest of the application does
 * it: React invokes an effect's setup and cleanup twice in development to surface exactly this
 * class of bug, and a second view of the same scene may legitimately be open at the same time.
 * Counting holds keeps the drawing context alive across the double invoke instead of destroying
 * and rebuilding it, and the latch means a cleanup that runs twice cannot drive the count
 * negative and strand the context forever.
 */
export function acquireScene3D(container: HTMLElement): Scene3DSession {
  if (scene && scene.container !== container) {
    throw new Error('a 3D scene is already attached to a different container');
  }
  scene ??= new CesiumScene3D(container);
  holders += 1;

  const held = scene;
  let released = false;
  return {
    engine: held,
    release() {
      if (released) {
        return;
      }
      released = true;
      holders -= 1;
      // Only tear down the scene this hold was actually taken on: if it was already replaced,
      // destroying now would take out a scene somebody else is holding.
      if (holders === 0 && scene === held) {
        scene = null;
        held.destroy();
      }
    },
  };
}
