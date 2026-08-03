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
// picking, vector sources and the ground surface); model loading is declared here and is not
// implemented yet, so no code claims to provide it.

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

export interface Scene3DCamera {
  getCamera(): Scene3DCameraState;
  setCamera(state: Scene3DCameraState, options?: Scene3DCameraOptions): void;
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
  /** Screen pixel for a position, or undefined when it is behind the camera or off-screen. */
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
  Scene3DSurface;
