// SPDX-License-Identifier: AGPL-3.0-or-later
import { userManager } from '../auth/auth.tsx';

// CaveView.js is not published on npm — it ships as a prebuilt browser bundle that exposes
// the `CV2` global and fetches its web workers/assets at runtime relative to the `home`
// option. The bundle vendored under public/caveview is built from this project's fork of
// the upstream repository (see the README there for provenance and the build procedure)
// and injected on demand, so the 3D viewer adds zero weight until someone opens it.

/**
 * Public base path of the vendored CaveView runtime (the viewer's `home` option).
 *
 * The path carries the fork's distribution version deliberately: the bundle URLs are
 * otherwise fixed, and browsers — and any page embedding the viewer — would keep running a
 * stale cached viewer across upgrades. A new vendored build lands in a new directory and
 * changes this constant in the same commit, so every asset URL changes with it.
 */
export const CAVEVIEW_HOME = '/caveview/v2.9.0-slx.12/';

const SCRIPT_URL = `${CAVEVIEW_HOME}js/CaveView2.min.js`;
const CSS_URL = `${CAVEVIEW_HOME}css/caveview.css`;

// Minimal hand-written surface of the CV2 global — only what the app calls.
//
// 'entrance' fires when an entrance label is clicked in the 3D scene; its event carries the
// survey's entrance label as `displayName`.
//
// 'station' and 'leg' fire when one of those is clicked. Their events carry the viewer's own
// objects — a survey-tree node under `node`, and under `leg` an object whose `start()` and
// `end()` are the tree nodes it runs between. Both also carry a `handled` flag the bundle reads
// back after dispatching: leaving it false lets the viewer do its own thing with the click as
// well, which is what keeps selecting a station for a link from also breaking selecting one to
// look at it.
//
// 'liveMarkerHover' fires when the pointer comes to rest on a marker the application placed; its
// event carries the marker's own id under `id`. It too carries `handled`, which suppresses the
// marker's second line of label — see the panel for why that line is left to the viewer.
//
// 'stationHover' fires when the pointer comes to rest over a station — and, on a pointer that
// cannot hover, when one is tapped. It too carries `handled`, which suppresses the viewer's own
// station-name label. Only dispatched while the viewer is tracking the station under the pointer,
// which is the `stationLabelOver` setting below.
export type CaveViewerEvent =
  | 'newCave'
  | 'progress'
  | 'entrance'
  | 'station'
  | 'stationHover'
  | 'leg'
  | 'liveMarkerHover'
  // What the viewer reports instead of the one above when markers share a station and are
  // drawn as one. A panel that listens for only the first is silent for a party standing
  // together, which is most of the time a party is anywhere.
  | 'liveMarkerCluster'
  // A thumbnail in a station's strip was clicked, before the viewer shows the picture itself. The
  // event carries `entry` — the very object the strip was built from — and `handled`, which a host
  // that opens its own viewer on the picture sets to suppress the viewer's in-model popup.
  | 'mediaOpen';

/**
 * How a station or a named part of a survey is addressed: the dotted path the viewer itself
 * uses, or the path already split into its components.
 *
 * The split form is the reliable one, because a dotted path is ambiguous when a name inside it
 * contains a dot of its own. Anchors written by this application store the dotted string the
 * viewer handed over and nothing else, so that is what is passed back — splitting one here would
 * invent component boundaries that were never observed.
 */
export type CaveViewRef = string | readonly string[];

/** One picture held for a station, as the viewer's media strip takes it. */
export interface CaveViewMediaEntry {
  /** Full size, shown when the thumbnail is clicked. An entry without one is ignored. */
  url: string;
  thumbnailUrl?: string;
  caption?: string;
  /**
   * Which document this picture is, for a host that opens its own viewer on it.
   *
   * <b>Carried through the viewer rather than looked up again on the way back.</b> The viewer hands
   * the clicked entry back on its `mediaOpen` event as the same object it was given, so anything
   * put here arrives with the click; the alternative is matching a URL against the map that built
   * it, which would be a second derivation of an identity already known. Ignored by the viewer
   * itself, which reads only the three fields above.
   */
  documentId?: string;
}

/**
 * Where the viewer reads a station's pictures from: a map keyed by the dotted path of a station,
 * or a function asked about each station as the pointer reaches it.
 */
export type CaveViewStationMediaSource =
  | ReadonlyMap<string, readonly CaveViewMediaEntry[]>
  | ((station: unknown) => readonly CaveViewMediaEntry[] | null);

/**
 * What a label says: one line, or the lines it is drawn on, one below the other.
 *
 * A single string is the same label it always was — the array form is an addition, not a
 * replacement — and the first line stays beside the dot however many lines follow it, so a block
 * that grows downwards never moves the name the marker is first recognised by.
 */
export type CaveViewLabelText = string | readonly string[];

/**
 * What a marker is drawn with. An option left out of a move is left as it was, so this panel
 * gives every one of them on every call rather than relying on what a marker already holds —
 * a value that can only be replaced and never cleared is a value that outlives its reason.
 */
export interface CaveViewLiveMarkerOptions {
  label?: CaveViewLabelText;
  sublabel?: CaveViewLabelText;
  color?: string;
  /**
   * How long this one move takes, in milliseconds, in place of `liveMarkerMoveTime`; 0 places the
   * marker without moving it. Only a move reads it. A value that is not a finite number of at
   * least 0 is ignored and the viewer's own move time applies.
   */
  duration?: number;
}

/** What a capture session draws: its frame size, and what the frames are drawn on. */
export interface CaveViewCaptureOptions {
  /**
   * Frame size in pixels, each an even whole number of at least 16 and no larger than the
   * renderer can draw. The container must already have the frame's shape, to within its width and
   * height each being rounded to a whole page pixel (half a pixel on each side): the viewer refuses
   * a container of another shape. A container restyled to that shape without `resize()` being
   * called is fine — the viewer resizes itself to the container as the session opens, so the
   * frames are never a stretched copy of the view it was last sized for.
   */
  width: number;
  height: number;
  /** Opaque background of the frames, as any CSS colour. Left out: the viewer's own background. */
  background?: string;
}

/** Where one captured frame looks from, how far the markers have moved, and where it is drawn. */
export interface CaveViewCaptureFrameOptions {
  /** Absolute azimuth of the camera about the view target, radians. Left out: unchanged. */
  azimuth?: number;
  /** Absolute polar angle, radians from looking straight down (0 is a plan view). Left out: unchanged. */
  polar?: number;
  /**
   * Milliseconds of marker-motion time to run before drawing. Default 0. A move of duration d
   * arrives after exactly d milliseconds of advancing, however they were divided between frames —
   * this is the markers' only clock while a session is open.
   */
  advance?: number;
  /** When given, the frame is drawn into this context, scaled to its canvas, before returning. */
  into?: CanvasRenderingContext2D;
}

export interface CaveViewCapturedFrame {
  /**
   * The viewer's own canvas, holding the frame. Its drawing buffer is not preserved, so the pixels
   * are only valid until the calling task ends: read them (or pass `into`) before yielding.
   */
  canvas: HTMLCanvasElement;
  /** The camera's azimuth and polar angle the frame was drawn from, radians. */
  azimuth: number;
  polar: number;
  /** Whether a marker is still between stations. */
  moving: boolean;
}

/** A trail's options as the viewer takes them; every one is optional on an update. */
export interface CaveViewTrailOptions {
  /** CSS colour. Default `#ffcc00`. */
  color?: string;
  /** Line width in CSS pixels. Default 4. */
  width?: number;
  /** Anything other than 'dashed' is solid. */
  style?: 'solid' | 'dashed';
  /** Default true. */
  visible?: boolean;
  /** How much of the trail is drawn, a fraction of its length, 0 to 1. Default 1. */
  progress?: number;
  /** Handed back untouched in the trail's description. */
  payload?: unknown;
}

/** A trail as the viewer describes it back. */
export interface CaveViewTrail {
  id: string;
  /** Whether every place the trail names is a station of the loaded model. */
  resolved: boolean;
  /**
   * Each place named, with its distance along the trail in metres — null where the place is not a
   * station of the loaded model.
   */
  points: readonly { ref: CaveViewRef; resolved: boolean; atLength: number | null }[];
  /** Where the trail could not be routed between two consecutive places, drawn as a gap. */
  gaps: readonly unknown[];
  /** The routed length, metres. */
  lengthM: number;
  progress: number;
  visible: boolean;
  payload?: unknown;
}

/**
 * The layers of the model the viewer switches on and off. Each is a boolean property of the same
 * name, with a `has<Name>` getter saying whether the loaded model has anything on that layer — the
 * name's first letter upper-cased and the rest left alone, so `hasEntrance_dots`. The HUD, the fog
 * and the terrain are switched the same way but are not layers of the model, and have no such
 * getter (the terrain's is `hasTerrain`).
 */
export type CaveViewModelLayer =
  | 'legs'
  | 'stations'
  | 'stationLabels'
  | 'stationComments'
  | 'entrances'
  | 'entrance_dots'
  | 'splays'
  | 'walls'
  | 'scraps'
  | 'model'
  | 'duplicateLegs'
  | 'surfaceLegs'
  | 'traces'
  | 'warnings'
  | 'box'
  | 'grid';

/** The `has<Name>` getter of each model layer, as the viewer spells it. */
export type CaveViewLayerGetter = `has${Capitalize<CaveViewModelLayer>}`;

/** The model layers as boolean properties of the viewer, with their `has*` getters. */
export type CaveViewLayers = Record<CaveViewModelLayer, boolean> &
  Readonly<Record<CaveViewLayerGetter, boolean>>;

/**
 * One marker as the viewer describes it back — the form a cluster label is asked about.
 *
 * Every field is a copy except the payload, which is handed back untouched.
 */
export interface CaveViewLiveMarker {
  id: string;
  ref: CaveViewRef;
  label: CaveViewLabelText;
  sublabel?: CaveViewLabelText;
  color?: string;
  payload?: unknown;
  /** Whether the loaded model holds the station named. An unresolved marker is drawn nowhere. */
  resolved: boolean;
}

/** What a focus does besides moving the camera. */
export interface CaveViewFocusOptions {
  /** Marks the station as the selected one. On by default. */
  highlight?: boolean;
  /**
   * Shows the station's own popup, and with it the strip of pictures a pointer resting on the
   * station would have opened.
   *
   * <b>Not what a tap needs, which is the correction this comment carries.</b> It used to say this
   * was the one way to that strip without a pointer that can hover, and a tap was answered by
   * focusing the station to reach it — which flies the camera. The viewer reports a tapped station
   * exactly as it reports a hovered one and draws the strip where the station stands, so this
   * option is for showing a strip at a station nobody pointed at: one arrived at from a list or a
   * link, where the camera is moving anyway.
   */
  popup?: boolean;
}

export interface CaveViewer extends CaveViewLayers {
  addEventListener(type: CaveViewerEvent, listener: (event: unknown) => void): void;
  removeEventListener(type: CaveViewerEvent, listener: (event: unknown) => void): void;
  /**
   * Whether the viewer labels the station under the pointer — and, with it, whether it tracks that
   * station at all, which is what the pictures of a station are shown from.
   *
   * <b>Assigned after a model is loaded, never asked for in the construction config.</b> The
   * viewer builds its view settings as its own defaults, then the config, then whatever was last
   * stored by its "save as default" button — so the stored value wins over the config, and it is
   * re-applied on every load. One press of that button by anybody, while the label was off, would
   * otherwise pin this off for that browser for good and take the station pictures with it.
   */
  stationLabelOver: boolean;
  /**
   * Selects a station, highlights it and flies the camera to it. Rejects when no model is loaded,
   * when the reference names no station of the loaded one, and when the move is abandoned —
   * superseded by a later focus, or cancelled by a selection made elsewhere.
   */
  focusStation(ref: CaveViewRef, options?: CaveViewFocusOptions): Promise<unknown>;
  /** Selects a named part of the survey and frames it. Rejects on the same conditions. */
  focusSurvey(ref: CaveViewRef): Promise<void>;
  /**
   * Marks a station as the selected one without moving the camera. Answers the station, or null
   * when no model is loaded or the reference names none of its stations — so unlike a focus this
   * never rejects, and a caller that only wants a mark has nothing to catch.
   */
  highlightStation(ref: CaveViewRef): unknown;
  /** Takes off the mark either of the two above put on. Safe with nothing marked. */
  clearHighlight(): void;
  /**
   * Places a marker over the model. A reference the loaded model does not hold is held
   * unresolved and placed when a model containing it is loaded.
   *
   * <b>The answer says whether it went anywhere, and it is not decoration.</b> The marker comes
   * back as the viewer now holds it, `resolved` and all — so a station the loaded model has no
   * node for is reported here, at the moment of asking, and a caller that throws this away has no
   * other way of learning it. Null only for a marker asked for with no id at all.
   */
  addLiveMarker(
    id: string,
    ref: CaveViewRef,
    options?: CaveViewLiveMarkerOptions,
  ): CaveViewLiveMarker | null;
  /**
   * Slides a marker to another station, answering it as the viewer now holds it — the same
   * `resolved` an add answers, about the station it has just been slid to. Null when no marker of
   * that id was added.
   */
  moveLiveMarker(
    id: string,
    ref: CaveViewRef,
    options?: CaveViewLiveMarkerOptions,
  ): CaveViewLiveMarker | null;
  removeLiveMarker(id: string): boolean;
  /**
   * Every marker the viewer is holding, in the order they were added.
   *
   * <b>The one way to ask about markers nothing has just touched.</b> An add and a move each
   * answer for the marker they acted on; the viewer re-resolves <em>all</em> of them whenever a
   * survey is loaded, so a marker placed before that and left alone can change its answer with
   * nothing having called anything. This is that answer, for all of them at once, and each marker
   * is a copy rather than the viewer's own object.
   */
  getLiveMarkers(): readonly CaveViewLiveMarker[];
  /**
   * What the single marker drawn in place of several at one station says. The viewer knows only
   * how many they are, so without this a party standing together is labelled with its count.
   *
   * <b>The viewer asks rather than being told, and it asks only when what it draws has moved.</b>
   * The function is called again each time markers are added, slid or removed, and at no other
   * moment — so an answer that changed for the caller's own reasons, a team renamed or a member
   * moved to another team, reaches nothing until something moves. What re-asks it is setting a
   * function again: the collapsed markers already displayed are built again, and only those whose
   * text actually changed. A caller whose label reads from anything but the markers themselves
   * therefore has to set it again when that thing changes.
   *
   * Answering null leaves the count in place, which is also what a function that throws leaves:
   * a group that cannot be named is still drawn.
   */
  setLiveMarkerClusterLabel(
    label: ((markers: readonly CaveViewLiveMarker[]) => CaveViewLabelText | null) | null,
  ): void;
  /**
   * Whether the markers carry their labels at all. On by default.
   *
   * Markers stay drawn and stay pointable with this off — the labels are what go, which is how a
   * screen a party has crowded is cleared without taking anybody off the model. It suppresses the
   * hover sublabel with them.
   *
   * <b>Not part of the view state the viewer saves, and it has to be reapplied by whoever wants it
   * remembered.</b> It is a property of the markers rather than of the view, so a viewer built for
   * a newly loaded survey file starts with its labels on however this was left on the last one.
   */
  liveMarkerLabels: boolean;
  /** Pictures shown over the model for the station under the pointer. */
  setStationMedia(source: CaveViewStationMediaSource): void;
  clearStationMedia(): void;
  /**
   * Visits every station of the loaded survey, handing each over as the same public
   * station object a click reports — so `pathOf` reads its name the same way. Only
   * meaningful once a survey is loaded; optional because it is the newest addition to
   * the vendored bundle and a build without it should cost an empty index, not a crash.
   */
  forEachStation?(visit: (station: unknown) => void): void;

  /**
   * Opens a capture session: from here until `endCapture()` nothing is drawn except by
   * `captureFrame()`, pointer and keyboard input move nothing, an auto rotation is suspended and a
   * camera move in flight ends where it was going, and the markers move only by each frame's
   * `advance`; setting `azimuthAngle` or `polarAngle` (animated turns) does nothing, and a station
   * the pointer was over is let go. A frame is what the container shows, rendered at `width`×`height` with everything
   * sized in pixels scaled by `width / container.clientWidth`, as on a display of that pixel ratio.
   * Answers the size of the drawing buffer, which is the frame's. Throws with no model loaded, with
   * a session already open, after dispose, on a size that is odd, under 16 or over what the renderer
   * can draw, and on a container of another shape.
   */
  beginCapture(options: CaveViewCaptureOptions): {
    width: number;
    height: number;
  };
  /**
   * Draws one frame synchronously, after advancing the markers and setting the camera as asked.
   * The same state gives the same pixels. Throws outside a session.
   */
  captureFrame(options?: CaveViewCaptureFrameOptions): CaveViewCapturedFrame;
  /**
   * Ends the session and puts back exactly what it changed — size, pixel ratio, clear colour and
   * alpha, controls, auto rotation, the marker clock, and everything sized to the view — then
   * draws the view. Idempotent, and safe after dispose.
   */
  endCapture(): void;
  /** Whether a capture session is open. */
  readonly capturing: boolean;
  /**
   * Sizes the drawing surface to its container as it is now — what a window `resize` event does,
   * for this viewer alone. A container restyled without it keeps drawing at its old size, stretched.
   * During a capture session the resize is carried out when the session ends.
   */
  resize(): void;
  /**
   * The camera's azimuth about the view target (radians, -π to π) and polar angle from plan, worked
   * out from where the camera is now — so right after a move animated to one of the toolbar's views,
   * a station or the model's first view. Reading them changes and draws nothing.
   */
  getCameraAngles(): { azimuth: number; polar: number };
  /**
   * Turns the camera to these angles at once, with no animation, cancelling a move in flight; an
   * angle left out stays. Draws the view, except in a capture session, where the next frame does.
   * Throws on an angle that is not a finite number.
   */
  setCameraAngles(angles: { azimuth?: number; polar?: number }): void;
  /** Whether the camera turns about the view target by itself. Suspended in a capture session. */
  autoRotate: boolean;
  /** Speed of the auto rotation, -1 to 1 (negative turns the other way). */
  autoRotateSpeed: number;
  /**
   * Size of the markers' label text in device pixels of whatever is drawn — the screen, or a
   * capture's frame. The default is a number, never null: 12 page pixels at the display's pixel
   * ratio as the viewer was built (at most 45), and — while no size has been set — 12 page pixels at
   * the capture's density during a session, given back as it was when the session ends. Null asks
   * for the model's own station-label size (the theme's, 18 screen pixels unless it says otherwise),
   * drawn from the material the model's labels share, which keeps its share of the view in a
   * capture. Once any size has been set, null included, the labels no longer follow a capture's
   * density, and there is no value that means "the default again": to put a size back, read this
   * property before changing it and write back what was read. The viewer refuses, with a warning, a
   * size above the glyph atlas's maximum of 45 and anything that is not a positive number or null.
   */
  liveMarkerLabelSize: number | null;
  /** Whether a plate is drawn behind each marker's label. On by default. */
  liveMarkerLabelBacking: boolean;
  /** How long a marker's move between two stations takes by default, in milliseconds. */
  liveMarkerMoveTime: number;
  /**
   * The survey's shading, one of the `SHADING_*` constants of the namespace. Undefined with no model
   * loaded. The depth shadings need real terrain.
   */
  shadingMode: number | undefined;
  /** `CAMERA_PERSPECTIVE` or `CAMERA_ORTHOGRAPHIC` (or `CAMERA_ANAGLYPH`). */
  cameraType: number;
  /**
   * Turns the camera to look at the whole model from above (`VIEW_PLAN`) or level, facing north,
   * south, east or west (`VIEW_ELEVATION_N`, `_S`, `_E`, `_W` — the camera of the north elevation
   * stands to the south of the model), framing the model as it goes.
   *
   * <b>The turn is animated, and the viewer ignores a turn asked for while another is running.</b> A
   * caller settles any move under way first (`setCameraAngles({})` brings it to its end), and reads
   * the camera's angles only after that — read mid-turn they are neither view.
   *
   * <b>Written, never read.</b> The viewer answers `VIEW_PLAN` whatever the camera is doing, so a
   * caller that compares before writing would write on every comparison — and each write reframes
   * the model, throwing away any turning or zooming done since.
   */
  view: number;
  /** Width of the survey's lines, 0 upwards; 0 is the thinnest. */
  linewidth: number;
  /** Vertical exaggeration of the model. Undefined with no model loaded. */
  zScale: number | undefined;
  /** The heads-up display: the compass, the angle of view, the scale and the colour key. */
  HUD: boolean;
  fog: boolean;
  /** Whether the terrain is shown. */
  terrain: boolean;
  /** Whether the model has terrain at all, and whether it is real rather than a flat plane. */
  readonly hasTerrain: boolean;
  /** Null, not false, when the model has no terrain at all: test it for truth, not `=== false`. */
  readonly hasRealTerrain: boolean | null;

  /**
   * Draws the way somebody went, routed along the survey between the stations named, and answers
   * it as the viewer now holds it (null with no id). A trail is kept across model loads and drawn
   * wherever the loaded model holds its stations.
   */
  addTrail(
    id: string,
    refs: readonly CaveViewRef[],
    options?: CaveViewTrailOptions,
  ): CaveViewTrail | null;
  /** Changes a trail; null refs change only the options and route nothing again. Null for an unknown id. */
  updateTrail(
    id: string,
    refs: readonly CaveViewRef[] | null,
    options?: CaveViewTrailOptions,
  ): CaveViewTrail | null;
  /** How much of the trail is drawn, as a fraction of its length. Null for an unknown id. */
  setTrailProgress(id: string, value: number): CaveViewTrail | null;
  removeTrail(id: string): boolean;
  clearTrails(): void;
  getTrails(): CaveViewTrail[];
}

/**
 * Whether a rejected focus means this model does not hold what was asked for.
 *
 * A focus rejects for two quite different reasons. The reference names nothing in the loaded model
 * — which is a real state a reader who followed a link is owed an answer about, and what a survey
 * re-exported with renamed sections leaves behind. Or the move was abandoned: superseded by a later
 * focus, which is what following two links in quick succession *is*, or cancelled by a selection
 * made in the viewer while the camera flew. Both of those leave the camera where whoever was
 * driving it wanted it and must pass in silence.
 *
 * Both arrive as an `Error` and only the message tells them apart, so <b>the failures are what is
 * matched, not the abandonments</b>. That decides which way an unrecognised message falls: anything
 * this does not recognise is treated as an abandonment and says nothing, so a reworded or wrapped
 * message in a later vendored build costs a notice that was not shown — rather than a notice
 * telling a reader that a link which worked perfectly points at nothing.
 */
export function focusNamedNothing(error: unknown): boolean {
  const message = error instanceof Error ? error.message : '';
  return /^No (station|survey section) \[/.test(message) || message === 'No survey loaded';
}

export interface CaveViewUi {
  /** A File's name extension (.lox / .3d) selects the parser; string values are URLs. */
  loadCave(file: File | string, section?: string): void;
  /** Tears down the UI, the viewer, its workers and WebGL resources. */
  dispose(): void;
}

/** Which of the viewer's own controls a toolbar offers, and which edge it sits against. */
export interface CaveViewToolbarOptions {
  placement?: 'top' | 'bottom';
  buttons?: readonly string[];
}

export interface CaveViewToolbar {
  /** Takes the toolbar off its container. Also removed when the viewer is disposed. */
  dispose(): void;
}

/** The shading modes the viewer's `shadingMode` takes, as the namespace names them. */
export interface CaveViewShadingConstants {
  SHADING_HEIGHT: number;
  SHADING_LENGTH: number;
  SHADING_INCLINATION: number;
  SHADING_CURSOR: number;
  SHADING_SINGLE: number;
  SHADING_SURVEY: number;
  SHADING_OVERLAY: number;
  SHADING_SHADED: number;
  SHADING_RELIEF: number;
  SHADING_DEPTH: number;
  SHADING_PATH: number;
  SHADING_DEPTH_CURSOR: number;
  SHADING_DISTANCE: number;
  SHADING_CONTOURS: number;
  SHADING_SURFACE: number;
  SHADING_DUPLICATE: number;
  SHADING_CUSTOM: number;
}

/** The camera kinds the viewer's `cameraType` takes. */
export interface CaveViewCameraConstants {
  CAMERA_ORTHOGRAPHIC: number;
  CAMERA_PERSPECTIVE: number;
  CAMERA_ANAGLYPH: number;
}

/** The views the viewer's `view` takes: from above, or level facing north, south, east or west. */
export interface CaveViewViewConstants {
  VIEW_PLAN: number;
  VIEW_ELEVATION_N: number;
  VIEW_ELEVATION_S: number;
  VIEW_ELEVATION_E: number;
  VIEW_ELEVATION_W: number;
}

export interface Cv2Namespace extends CaveViewShadingConstants, CaveViewCameraConstants, CaveViewViewConstants {
  CaveViewer: new (containerId: string, config: Record<string, unknown>) => CaveViewer;
  CaveViewUI: new (viewer: CaveViewer) => CaveViewUi;
  CaveViewToolbar: new (
    viewer: CaveViewer,
    container: string | HTMLElement,
    options?: CaveViewToolbarOptions,
  ) => CaveViewToolbar;
}

declare global {
  interface Window {
    CV2?: Cv2Namespace;
  }
}

let pending: Promise<Cv2Namespace> | null = null;

/** Injects the vendored CaveView stylesheet + script once and resolves the CV2 global. */
export function loadCaveView(): Promise<Cv2Namespace> {
  pending ??= new Promise<Cv2Namespace>((resolve, reject) => {
    if (window.CV2) {
      resolve(window.CV2);
      return;
    }

    if (!document.querySelector(`link[href="${CSS_URL}"]`)) {
      const link = document.createElement('link');
      link.rel = 'stylesheet';
      link.href = CSS_URL;
      document.head.appendChild(link);
    }

    const script = document.createElement('script');
    script.src = SCRIPT_URL;
    script.onload = () => {
      if (window.CV2) resolve(window.CV2);
      else reject(new Error('CaveView bundle loaded but CV2 global is missing'));
    };
    script.onerror = () => {
      script.remove();
      // Allow a retry on the next call instead of caching the failure forever.
      pending = null;
      reject(new Error('failed to load the CaveView bundle'));
    };
    document.head.appendChild(script);
  });
  return pending;
}

// ---- CRS lookup ----
//
// A Survex .3d file names its coordinate system in the header. Stock CaveView resolves that
// name by fetching `https://epsg.io/{code}.proj4` while parsing — an uncontrolled outbound
// request that tells a third party which surveys are being opened, and a dead end on an
// installation with no route out (Romanian Stereo70, EPSG:31700, is not among the bundle's
// built-in systems). The vendored fork adds a `crsLookup` viewer option for exactly this
// case; the function below is the value the app supplies for it, resolving codes against
// this installation's own offline registry instead.

/**
 * A `crsLookup` value for the viewer config: resolves a numeric EPSG/ESRI code to a PROJ.4
 * definition via this installation's own endpoint, with the caller's bearer token when
 * signed in. Answers null when the code is unknown or the request fails — the viewer then
 * falls back to its defaultCRS handling, exactly as an epsg.io miss would, and the survey
 * loads unreferenced rather than not at all.
 */
export function makeCrsLookup(): (code: string) => Promise<string | null> {
  return async (code: string) => {
    try {
      const user = await userManager.getUser();
      const response = await fetch(`/api/v1/crs/${encodeURIComponent(code)}.proj4`, {
        headers: user?.access_token ? { Authorization: `Bearer ${user.access_token}` } : undefined,
      });
      return response.ok ? await response.text() : null;
    } catch {
      return null;
    }
  };
}
