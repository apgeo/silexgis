// SPDX-License-Identifier: AGPL-3.0-or-later

// The contract between the application and whatever draws the 3D scene.
//
// Exactly one module in this application talks to a 3D engine library, and everything else talks
// to the types below. That is enforced by a lint rule rather than left to discipline, because the
// cost of an engine leaking into pages, stores and data modules is only ever discovered when it is
// too late to pay. The interface is not an abstraction layer with two implementations behind it —
// there is one implementation and there is no plan to write a second today. It exists so that a
// second one *could* be written against a written contract instead of by archaeology, and so that
// the sibling modules (data loaders, view state, presets, selection) stay unit-testable with a
// plain object standing in for the engine and no GPU anywhere near the test runner.
//
// Two deliberate non-goals:
//
//   * The coordinate model is NOT abstracted. Every position below is WGS84 longitude/latitude in
//     degrees plus metres above the ellipsoid, and that is stated as a fact about this application
//     rather than as one option among several. An engine that models the world as a plane cannot
//     be hidden behind a coordinate abstraction without either lying or costing more than the
//     switch it is meant to cheapen.
//   * There is no `applyPreset`/`applyView` member. A camera preset or a saved view is a
//     `Scene3DCameraState` plus a flag, computed by engine-free code and handed to `setCamera` —
//     putting it in the engine contract would move product policy behind the seam.
//
// Members are grouped so that an implementation can honestly declare which groups it satisfies.
// Today's implementation satisfies `Scene3DCore` (lifecycle, imagery, camera, coordinates,
// picking, vector sources, the ground surface and its elevation); model loading is declared here
// and is not implemented yet, so no code claims to provide it.

/** A position on the globe: degrees, plus metres above the WGS84 ellipsoid. */
export interface Scene3DPosition {
  longitude: number;
  latitude: number;
  /** Metres above the WGS84 ellipsoid — not above terrain, and not an orthometric height. */
  height: number;
}

/** Where the camera is and which way it points; angles in degrees. */
export interface Scene3DCameraState extends Scene3DPosition {
  /** Compass direction the camera faces: 0 = north, increasing clockwise. */
  heading: number;
  /** Angle below the horizon: -90 looks straight down, 0 looks at the horizon. */
  pitch: number;
  roll: number;
}

/**
 * A position that may belong on the ground rather than at a height of its own.
 *
 * The distinction cannot be recovered from the numbers. A marker dropped onto the ground has no
 * height until something says how high the ground under it is, and until an elevation model is
 * loaded that answer is zero everywhere — so an anchor written as "zero" and an anchor written as
 * "wherever the ground is" are the same object, right up to the moment they stop being. Whatever
 * projects one of these has to resolve it against the ground first; `height` is what to use when
 * there is no better answer.
 */
export interface Scene3DAnchor extends Scene3DPosition {
  onGround?: boolean;
}

/** A screen position in CSS pixels relative to the scene's drawing surface. */
export interface Scene3DScreenPosition {
  x: number;
  y: number;
}

/** Longitude/latitude bounds in degrees, west, south, east, north. */
export type Scene3DBounds = [number, number, number, number];

export interface Scene3DImageryOptions {
  /** Tile URL with `{z}`/`{x}`/`{y}` placeholders, exactly as the layer catalog stores it. */
  urlTemplate: string;
  /** Attribution text; shown by the scene because the licence of the tiles requires it. */
  attribution?: string;
  visible?: boolean;
  /** 0..1; 1 is fully opaque. */
  opacity?: number;
}

// ---- lifecycle --------------------------------------------------------------

export interface Scene3DLifecycle {
  /** True once `destroy` has run. A destroyed scene must never be used again. */
  isDestroyed(): boolean;
  /**
   * Releases the drawing surface and every GPU resource behind it. Calling it twice is a no-op:
   * a browser allows only a handful of live 3D drawing contexts, so leaking one is fatal to the
   * next scene, while a double teardown is an ordinary consequence of a component remounting.
   */
  destroy(): void;
  /**
   * Draws one frame. The scene renders on demand rather than continuously — an idle 3D view then
   * costs nothing on a phone — so anything that changes what should be on screen without moving
   * the camera has to ask for the redraw itself.
   */
  requestRender(): void;
  /**
   * Fires immediately before each frame the scene actually draws, with the camera already moved to
   * where that frame will be seen from. Returns an unsubscribe function.
   *
   * It exists for chrome pinned to a place in the world — a label over a cave entrance has to be
   * repositioned on every frame of a drag and every frame of an animated flight, and there is no
   * other moment at which that is both necessary and sufficient. Doing it from an animation frame
   * loop instead would work and would also destroy the property that makes this view affordable on
   * a phone: a still scene draws nothing, so a listener here costs nothing while nothing is
   * happening, whereas a loop of one's own runs at the display's refresh rate for ever.
   *
   * The corollary is that it is silent while the scene is idle, so it must never be the only thing
   * that positions something: whatever it drives has to be placed once when it appears as well.
   */
  onBeforeRender(listener: () => void): () => void;
  /**
   * Reports that rendering has stopped because the graphics context failed (a lost GPU context, a
   * driver reset). Returns an unsubscribe function. The message is engine text for a diagnostic
   * detail line, never a user-facing sentence — the caller supplies the translated wording.
   */
  subscribeRenderError(listener: (message: string) => void): () => void;
}

// ---- imagery ----------------------------------------------------------------

/**
 * Raster basemaps draped over the globe. Layers are addressed by a caller-chosen string id, the
 * same convention the 2D map uses, so nothing outside the engine module ever holds an engine
 * object. Adding an id that already exists is a no-op.
 */
export interface Scene3DImagery {
  addImageryLayer(id: string, options: Scene3DImageryOptions): void;
  removeImageryLayer(id: string): void;
  hasImageryLayer(id: string): boolean;
  /** Ids of the imagery layers currently in the scene, bottom→top. */
  getImageryLayerIds(): string[];
  setImageryLayerVisible(id: string, visible: boolean): void;
  /** 0..1; 1 is fully opaque. */
  setImageryLayerOpacity(id: string, opacity: number): void;
}

// ---- camera -----------------------------------------------------------------

export interface Scene3DCameraOptions {
  /** Animate the move instead of jumping. Defaults to false. */
  animate?: boolean;
}

/**
 * How the scene projects the world onto the screen.
 *
 * `perspective` is the ordinary one: things further away are drawn smaller, which is how a view
 * of a landscape is read. `orthographic` removes that convergence, so two passages the same width
 * are drawn the same width however far apart they are — which is what makes a plan or an elevation
 * of a survey measurable off the screen, and is why a cave viewer needs both.
 */
export type Scene3DProjection = 'perspective' | 'orthographic';

export interface Scene3DCamera {
  getCamera(): Scene3DCameraState;
  setCamera(state: Scene3DCameraState, options?: Scene3DCameraOptions): void;
  /**
   * The ground the middle of the screen is showing, or undefined when the camera is not pointed
   * at the globe at all.
   *
   * It is the pivot a camera preset turns around and the point a saved view is really about — a
   * view is remembered as "this place, from this direction", and only the place is stable when the
   * window it is reopened in is a different shape. Computing it outside the engine would mean
   * knowing the size of the drawing surface, which is the engine's to know.
   */
  getCameraTarget(): Scene3DPosition | undefined;
  /** Which projection the scene is drawing with. */
  getProjection(): Scene3DProjection;
  /**
   * Switches projection. `halfWidthMeters` sets how much ground an orthographic view spans from
   * the middle of the screen to its edge; omitted, the scene keeps the framing the perspective
   * camera had. It is ignored for `perspective`, which is framed by distance alone.
   *
   * Asking for a width may move the camera. How wide an orthographic view is and how far back it
   * stands are one fact and not two — a renderer is free to derive either from the other, and the
   * one behind this contract derives the width from the distance, recomputing it on every camera
   * move — so a width that is meant to outlive the next move has to be expressed as the distance
   * it comes from. Restoring a camera this application wrote down moves nothing, because the width
   * it carries was read off that same camera.
   */
  setProjection(projection: Scene3DProjection, halfWidthMeters?: number): void;
  /**
   * Half the ground an orthographic view spans horizontally, in metres, or undefined under a
   * perspective projection where the question has no answer.
   */
  getOrthoHalfWidth(): number | undefined;
  /**
   * How high above the ellipsoid this camera has to be to show the same ground detail a 2D map at
   * `zoom` would, at the given latitude.
   *
   * The answer depends on the projection in force: under an orthographic one a camera's distance
   * is what sets how much ground is on screen, so the same zoom asks for a different height.
   *
   * The inverse of `getPseudoZoom`, and the one part of "put this box on the screen" that depends
   * on the frustum and on the size of the drawing surface. Exposing it is what lets a caller frame
   * a box without also handing the engine its own idea of where the camera should end up pointing.
   */
  cameraHeightForZoom(zoom: number, latitude: number): number;
  /**
   * Places the camera looking straight down at a point, framed so that the ground covers the same
   * area a 2D map at `zoom` would. This is the currency the two views share: the map endpoints are
   * keyed by an integer zoom, and a camera has none of its own.
   */
  flyToZoom(longitude: number, latitude: number, zoom: number, options?: Scene3DCameraOptions): void;
  /** Frames a longitude/latitude box; a degenerate (point) box gets a sane close-up instead. */
  fitBounds(bounds: Scene3DBounds, options?: Scene3DCameraOptions): void;
  /**
   * The fractional map zoom currently showing the same amount of ground as this camera — what a
   * caller rounds and sends to an endpoint that only speaks in zoom levels.
   */
  getPseudoZoom(): number;
  /**
   * The longitude/latitude box a data loader should request for the current view, or undefined
   * when the camera is not looking at the globe at all.
   *
   * This is deliberately "what to ask the server for" rather than "exactly what is on screen":
   * a camera pointed near the horizon technically sees ground all the way to the limb, and
   * requesting that box at a close-up zoom would ask for a country's worth of survey geometry.
   * The box is therefore sized from the ground the middle of the screen is showing.
   */
  getVisibleBounds(): Scene3DBounds | undefined;
  /**
   * Fires once the camera has finished moving, which is when a bbox-driven loader should refetch.
   * Returns an unsubscribe function. It is the counterpart of the 2D map's move-end event and
   * carries no payload for the same reason: the listener reads the camera it wants to read.
   *
   * It reports user navigation and animated flights. A caller that jumps the camera itself
   * already knows it moved and can reload directly, so no event is synthesised for that.
   */
  onViewChanged(listener: () => void): () => void;
  /**
   * How deep the camera may descend, in metres above the ellipsoid.
   *
   * The camera is allowed below the surface — a cave view that cannot go underground is not a
   * cave view — and the moment that is allowed, the engine's own "stop at the ground" behaviour
   * stops applying and nothing at all limits the descent: a viewer who keeps zooming ends up at
   * the centre of the earth with the whole world behind them. This is that limit, and it is the
   * caller's to set because only the caller knows how deep the data being shown goes.
   *
   * An implementation may lower a floor it is given but must never raise one above its own safe
   * default, so a caller that guesses badly cannot lock a viewer out of a cave.
   */
  setCameraFloorHeight(height: number): void;
}

// ---- coordinates ------------------------------------------------------------

export interface Scene3DCoordinates {
  /**
   * Screen pixel for a position, measured from the top left of the drawing surface.
   *
   * Undefined means the question has no answer at all: the position is behind the camera. That
   * must hold under every projection — a renderer whose box frustum places a point from where it
   * is sideways alone will happily answer with an ordinary-looking pixel, usually near the middle
   * of the view, for something the viewer has already descended past, and chrome placed from it
   * would name a cave that is nowhere on the screen.
   *
   * It does **not** mean "not visible". A point off the sides of the screen projects to a pixel
   * outside the surface rather than to nothing, and a point on the far side of the globe projects
   * to an ordinary pixel because no occlusion is tested — which is right rather than a
   * shortcoming, since the cave data is drawn without depth testing too, so a marker there is
   * drawn and a label naming it agrees with what is on the screen. Anything placing chrome from
   * this still has to decide for itself what is within the surface.
   */
  positionToScreen(position: Scene3DPosition): Scene3DScreenPosition | undefined;
  /** Where a screen pixel meets the ground, or undefined when it points at the sky. */
  screenToPosition(screen: Scene3DScreenPosition): Scene3DPosition | undefined;
}

// ---- picking ----------------------------------------------------------------

/**
 * What a click or hover found. `id` is the payload the caller attached to the item when it was
 * added to a source, handed back by reference and untouched by the engine; `position` is where the
 * ray met the ground, present when nothing was hit or when the hit sits on the surface.
 *
 * A click that found only ground reports `id: undefined` with a position; a click that found
 * nothing at all — the sky above the horizon — reports `null` instead of a pick.
 */
export interface Scene3DPick {
  id: unknown;
  position?: Scene3DPosition;
  /**
   * The pixel the hit test was made at — where the pointer was, not where the thing it found is.
   *
   * A tooltip that follows the pointer needs it and cannot recover it: the item's own position
   * projects to the middle of an icon the pointer is merely somewhere within, and a hit test
   * reaches several pixels further than that again.
   */
  screen?: Scene3DScreenPosition;
}

export interface Scene3DPicking {
  /** Subscribes to clicks; the listener gets null when the click hit nothing at all. */
  onClick(listener: (pick: Scene3DPick | null) => void): () => void;
  /**
   * Subscribes to hover. Implementations must throttle to at most one hit test per drawn frame:
   * an unthrottled hit test on every pointer move costs more than a frame's whole budget.
   */
  onHover(listener: (pick: Scene3DPick | null) => void): () => void;
}

// ---- vector sources ----------------------------------------------------------

/**
 * A screen-aligned icon on the globe. There is deliberately no colour, outline or label field:
 * a marker is an image, and everything a caller wants to vary about how one looks — fill, ring,
 * a count printed inside a bubble — is decided by producing a different image URL. That keeps the
 * styling vocabulary out of the engine contract, where a second implementation would have to
 * reproduce it exactly, and in engine-free code that can be unit-tested character by character.
 *
 * Implementations must make markers hittable even when the ground in front of them is nearer to
 * the camera than they are: a marker standing in a valley is drawn but, without that, cannot be
 * clicked, which reads as an unresponsive map rather than as a depth problem.
 */
export interface Scene3DMarker {
  position: Scene3DPosition;
  /** Drops the marker onto the terrain surface, ignoring `position.height`. */
  clampToGround?: boolean;
  /** URL of the icon image. */
  image: string;
  scale?: number;
  /** Round-tripped by reference to `Scene3DPick.id`. */
  id: unknown;
}

/**
 * A line through a list of positions, drawn as straight segments between them. Straight is the
 * only option on purpose: the positions are survey legs of a few metres each, and bending them
 * along a constant compass bearing — which is what a mapping library does by default — would
 * subdivide every one of tens of thousands of legs into vertices that describe nothing.
 */
export interface Scene3DPolyline {
  positions: Scene3DPosition[];
  widthPixels: number;
  /** CSS colour string. */
  color: string;
  /**
   * Lays the line on the ground along its whole length, ignoring `position.height`.
   *
   * This is the line equivalent of a marker dropped onto the terrain, and it exists for the same
   * reason: some geometry describes a place on the surface rather than a height above it — the
   * outline of a karst area, the plan of a cave whose depths were never surveyed — and a height of
   * its own is exactly what it does not have. While the globe is the bare ellipsoid the two are
   * the same thing, because the ellipsoid IS the ground and these positions are already on it;
   * with an elevation model loaded they are a whole hillside apart, which in the Carpathian karst
   * this application is for is around eleven hundred metres.
   *
   * An implementation that cannot drape a line on this browser is expected to fall back to drawing
   * it at the positions given rather than to drop it: that is the bare-ellipsoid rendering, which
   * is exactly right until an elevation model is attached and legible even then.
   *
   * Where the ground has been cut away, a line that lies on it has nothing left to lie on and is
   * not drawn across the opening. That is the honest consequence of removing the ground rather
   * than something to work around.
   */
  clampToGround?: boolean;
  /** Round-tripped by reference to `Scene3DPick.id`. */
  id: unknown;
}

/**
 * A named, replaceable batch of like items — one GPU batch, not one object per item.
 *
 * Every method here changes what should be on screen, so every one of them also asks the scene to
 * draw a frame. That is the implementation's job rather than the caller's: the scene draws only on
 * demand, and a source that left the redraw to whoever mutated it would work perfectly whenever
 * the camera happened to be moving and appear to do nothing whenever it was not.
 */
export interface Scene3DVectorSource<TItem> {
  replace(items: readonly TItem[]): void;
  clear(): void;
  setVisible(visible: boolean): void;
  /**
   * Fades the whole batch, 0..1, 1 being fully opaque. It multiplies whatever transparency the
   * items carry themselves rather than replacing it, and it survives `replace`: a source faded
   * to a quarter and then reloaded is still faded to a quarter, because the viewer set that and
   * the camera moving is not a reason to undo it.
   */
  setOpacity(opacity: number): void;
  /** Removes the source from the scene; the handle is unusable afterwards. */
  remove(): void;
}

export interface Scene3DVectorSources {
  createMarkerSource(id: string): Scene3DVectorSource<Scene3DMarker>;
  createPolylineSource(id: string): Scene3DVectorSource<Scene3DPolyline>;
}

// ---- the ground surface -------------------------------------------------------

/**
 * How the ground is drawn in relation to the cave underneath it.
 *
 * `overlay` draws the survey over the ground: the whole cave is legible from every camera angle
 * and the basemap stays whole. It is the default, and it is honest about what it is — an X-ray
 * view with no depth cue at all, in which a passage under four hundred metres of rock looks
 * exactly like one under ten.
 *
 * `cutaway` removes the ground over the cave instead, so the survey is seen down a shaft cut into
 * the surface. That reads as depth, and it costs: the shaft is vertical, so a camera looking along
 * the ground rather than down into it sees only the shaft's near wall and almost none of the cave.
 * An implementation is therefore expected to keep drawing the overlay whenever the cutaway would
 * not show the cave, and to report that through `Scene3DSurfaceState.effective` so the chrome can
 * say why the viewer is not seeing what they asked for.
 */
export type Scene3DSurfaceMode = 'overlay' | 'cutaway';

/** The patch of ground a cutaway is cut out of, and how far down the excavation goes. */
export interface Scene3DCutawayFootprint {
  /**
   * The outline, in order, first point not repeated at the end. Heights are ignored: the cut is
   * a vertical shaft through the ground wherever the outline encloses, at every altitude.
   */
  ring: Scene3DPosition[];
  /** Metres above the ellipsoid for the bottom of the excavation — below the deepest passage. */
  floorHeight: number;
}

/**
 * Why a cutaway that was asked for, is possible and has ground to cut is still not on screen.
 *
 * The two reasons need different words, because they need different actions from the viewer and
 * the advice for one is wrong for the other. `angle` means the camera is too near the horizon and
 * is looking at the near wall of the opening rather than into it, which tilting down fixes.
 * `belowSurface` means the camera is under the ground, where there is no ground left between it
 * and the cave to remove — no tilt in any direction brings the cutaway back, only climbing above
 * the surface again.
 */
export type Scene3DCutawayPause = 'angle' | 'belowSurface';

export interface Scene3DSurfaceState {
  /** What was asked for. */
  requested: Scene3DSurfaceMode;
  /** What is on screen right now, which is the overlay whenever a cutaway would not show much. */
  effective: Scene3DSurfaceMode;
  /** False when this browser or graphics driver cannot cut a hole in the ground at all. */
  cutawayAvailable: boolean;
  /** False when nothing has said which patch of ground to cut away. */
  hasFootprint: boolean;
  /**
   * Set only when a cutaway was asked for and could have been drawn, but the camera is somewhere
   * it would not show the cave. Absent whenever the effective mode is the requested one, and
   * absent when the cutaway is unavailable or has no ground to cut — those have their own fields
   * and their own explanations.
   */
  pausedBy?: Scene3DCutawayPause;
}

export interface Scene3DSurface {
  setSurfaceMode(mode: Scene3DSurfaceMode): void;
  getSurfaceState(): Scene3DSurfaceState;
  /**
   * Reports every change to the surface state, including the ones the camera causes rather than
   * the viewer. Returns an unsubscribe function.
   */
  onSurfaceStateChanged(listener: (state: Scene3DSurfaceState) => void): () => void;
  /** Sets, or with `undefined` clears, the ground a cutaway is cut out of. */
  setCutawayFootprint(footprint: Scene3DCutawayFootprint | undefined): void;
}

// ---- the ground's elevation ---------------------------------------------------

/**
 * An elevation model for the globe's surface: a pre-baked pyramid of terrain tiles served by this
 * installation, named by the URL its `layer.json` sits under.
 *
 * Deliberately not a layer in the imagery catalog. There is one ground and it has one shape, so
 * this is not something a viewer chooses between or fades: it is either configured for the
 * installation or the globe is the smooth reference ellipsoid, which is what an installation that
 * has not baked one draws.
 */
export interface Scene3DTerrainSource {
  /** Directory holding `layer.json`, with a trailing slash. */
  url: string;
  /** Credit the elevation data's licence requires; shown on the scene, not hidden behind a link. */
  attribution?: string;
}

export interface Scene3DTerrain {
  /**
   * Draws the ground from this elevation model, or with `undefined` from the reference ellipsoid.
   *
   * Resolves once the model has been read and the globe is drawing from it; rejects when it could
   * not be read at all, which the caller is expected to turn into a visible explanation rather
   * than a globe that quietly has no ground on it. Calling it again with a source already in force
   * does nothing — attaching one discards every surface tile the globe is holding.
   */
  setTerrainSource(source: Scene3DTerrainSource | undefined): Promise<void>;
  /** True while an elevation model is in force rather than the bare ellipsoid. */
  hasTerrain(): boolean;
  /**
   * Height of the drawn ground at a point, in metres above the ellipsoid, or the ellipsoid itself
   * where nothing says otherwise.
   *
   * The answer is what is on the screen now and not what the elevation model ultimately holds:
   * before the tiles covering a point have been fetched and refined, the globe answers from a
   * coarser surface than it will a second later. Callers that place something once must therefore
   * be prepared to place it again, and callers that place something on every frame get that for
   * free.
   */
  groundHeight(longitude: number, latitude: number): number;
}

// ---- models (declared, not implemented yet) ----------------------------------

export interface Scene3DModelOptions {
  url: string;
  /** Where the model's own origin sits on the globe. */
  origin: Scene3DPosition;
  headingDegrees?: number;
}

export interface Scene3DModels {
  loadModel(id: string, options: Scene3DModelOptions): Promise<void>;
  removeModel(id: string): void;
  setModelVisible(id: string, visible: boolean): void;
}

// ---- the whole surface, and the part that exists today -----------------------

/** Everything a fully featured 3D scene owes the application. */
export interface Scene3DEngine
  extends Scene3DLifecycle,
    Scene3DImagery,
    Scene3DCamera,
    Scene3DCoordinates,
    Scene3DPicking,
    Scene3DVectorSources,
    Scene3DSurface,
    Scene3DTerrain,
    Scene3DModels {}

/**
 * The part of the contract that is implemented. It is a separate name rather than a set of
 * optional members so the compiler keeps telling the truth about what a caller can rely on: code
 * written against `Scene3DCore` compiles against the real scene, and code that reaches for model
 * loading fails to compile until that group is built.
 */
export type Scene3DCore = Scene3DLifecycle &
  Scene3DImagery &
  Scene3DCamera &
  Scene3DCoordinates &
  Scene3DPicking &
  Scene3DVectorSources &
  Scene3DSurface &
  Scene3DTerrain;
