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
export const CAVEVIEW_HOME = '/caveview/v2.9.0-slx.6/';

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
  | 'liveMarkerCluster';

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
}

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
   * station would have opened. The one way to that strip that does not need a pointer that hovers.
   */
  popup?: boolean;
}

export interface CaveViewer {
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
  /** Places a marker over the model. A reference the loaded model does not hold is held
   *  unresolved and placed when a model containing it is loaded. */
  addLiveMarker(id: string, ref: CaveViewRef, options?: CaveViewLiveMarkerOptions): unknown;
  /** Slides a marker to another station. Null when no marker of that id was added. */
  moveLiveMarker(id: string, ref: CaveViewRef, options?: CaveViewLiveMarkerOptions): unknown;
  removeLiveMarker(id: string): boolean;
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

export interface Cv2Namespace {
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
