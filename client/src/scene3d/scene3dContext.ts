// SPDX-License-Identifier: AGPL-3.0-or-later
import 'cesium/Source/Widgets/CesiumWidget/CesiumWidget.css';
import {
  type Billboard,
  BillboardCollection,
  Cartesian2,
  Cartesian3,
  Cartographic,
  CesiumWidget,
  ClippingPolygon,
  ClippingPolygonCollection,
  Color,
  ColorGeometryInstanceAttribute,
  Credit,
  Ellipsoid,
  GeometryInstance,
  HeightReference,
  type ImageryLayer,
  Ion,
  Material,
  Math as CesiumMath,
  NearFarScalar,
  OrthographicFrustum,
  PerInstanceColorAppearance,
  PerspectiveFrustum,
  Plane,
  PolygonGeometry,
  PolygonHierarchy,
  PolylineCollection,
  Primitive,
  Rectangle,
  SceneTransforms,
  ScreenSpaceEventHandler,
  ScreenSpaceEventType,
  UrlTemplateImageryProvider,
  VerticalOrigin,
  WallGeometry,
} from 'cesium';
import { coarsePointer } from '../map/pointer.ts';
import { eyeLookingAt, wrapRoll } from './camera3d.ts';
import {
  cutawayPitchLimitDegrees,
  DEFAULT_CAMERA_FLOOR_METERS,
  footprintCenter,
} from './caveFootprint3d.ts';
import {
  cameraGroundSampleDistance,
  cameraHeightForZoom,
  groundSampleDistance as groundSampleDistanceForZoom,
  orthographicGroundSampleDistance,
  zoomForGroundSampleDistance,
} from './pseudoZoom.ts';
import { viewportBounds } from './viewBounds3d.ts';
import type {
  Scene3DBounds,
  Scene3DCameraOptions,
  Scene3DCameraState,
  Scene3DCore,
  Scene3DCutawayFootprint,
  Scene3DImageryOptions,
  Scene3DMarker,
  Scene3DPick,
  Scene3DPolyline,
  Scene3DPosition,
  Scene3DProjection,
  Scene3DScreenPosition,
  Scene3DSurfaceMode,
  Scene3DSurfaceState,
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

/**
 * Furthest from the ellipsoid a reported ground height is believed, in metres. Comfortably outside
 * the range the earth's own surface occupies — about 11 km down and 9 km up — and inside the tens
 * of kilometres a coarse tile's chord reports. See `groundHeightAt`.
 */
const GROUND_HEIGHT_LIMIT_METERS = 12_000;

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

/**
 * How far the excavation's walls are carried above the ground they are cut into. A wall stopping
 * exactly at the surface leaves a hairline of background showing along the rim; a couple of metres
 * of overlap closes it and is far below anything visible at the distance a cave is looked at from.
 */
const CUTAWAY_RIM_OVERLAP_METERS = 2;

/**
 * The excavation is drawn in earth colours rather than left as the hole it is. Cutting the ground
 * away does not put anything in its place: the opening renders as a hard black void with a
 * stepped rim, which reads as a broken renderer rather than as a hole in a hillside. These are the
 * walls and the floor of the shaft, with the floor darker so the shaft has a visible bottom.
 */
const CUTAWAY_WALL_COLOR = Color.fromCssColorString('#7a6a58');
const CUTAWAY_FLOOR_COLOR = Color.fromCssColorString('#4a4038');

/**
 * What is under the ground when the camera is below it. The engine's default is to let the
 * surface stay see-through from beneath at close range, which shows the basemap from its back
 * side — coastlines and lake outlines above a viewer standing inside a mountain.
 *
 * The near/far pair is not "how far the camera is from the region", which is how it reads: the
 * shader measures the distance from the eye to each fragment of the surface, so a value that fell
 * away with distance would make the near ceiling solid rock and everything past the far distance
 * transparent — and from underground, a horizontal look reaches a hundred kilometres of shell and
 * a downward look the whole diameter of the planet, so most of the view would be back to showing
 * the basemap from behind. Both ends are therefore the same: solid rock at every range. The pair
 * is still a near/far one because the engine only takes the underground colour into account at all
 * when it is given one with a non-zero near value.
 */
const UNDERGROUND_COLOR = Color.fromCssColorString('#3a332c');
const UNDERGROUND_COLOR_ALPHA_BY_DISTANCE = new NearFarScalar(1000, 1, 500000, 1);

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
  /** Takes down the listeners this module puts on the drawing surface itself. */
  private removeSurfaceListeners: (() => void) | undefined;
  private hoverFrame: number | undefined;
  private hoverPosition: Cartesian2 | undefined;

  private requestedSurfaceMode: Scene3DSurfaceMode = 'overlay';
  private surfaceState: Scene3DSurfaceState = {
    requested: 'overlay',
    effective: 'overlay',
    cutawayAvailable: false,
    hasFootprint: false,
  };
  private readonly surfaceListeners = new Set<(state: Scene3DSurfaceState) => void>();
  private cutawayFootprint: Scene3DCutawayFootprint | undefined;
  private clippingPolygons: ClippingPolygonCollection | undefined;
  private cutawayFill: Primitive | undefined;
  private cameraFloorHeight = DEFAULT_CAMERA_FLOOR_METERS;
  /**
   * How far over the opening the camera has to be before the cutaway is worth drawing, in degrees
   * below the horizon. Worked out from the outline when it is set rather than on every frame: it
   * only changes when the excavation does, and the answer is wanted on every drawn frame.
   */
  private cutawayPitchLimit = 0;
  private readonly cutawayAvailable: boolean;
  private readonly removePreRenderHandler: () => void;
  private readonly beforeRenderListeners = new Set<() => void>();

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
      // The two settings that decide whether the line above means anything, both pinned here
      // rather than left to the library's defaults because both happen to *be* the defaults and a
      // future version quietly changing either would turn an idle view into a permanent redraw
      // with no visible symptom at all — the view would look identical and the battery would go.
      //
      // A non-zero render-time change makes the scene redraw whenever simulation time has advanced
      // that far, and a running clock is what advances it. Zero and stopped together are what make
      // "nothing is happening" mean no frames rather than sixty a second. Measured on this scene:
      // zero frames drawn in three seconds and zero in ten, at rest, with the camera parked on its
      // descent floor, and with the ground cut away — the three states whose per-frame work could
      // have kept asking for another frame.
      maximumRenderTimeChange: 0,
      shouldAnimate: false,
      // No `terrainProvider`: the default smooth ellipsoid needs no terrain server, so a stock
      // deployment shows a working globe with nothing to install or pre-bake.
      //
      // No sky. The engine's default is a star field cube, a sun, a moon and an atmosphere shell,
      // and this view looks at the ground: at the distances a cave is read from, the sky is a few
      // pixels along the top edge or nothing at all. Turning it off is worth roughly 2% of a frame
      // — small enough to be inside this machine's run-to-run spread and not the reason — but it
      // also stops six star-field faces and a moon texture, 865 KiB together, being downloaded and
      // uploaded to the graphics card on every cold start. That is the reason, and it is a
      // measured byte count rather than a frame rate.
      //
      // These skip creation rather than hide the objects: an object that exists and is hidden
      // still fetched its textures.
      skyBox: false,
      skyAtmosphere: false,
      // Full-screen multisampling, which the engine defaults to four samples of. Measured on this
      // scene, at 1280x800 with 3100 survey lines drawn: four samples 124.9 fps / 8.01 ms a frame,
      // one sample 164.7 fps / 6.07 ms — a third of the frame budget spent on edge smoothing.
      // What replaces it is below: a post-pass that costs nothing measurable here and puts most of
      // the smoothing back.
      msaaSamples: 1,
    });

    const { scene } = this.widget;

    // The cheap half of the trade above. Measured in the same run, alternated five times: one
    // sample without it 164.7 fps, one sample with it 164.9 — no cost this instrument can see,
    // against multisampling's third of a frame. It matters here because a survey is drawn as
    // two-pixel lines at every angle, which is the worst case for an unsmoothed edge.
    //
    // The engine ships this switched off, so this is a change rather than a restatement.
    scene.postProcessStages.fxaa.enabled = true;

    // Draw the cave data over the terrain rather than letting the mountain hide it. Measured
    // against the alternatives (a see-through globe, and cutting a hole in the terrain): this is
    // the only one that shows the whole survey from every camera angle, it is the fastest of them
    // at oblique angles, and hit testing agrees with what is on screen. Its honest cost is that
    // there is no depth cue at all — a passage under 400 m of rock looks like one under 10 m — so
    // depth has to be carried by symbology, not by occlusion. A see-through globe was measured and
    // rejected: it leaves line geometry byte-for-byte unchanged while washing out the entire
    // planet, so it is not implemented in any form and there is no switch for it here.
    //
    // This is the overlay surface mode, which the scene starts in; the cutaway mode below turns
    // the depth test back on, because there the ground in front of the cave really has been taken
    // away and letting what is left occlude is the whole point of it.
    scene.globe.depthTestAgainstTerrain = false;
    scene.camera.frustum.near = NEAR_PLANE_METERS;

    // Default navigation is kept as shipped: the drag/zoom/tilt gestures are the ones users of
    // other 3D map applications already have in their fingers — with one exception.
    //
    // The camera is allowed through the ground. Refusing that would make the view useless for the
    // thing it exists for: a cave is under the surface and a viewer has to be able to go and look
    // at it from beside and below. What the engine gives up in return is its own descent limit —
    // read from its source: the code that stops a camera short of the surface, and the whole
    // block that honours a minimum zoom distance, are both skipped once collision detection is
    // off — so `clampCameraDepth` below is the only thing between a viewer and the centre of the
    // earth, and it is not optional.
    scene.screenSpaceCameraController.enableCollisionDetection = false;

    scene.globe.undergroundColor = UNDERGROUND_COLOR;
    scene.globe.undergroundColorAlphaByDistance = UNDERGROUND_COLOR_ALPHA_BY_DISTANCE;

    // Cutting a hole in the ground needs floating-point textures, which a browser may not have
    // even when it draws everything else here perfectly well. Asked once, and reported through
    // the surface state so the chrome can offer the mode or explain its absence rather than
    // presenting a control that quietly does nothing.
    this.cutawayAvailable = ClippingPolygonCollection.isSupported(scene);
    this.surfaceState = this.surfaceStateNow();

    // Settings measured on this scene and deliberately LEFT AS THEY ARE, recorded so the next
    // person does not spend the afternoon rediscovering them:
    //
    //   * Distance fog. Turning it off measured +0.8% of a frame against 8.8% of drift between the
    //     two readings either side of it — indistinguishable from noise. It also raises the detail
    //     the globe asks for at distance, so switching it off would buy nothing and fetch more
    //     tiles.
    //   * The ground atmosphere. Two independent passes disagreed about its SIGN: +22.8% in one
    //     and -8.9% in the other, on the same machine within the hour. Its effect is not
    //     separable from run-to-run variance here, and changing a setting on evidence that cannot
    //     decide which way it points is guessing with extra steps.
    //   * Globe detail (`maximumScreenSpaceError`). Doubling it measured +4.3%, paid for with a
    //     visibly coarser basemap. Not worth it at the distances a survey is read from.
    //   * Resolution scale. Halving it measured +33%, and it is not taken: the engine already
    //     ignores a phone's device pixel ratio and draws at CSS resolution, so this view is not
    //     short of fill rate — it would trade a blurred image for headroom it does not need. The
    //     library default that ignores the pixel ratio is the cheap one and must not be overridden.
    //   * Antialiasing on the whole surface is dealt with above.

    // The depth clamp and the surface mode both depend on where the camera is, and the camera can
    // move without anything in this application being told. This runs on drawn frames only, and a
    // camera that moves always draws.
    //
    // Chrome pinned to a place on the globe is repositioned from the same event, fanned out the
    // way render errors and camera rests are: the scene keeps one listener, and a frame that is
    // never drawn costs the overlay nothing, which is exactly the property drawing on demand
    // exists to buy.
    const onPreRender = () => {
      this.clampCameraDepth();
      this.refreshSurface();
      for (const listener of [...this.beforeRenderListeners]) {
        listener();
      }
    };
    scene.preRender.addEventListener(onPreRender);
    this.removePreRenderHandler = () => scene.preRender.removeEventListener(onPreRender);

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
    this.removeSurfaceListeners?.();
    this.removeSurfaceListeners = undefined;
    this.inputHandler?.destroy();
    this.inputHandler = undefined;
    this.clickListeners.clear();
    this.hoverListeners.clear();
    this.removeMoveEndHandler?.();
    this.removeMoveEndHandler = undefined;
    this.viewChangedListeners.clear();
    this.removePreRenderHandler();
    this.beforeRenderListeners.clear();
    this.surfaceListeners.clear();
    // The globe owns the clipping outline and the scene owns the excavation, and both go down
    // with the widget below; only the references to them are this object's to drop.
    this.clippingPolygons = undefined;
    this.cutawayFill = undefined;
    this.cutawayFootprint = undefined;
    this.removeRenderErrorHandler();
    this.renderErrorListeners.clear();
    this.imageryById.clear();
    // The widget takes every batch down with the scene; the map only exists so a source built
    // twice under one id can be found, and it must not outlive the scene it named.
    this.vectorSourceIds.clear();
    this.widget.destroy();
  }

  requestRender(): void {
    if (this.widget.isDestroyed()) {
      return;
    }
    this.widget.scene.requestRender();
  }

  onBeforeRender(listener: () => void): () => void {
    this.beforeRenderListeners.add(listener);
    return () => {
      this.beforeRenderListeners.delete(listener);
    };
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
      // Folded around zero for the same reason, and it bites harder: the engine reports a level
      // camera's roll as a whole turn at every tilt except straight down, so read raw it says the
      // camera is upside down nearly all the time — and "is this camera level" is exactly what
      // decides whether one of the standard views is shown as the current one.
      roll: wrapRoll(CesiumMath.toDegrees(camera.roll)),
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

  getCameraTarget(): Scene3DPosition | undefined {
    return this.screenToPosition({
      x: this.viewportWidthPixels() / 2,
      y: this.viewportHeightPixels() / 2,
    });
  }

  getProjection(): Scene3DProjection {
    return this.widget.scene.camera.frustum instanceof OrthographicFrustum
      ? 'orthographic'
      : 'perspective';
  }

  setProjection(projection: Scene3DProjection, halfWidthMeters?: number): void {
    if (this.widget.isDestroyed()) {
      return;
    }
    const { camera } = this.widget.scene;
    if (projection === 'orthographic') {
      if (!(camera.frustum instanceof OrthographicFrustum)) {
        // The engine derives the width from how far the camera currently is from the ground, so
        // the switch keeps roughly the framing the viewer already had rather than jumping.
        camera.switchToOrthographicFrustum();
      }
      if (halfWidthMeters !== undefined && halfWidthMeters > 0) {
        this.standWhereWidthIs(halfWidthMeters * 2);
      }
    } else if (camera.frustum instanceof OrthographicFrustum) {
      camera.switchToPerspectiveFrustum();
      // The switch builds a fresh frustum carrying the library's defaults, so the near plane this
      // scene needs to see inside a narrow passage has to be put back on it.
      camera.frustum.near = NEAR_PLANE_METERS;
    }
    this.requestRender();
  }

  getOrthoHalfWidth(): number | undefined {
    const { frustum } = this.widget.scene.camera;
    return frustum instanceof OrthographicFrustum ? frustum.width / 2 : undefined;
  }

  cameraHeightForZoom(zoom: number, latitude: number): number {
    if (this.getProjection() === 'orthographic') {
      // An orthographic view has no cone to widen with distance: how much ground is on screen is
      // the width of its box, and this engine does not let that be an independent property — it
      // recomputes the width from how far the camera stands from the ground under the middle of
      // the screen, on every single camera move including every frame of an animated one. So the
      // only durable way to ask for an amount of ground is to stand where that amount implies,
      // and for a camera looking straight down the distance to the ground is its height over it.
      return groundSampleDistanceForZoom(zoom, latitude) * this.viewportWidthPixels();
    }
    return cameraHeightForZoom(zoom, {
      latitudeDegrees: latitude,
      fieldOfViewRadians: this.fieldOfViewRadians(),
      viewportHeightPixels: this.viewportHeightPixels(),
    });
  }

  flyToZoom(
    longitude: number,
    latitude: number,
    zoom: number,
    options: Scene3DCameraOptions = {},
  ): void {
    // Nothing is done to the frustum afterwards, deliberately: under an orthographic projection
    // the height above answers for the zoom, and a width written here would be overwritten by the
    // engine on the next camera move — including, when this move is animated, by every frame of
    // the flight it has just started.
    const height = this.cameraHeightForZoom(zoom, latitude);
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

  setCameraFloorHeight(height: number): void {
    // A caller hands the descent limit back as it goes away, and by then the drawing surface it
    // was set on may already have been torn down — the scene is shared and reference counted, so
    // the order of two teardowns is not this object's to decide. Everything below reads the
    // scene, which a destroyed widget no longer has.
    if (this.widget.isDestroyed()) {
      return;
    }
    // Never shallower than the standing default: a caller that under-reports how deep its data
    // goes may lower the floor but must not be able to strand a viewer above the cave they came
    // to look at.
    const floor = Math.min(height, DEFAULT_CAMERA_FLOOR_METERS);
    if (floor === this.cameraFloorHeight) {
      return;
    }
    this.cameraFloorHeight = floor;
    this.clampCameraDepth();
    this.requestRender();
  }

  // ---- the ground surface ----

  setSurfaceMode(mode: Scene3DSurfaceMode): void {
    if (this.requestedSurfaceMode === mode) {
      return;
    }
    this.requestedSurfaceMode = mode;
    this.refreshSurface();
  }

  getSurfaceState(): Scene3DSurfaceState {
    return this.surfaceState;
  }

  onSurfaceStateChanged(listener: (state: Scene3DSurfaceState) => void): () => void {
    this.surfaceListeners.add(listener);
    return () => {
      this.surfaceListeners.delete(listener);
    };
  }

  setCutawayFootprint(footprint: Scene3DCutawayFootprint | undefined): void {
    if (this.widget.isDestroyed()) {
      return;
    }
    // Two points enclose no ground; anything that thin is treated as no footprint at all rather
    // than as a cut nobody could see.
    const usable = footprint && footprint.ring.length >= 3 ? footprint : undefined;

    if (usable) {
      // Worked out on every call rather than only when the outline changes: how deep the
      // excavation is depends on the ground it is cut into, and that ground can move under an
      // unchanged outline as elevation data for it arrives.
      const center = footprintCenter(usable);
      this.cutawayPitchLimit = cutawayPitchLimitDegrees(
        usable,
        this.groundHeightAt(center.longitude, center.latitude),
      );
    }

    // The outline is recomputed every time the camera comes to rest, and most of those recomputes
    // produce the outline that is already in force. Rebuilding the cut and its excavation for one
    // of those would discard and re-upload their GPU textures and geometry for no change at all.
    if (sameFootprint(this.cutawayFootprint, usable)) {
      this.refreshSurface();
      return;
    }
    this.cutawayFootprint = usable;
    const { scene } = this.widget;

    if (this.cutawayFill) {
      scene.primitives.remove(this.cutawayFill);
      this.cutawayFill = undefined;
    }

    // Worked out here rather than left to the refresh below, which reports a *change* and does
    // nothing when there is none: swapping one outline for another while the mode is already on
    // changes nothing it compares, so anything built here has to arrive already switched on.
    const cutaway = this.surfaceStateNow().effective === 'cutaway';

    if (this.cutawayAvailable) {
      // A new outline collection every time, rather than emptying and refilling the one already
      // there. The engine only rebuilds the textures it clips with when the TOTAL number of
      // points across the outlines it holds changes — and every outline this application produces
      // has the same number of points — so refilling one in place is silently discarded and the
      // ground stays cut where the first survey was, for the life of the scene. Handing the globe
      // a new collection destroys the one it held, which is the whole cost of doing it this way,
      // and it is paid once per survey rather than once per frame.
      this.clippingPolygons = usable
        ? new ClippingPolygonCollection({
            // The default sense is what is wanted: ground inside the outline is removed, ground
            // outside it is kept. The inverse — an island of ground and nothing else — is not a
            // view of a cave.
            polygons: [new ClippingPolygon({ positions: usable.ring.map(toCartesian) })],
            enabled: cutaway,
          })
        : undefined;
      scene.globe.clippingPolygons = this.clippingPolygons;

      if (usable) {
        // Only ever alongside a cut. On a browser that cannot cut the ground away this would be a
        // brown shaft standing on unbroken hillside.
        this.cutawayFill = scene.primitives.add(this.buildCutawayFill(usable));
        this.cutawayFill.show = cutaway;
      }
    }

    this.refreshSurface();
    this.requestRender();
  }

  // ---- coordinates ----

  positionToScreen(position: Scene3DPosition): Scene3DScreenPosition | undefined {
    // Guarded, unlike most read-only members, because of who calls it: chrome pinned to the globe
    // asks on every drawn frame and again whenever the window changes size, and a resize arriving
    // between the widget's teardown and the observer's is ordinary rather than exotic. A destroyed
    // widget has no scene at all — the property is undefined, not a destroyed object — so this
    // would fail inside the library rather than answer "nowhere".
    if (this.widget.isDestroyed()) {
      return undefined;
    }
    const { scene } = this.widget;
    const world = toCartesian(position);
    // Behind the camera means there is no answer, and this has to be decided here because the
    // library only decides it under a converging frustum. Under a box one, where screen position
    // does not depend on distance at all, it hands back an ordinary-looking pixel for a point that
    // is behind the viewer and is drawn nowhere — typically the middle of the view, which is the
    // most convincing place for a label about something that is not there. Reaching that is
    // ordinary rather than exotic in this application: take the perspective out of the view, then
    // descend past the cave, and every marker above is behind you.
    const inFront = Plane.getPointDistance(
      Plane.fromPointNormal(scene.camera.positionWC, scene.camera.directionWC),
      world,
    );
    if (!(inFront > 0)) {
      return undefined;
    }
    const screen = SceneTransforms.worldToWindowCoordinates(scene, world);
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
    // Each line's own colour object, with the transparency it asked for, so a fade applied later
    // multiplies that rather than flattening every line to the same value.
    const colors: { color: Color; baseAlpha: number }[] = [];
    let opacity = 1;
    return this.registerVectorSource<Scene3DPolyline>(
      id,
      () => {
        collection.removeAll();
        colors.length = 0;
      },
      (items) => {
        for (const item of items) {
          const color = Color.fromCssColorString(item.color);
          const baseAlpha = color.alpha;
          color.alpha = baseAlpha * opacity;
          colors.push({ color, baseAlpha });
          collection.add({
            positions: item.positions.map(toCartesian),
            width: item.widthPixels,
            // One material object per line, not one shared between them: clearing the collection
            // destroys each line's material, so a shared instance would be destroyed once per
            // line and every line after the first would fail. Lines carrying the same colour are
            // still drawn in one batch — the renderer groups them by the material's value, not by
            // its identity — so this costs objects, not draw calls.
            material: Material.fromType(Material.ColorType, { color }),
            id: item.id,
          });
        }
      },
      (visible) => {
        collection.show = visible;
      },
      (next) => {
        opacity = next;
        for (const entry of colors) {
          // Changed in the colour the material already holds, rather than by handing each line a
          // new material. The batch reads this value when it groups the lines into draw calls,
          // which it does every frame, so the change lands on the next one; replacing the
          // material instead marks the line's geometry dirty and rebuilds the vertex buffers of
          // a survey counted in thousands of components for what is a change of shade.
          entry.color.alpha = entry.baseAlpha * next;
        }
      },
      () => this.widget.scene.primitives.remove(collection),
    );
  }

  createMarkerSource(id: string): Scene3DVectorSource<Scene3DMarker> {
    // The scene is handed over so the collection can resolve markers that sit on the terrain.
    const collection = new BillboardCollection({ scene: this.widget.scene });
    this.widget.scene.primitives.add(collection);
    const markers: Billboard[] = [];
    let opacity = 1;
    return this.registerVectorSource<Scene3DMarker>(
      id,
      () => {
        collection.removeAll();
        markers.length = 0;
      },
      (items) => {
        for (const item of items) {
          markers.push(
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
              // The icon's own colours multiplied by white, which changes nothing until a fade
              // lowers the alpha. The icons are images, so this is the only place a marker's
              // transparency can live.
              color: Color.WHITE.withAlpha(opacity),
              id: item.id,
            }),
          );
        }
      },
      (visible) => {
        collection.show = visible;
      },
      (next) => {
        opacity = next;
        for (const marker of markers) {
          // Assigned rather than mutated: the batch keeps colours in a GPU buffer and only the
          // property setter tells it that one of them has moved.
          marker.color = Color.WHITE.withAlpha(next);
        }
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
    setAlpha: (opacity: number) => void,
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
      setOpacity: (opacity) => {
        if (!alive()) return;
        setAlpha(opacity);
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

  /**
   * The excavation itself: the walls of the shaft and the floor at the bottom of it.
   *
   * Removing ground draws nothing in its place, and the engine's own "what is under the surface"
   * colour cannot help — it applies to the far side of a globe seen from beneath, not to a region
   * that was cut out, and setting it leaves the opening black whatever it is set to. So the hole
   * gets a body of its own: a ring of wall from the floor up to the ground at each point of the
   * outline, and a floor closing it off underneath.
   *
   * Both faces of the wall are drawn, because the camera is expected to be inside the shaft as
   * often as outside it, and the whole thing is built on the spot rather than in the background:
   * the geometry is a hundred-odd vertices, and a scene that only draws when asked would
   * otherwise have to keep asking until a worker somewhere finished with it.
   */
  private buildCutawayFill(footprint: Scene3DCutawayFootprint): Primitive {
    const ring = footprint.ring.map(toCartesian);
    const wallTops = footprint.ring.map(
      (position) =>
        this.groundHeightAt(position.longitude, position.latitude) + CUTAWAY_RIM_OVERLAP_METERS,
    );
    const floors = footprint.ring.map(() => footprint.floorHeight);

    const walls = new GeometryInstance({
      geometry: new WallGeometry({
        // The ring is stored open — the outline of a shape, not a path — and a wall is a path, so
        // the first point is repeated here to close it.
        positions: [...ring, ring[0]],
        maximumHeights: [...wallTops, wallTops[0]],
        minimumHeights: [...floors, floors[0]],
        vertexFormat: PerInstanceColorAppearance.FLAT_VERTEX_FORMAT,
      }),
      attributes: { color: ColorGeometryInstanceAttribute.fromColor(CUTAWAY_WALL_COLOR) },
    });

    const floor = new GeometryInstance({
      geometry: new PolygonGeometry({
        polygonHierarchy: new PolygonHierarchy(ring),
        height: footprint.floorHeight,
        vertexFormat: PerInstanceColorAppearance.FLAT_VERTEX_FORMAT,
      }),
      attributes: { color: ColorGeometryInstanceAttribute.fromColor(CUTAWAY_FLOOR_COLOR) },
    });

    return new Primitive({
      geometryInstances: [walls, floor],
      appearance: new PerInstanceColorAppearance({
        flat: true,
        // Opaque, so it writes depth and takes part in occlusion the way rock does.
        translucent: false,
        // Not a closed solid: the camera goes inside it, and culling the faces nearest the camera
        // would leave the viewer looking out through the walls.
        closed: false,
      }),
      asynchronous: false,
      // Nothing here is a thing a viewer can select. Leaving it out of hit testing keeps a click
      // inside the opening answering with the cave behind it rather than with the hole's wall.
      allowPicking: false,
      show: false,
    });
  }

  /** What the surface state would be right now, from what was asked for and what is possible. */
  private surfaceStateNow(): Scene3DSurfaceState {
    const requested = this.requestedSurfaceMode;
    const hasFootprint = this.cutawayFootprint !== undefined;
    const possible = requested === 'cutaway' && this.cutawayAvailable && hasFootprint;
    const pausedBy = possible ? this.cutawayPausedBy() : undefined;
    return {
      requested,
      effective: possible && !pausedBy ? 'cutaway' : 'overlay',
      cutawayAvailable: this.cutawayAvailable,
      hasFootprint,
      ...(pausedBy ? { pausedBy } : {}),
    };
  }

  /**
   * Why the cut would not show the cave from where the camera is, or undefined when it would.
   *
   * Two places it would not, and they are reported apart because the way out of one is not the way
   * out of the other. From underneath, a cut does nothing useful at all: the ground is no longer
   * between the camera and the cave, and all the opening adds is a view of the underside of the
   * surrounding terrain with the basemap painted on it — no angle recovers that, only coming back
   * up. Too near the ground, the opening is edge-on and its near wall is all there is to see, and
   * tilting down does recover it; how near counts depends on how deep the excavation is against
   * how wide it is, which is why the angle comes from the outline rather than from a constant.
   *
   * Under the ground is checked first: a camera that is both underneath and shallowly pitched has
   * one thing to do about it, and being told to tilt would send it the wrong way.
   */
  private cutawayPausedBy(): 'angle' | 'belowSurface' | undefined {
    const camera = this.getCamera();
    if (camera.height <= this.groundHeightAt(camera.longitude, camera.latitude)) {
      return 'belowSurface';
    }
    return camera.pitch > this.cutawayPitchLimit ? 'angle' : undefined;
  }

  /**
   * Applies the surface mode the scene should be in, and reports it if it changed.
   *
   * Called on every drawn frame as well as on every request, because half of what decides the
   * answer is where the camera is: a viewer who tilts towards the horizon has changed the mode
   * without touching a control, and the chrome has to be able to say so.
   */
  private refreshSurface(): void {
    if (this.widget.isDestroyed()) {
      return;
    }
    const next = this.surfaceStateNow();
    const previous = this.surfaceState;
    if (
      next.requested === previous.requested &&
      next.effective === previous.effective &&
      next.hasFootprint === previous.hasFootprint &&
      next.cutawayAvailable === previous.cutawayAvailable &&
      next.pausedBy === previous.pausedBy
    ) {
      return;
    }
    this.surfaceState = next;

    const cutaway = next.effective === 'cutaway';
    if (this.clippingPolygons) {
      this.clippingPolygons.enabled = cutaway;
    }
    if (this.cutawayFill) {
      this.cutawayFill.show = cutaway;
    }
    // With the ground over the cave gone, what is left in front of it is genuinely in front of
    // it, and letting it occlude is what makes the view read as depth. Without the cut the same
    // setting would hide the entire survey inside the hillside.
    this.widget.scene.globe.depthTestAgainstTerrain = cutaway;
    this.requestRender();

    for (const listener of [...this.surfaceListeners]) {
      listener(next);
    }
  }

  /**
   * Frames an orthographic view a given number of metres wide, by standing where that width comes
   * from rather than by writing the width down.
   *
   * The engine treats an orthographic frustum's width as derived, not given: every camera move
   * recomputes it as the distance from the eye to the ground under the middle of the screen. That
   * is not an implementation detail to work around — it is what makes an ordinary zoom gesture
   * work at all under this projection, since zooming moves the camera and the box follows. A width
   * assigned on top of that survives until the next move, which in practice means until the flight
   * that was just started puts its next frame through, or until the viewer nudges either view. So
   * a width that has to last is applied as a distance, and then nothing has to defend it.
   *
   * Nothing moves when the camera already stands at that distance, which is the ordinary case when
   * a saved view is reopened: the width it carries was read off the very camera being restored.
   */
  private standWhereWidthIs(widthMeters: number): void {
    const currentHalfWidth = this.getOrthoHalfWidth();
    if (currentHalfWidth === undefined || !(widthMeters > 0)) {
      return;
    }
    // A relative comparison, because these are metres of ground and range from a cave passage to
    // half a continent; an absolute tolerance would be meaningless at one end or the other.
    if (Math.abs(currentHalfWidth * 2 - widthMeters) <= widthMeters * 1e-9) {
      return;
    }
    const camera = this.getCamera();
    const target = this.getCameraTarget();
    const pivot = {
      lon: target?.longitude ?? camera.longitude,
      lat: target?.latitude ?? camera.latitude,
      // The middle of the screen is showing sky. The ground below the camera is the honest
      // substitute: it is where a view that has lost the horizon is still standing over.
      height: target?.height ?? this.groundHeightAt(camera.longitude, camera.latitude),
    };
    const eye = eyeLookingAt(pivot, camera.heading, camera.pitch, widthMeters);
    this.setCamera({
      longitude: eye.lon,
      latitude: eye.lat,
      height: eye.height,
      heading: camera.heading,
      pitch: camera.pitch,
      roll: camera.roll,
    });
  }

  /**
   * Stops the camera at the floor. With the engine's collision detection off there is nothing
   * else holding it: a viewer who keeps zooming in past the cave passes through the planet and
   * comes out looking at the sky from the inside, with no gesture that gets them back.
   */
  private clampCameraDepth(): void {
    const { camera } = this.widget.scene;
    const carto = Cartographic.fromCartesian(camera.positionWC);
    if (!carto || carto.height >= this.cameraFloorHeight) {
      return;
    }
    camera.setView({
      destination: Cartesian3.fromDegrees(
        CesiumMath.toDegrees(carto.longitude),
        CesiumMath.toDegrees(carto.latitude),
        this.cameraFloorHeight,
      ),
      orientation: { heading: camera.heading, pitch: camera.pitch, roll: camera.roll },
    });
  }

  /**
   * Height of the drawn ground at a point, or the ellipsoid where no elevation model says.
   *
   * The answer is bounded because the globe does not only answer about the ground: it answers from
   * whatever surface tile it is holding, and for a moment after the camera arrives somewhere new
   * that is a very coarse one. A coarse tile's mesh is a flat chord across many degrees of a curved
   * planet, and the middle of a chord that wide sits TENS OF KILOMETRES below the surface it stands
   * for — measured here, -35,966 m under a camera that was 900 m over a cave. Taken as terrain, it
   * makes the camera believe it is 37 km up, which is a different map zoom: the view then asks the
   * server for a region's worth of aggregated data, and because nothing moves the camera afterwards
   * it never asks again. A view opened on a shared position sits there naming "a few entrances in
   * this area" over ground it is close enough to name each of them on.
   *
   * The earth's own ground runs from about eleven kilometres below the ellipsoid to about nine
   * above it. Anything outside that is the globe describing a tile rather than the ground, and for
   * that the ellipsoid is the better answer — no elevation model is wanted here, only a refusal to
   * believe an impossible one.
   */
  private groundHeightAt(longitude: number, latitude: number): number {
    const height = this.widget.scene.globe.getHeight(Cartographic.fromDegrees(longitude, latitude));
    return height !== undefined && Math.abs(height) <= GROUND_HEIGHT_LIMIT_METERS ? height : 0;
  }

  /** Builds the pointer handler the first time anything subscribes, and not before. */
  private ensureInputHandler(): void {
    if (this.inputHandler || this.widget.isDestroyed()) {
      return;
    }
    const surface = this.widget.canvas;
    const handler = new ScreenSpaceEventHandler(surface);

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

    // The pointer leaving the drawing surface has to be reported, and the engine's own handler
    // cannot do it: every one of its pointer listeners is bound to that surface, so the moment the
    // pointer is off it no further move arrives and the last thing hovered stays the last thing
    // hovered for ever. What that looks like is a name pinned over the middle of the scene, and a
    // pointer cursor stuck on it, after the viewer has moved onto a label, onto the controls, onto
    // the browser's own chrome or into another window entirely — until they happen to come back
    // over empty ground. Reported as a hover that found nothing, which is the same answer as
    // moving onto empty ground and needs no second path through the callers.
    const onPointerLeave = () => this.clearHover();
    surface.addEventListener('pointerleave', onPointerLeave);
    this.removeSurfaceListeners = () => surface.removeEventListener('pointerleave', onPointerLeave);

    this.inputHandler = handler;
  }

  /** Says the pointer is over nothing, and forgets any hit test still waiting for a frame. */
  private clearHover(): void {
    if (this.hoverFrame !== undefined) {
      // Otherwise the pick queued from the last move inside the surface lands after this and puts
      // the name straight back.
      window.cancelAnimationFrame(this.hoverFrame);
      this.hoverFrame = undefined;
    }
    this.hoverPosition = undefined;
    for (const listener of [...this.hoverListeners]) {
      listener(null);
    }
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
    const screen: Scene3DScreenPosition = { x: windowPosition.x, y: windowPosition.y };
    const ground = includeGround ? this.screenToPosition(screen) : undefined;
    if (id !== undefined && id !== null) {
      return ground ? { id, position: ground, screen } : { id, screen };
    }
    // The hit test never reports the globe itself, so "did this land on the ground?" is a
    // separate question and only the depth read can answer it.
    return ground ? { id: undefined, position: ground, screen } : null;
  }

  /**
   * Ground metres one pixel covers at the point the camera is aimed at. Height above the ground
   * being looked at, not above the ellipsoid: on a smooth globe they are the same number, but with
   * terrain loaded a camera 500 m over a 1500 m ridge is showing the ground detail of 500 m, not
   * of 2000 m.
   */
  private groundSampleDistance(camera: Scene3DCameraState): number {
    // An orthographic view is a box, not a cone: its width is the whole answer, and the camera's
    // distance from the ground contributes nothing. Feeding the perspective arithmetic a camera
    // that has switched projection would report a zoom the view is not at, and both the data
    // loader's requests and the box it asks for would then describe a different view than the one
    // on screen.
    const halfWidth = this.getOrthoHalfWidth();
    if (halfWidth !== undefined) {
      return orthographicGroundSampleDistance(halfWidth * 2, this.viewportWidthPixels());
    }
    return cameraGroundSampleDistance({
      heightMeters: camera.height - this.groundHeightAt(camera.longitude, camera.latitude),
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

/**
 * Whether two excavations describe the same hole. Compared point by point rather than by identity,
 * because the outline is recomputed from the survey after every camera move and a viewer looking
 * at one cave gets an equal-but-new object each time.
 */
function sameFootprint(
  left: Scene3DCutawayFootprint | undefined,
  right: Scene3DCutawayFootprint | undefined,
): boolean {
  if (left === right) {
    return true;
  }
  if (!left || !right || left.floorHeight !== right.floorHeight) {
    return false;
  }
  if (left.ring.length !== right.ring.length) {
    return false;
  }
  return left.ring.every((position, index) => {
    const other = right.ring[index];
    return (
      position.longitude === other.longitude &&
      position.latitude === other.latitude &&
      position.height === other.height
    );
  });
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
