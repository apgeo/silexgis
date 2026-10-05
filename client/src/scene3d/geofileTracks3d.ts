// SPDX-License-Identifier: AGPL-3.0-or-later
import { fetchGeofileFeatureCollection, type GeofileInfo } from '../api/hooks.ts';
import { geofileLayerId, geofileStrokeColour } from '../map/geofileProperties.ts';
import { ANCHORED_TO_SURFACE, drawnAltitude, samePlacement } from './altitude3d.ts';
import type { Altitude3DPlacement } from './altitude3d.ts';
import { featuresOf, lineStrings, propertiesOf, stringProperty } from './geoJson3d.ts';
import type {
  Scene3DCamera,
  Scene3DPolyline,
  Scene3DPosition,
  Scene3DVectorSource,
  Scene3DVectorSources,
} from './scene3dEngine.ts';
import { boundsToBbox } from './viewBounds3d.ts';

// Lines from imported files — the track of a GPX, the outline of a KML area — drawn in the scene
// with the same treatment as a cave's survey lines.
//
// This is the cave loader's loop again: refetch when the camera settles, drop an answer a newer
// request has overtaken, keep the previous frame when a request fails. It is a loop of its own
// rather than a fourth batch in that loader because what it draws is chosen file by file — one
// source per file, created when the viewer turns the file on and taken out when they turn it off,
// exactly as the flat map keeps one layer per file — and because nothing the cave loader derives
// from what it draws (where the ground is cut away, how deep a camera may go) is derived from a
// track.
//
// There is deliberately no parser here and no use of an engine's own reader of these formats. The
// server already turned every file into GeoJSON with its altitudes kept, and the request this
// makes is the one the flat map makes for the same file, so the two views cannot disagree about
// what a file contains. The engine arrives as an argument, so the whole module runs against a
// plain object.

/** The parts of the engine this loader touches, named one by one for the same reason the cave loader names its own. */
export interface GeofileTracks3DEngine
  extends Pick<Scene3DCamera, 'getVisibleBounds' | 'onViewChanged'>,
    Pick<Scene3DVectorSources, 'createPolylineSource'> {}

/** What this needs to know about a file: which it is, what to draw it in, and whether it holds anything yet. */
export type GeofileTrack3DFile = Pick<GeofileInfo, 'id' | 'name' | 'style' | 'importStatus'>;

/**
 * What a line from an imported file carries into the scene, round-tripped by reference to a pick.
 *
 * It is not one of the payloads a click can act on, on purpose: a track has no page to open and
 * no selection kind to become, so picking one must read as picking nothing — the kind is what
 * makes the selection reader answer that, rather than crash on a shape it does not know.
 */
export interface GeofileTrackId {
  kind: 'geofile-track';
  geofileId: string;
  featureId?: string;
}

/** Line width in screen pixels: the stroke the flat map draws these files with, and the surveys' width here. */
const WIDTH_PIXELS = 2;

/** How long the camera has to stay put before the files are refetched; the same wait the cave data takes. */
const SETTLE_MILLISECONDS = 250;

/** What the loader is doing, for chrome that waits on the view to fill itself in. */
export interface GeofileTracks3DState {
  /** True while any file's request for the current view is still in the air. */
  loading: boolean;
}

export const EMPTY_GEOFILE_TRACKS_3D_STATE: GeofileTracks3DState = { loading: false };

export interface GeofileTracks3DHandle {
  /**
   * Which files are drawn. A file is drawn when it is in the list, among the chosen ids, and its
   * import has finished; everything else is taken out of the scene — not hidden, because a file
   * that is off is also not fetched, and the requests are the expensive part of it.
   */
  setFiles(files: readonly GeofileTrack3DFile[], visibleIds: ReadonlySet<string>): void;
  /** Fades one file, 0..1. Remembered for a file not yet drawn, so it comes up at the fade the viewer set. */
  setOpacity(geofileId: string, opacity: number): void;
  /**
   * Says whether the ground has relief, which decides where a track with altitudes is drawn.
   * Redraws what is held rather than fetching it again: nothing else is derived from a track.
   */
  setAltitudePlacement(placement: Altitude3DPlacement): void;
  /** Loads the current view now, without waiting for the camera to move. */
  reload(): void;
  getState(): GeofileTracks3DState;
  /** Subscribes to load-state changes; returns an unsubscribe function. */
  subscribe(listener: (state: GeofileTracks3DState) => void): () => void;
  /** Stops loading and takes every file out of the scene. */
  detach(): void;
}

/**
 * Whether a line carries altitudes worth drawing it at.
 *
 * A position with no third ordinate reads as height zero, and so does one whose third ordinate
 * really is zero: a file written by software that fills the field in with nothing. The two cannot
 * be told apart, and neither is an altitude — a track at exactly sea level along its whole length
 * is not something a GPS in the Carpathians records.
 */
function carriesAltitudes(components: readonly Scene3DPosition[][]): boolean {
  return components.some((positions) => positions.some((position) => position.height !== 0));
}

/** The same positions, laid on the ground: height zero, which is where the renderer is asked to put them. */
function onGround(positions: readonly Scene3DPosition[]): Scene3DPosition[] {
  return positions.map((position) => ({ ...position, height: 0 }));
}

/**
 * The polylines for one file's response, in one colour.
 *
 * Where a line goes is decided per feature, by whether it carries altitudes and whether the ground
 * has any relief to place them against:
 *
 *   * A track with no altitudes is laid on the ground, whatever the ground is. It is a surface
 *     thing with no height of its own, and drawing its zeros as heights would put it at sea level
 *     — under real relief, a whole hillside below the entrance markers of the caves it walks past,
 *     where nothing would ever be seen of it. On the ground it degrades visibly rather than
 *     silently: the viewer sees a track, and sees that it follows the hillside and not its own
 *     profile.
 *
 *   * A track with altitudes, over ground with relief, is drawn at them — through the same
 *     placement the surveys pass through, with the same correction for what this scene's ground
 *     counts as height. That is what puts a GPS track on the same ground as the caves it visits.
 *
 *   * A track with altitudes, over the bare ellipsoid, is laid on the ground too. There is no
 *     hillside to place an altitude against: drawn at 1100 m it would float a kilometre over the
 *     smooth globe, and hung from its own highest point — the way a cave is hung — it would dip
 *     below the surface by its real descent, which states that a path over a ridge goes
 *     underground. The ellipsoid is where every surface thing sits in that scene, the entrance
 *     markers and the surface features included, and a track is a surface thing.
 *
 * A file's own id and its row's id travel with every line, so a click on one can be recognised
 * as a click on nothing selectable rather than mistaken for something else.
 */
export function geofileTrackPolylines(
  collection: unknown,
  geofileId: string,
  color: string,
  placement: Altitude3DPlacement = ANCHORED_TO_SURFACE,
): Scene3DPolyline[] {
  const polylines: Scene3DPolyline[] = [];
  for (const feature of featuresOf(collection)) {
    const components = lineStrings(feature);
    if (components.length === 0) {
      continue; // A waypoint, or a row this reader does not draw as a line.
    }
    const featureId = stringProperty(propertiesOf(feature), 'id');
    // One payload shared by every component of the row, by reference, like a cave's lines.
    const id: GeofileTrackId = { kind: 'geofile-track', geofileId, ...(featureId ? { featureId } : {}) };
    const atAltitude = placement.absolute && carriesAltitudes(components);
    for (const positions of components) {
      polylines.push(
        atAltitude
          ? {
              positions: positions.map((position) => ({
                ...position,
                // A track's altitudes are its own; there is no top of a cave to hang them from,
                // so the rule is applied with nothing to anchor against and only the ground's
                // correction remains.
                height: drawnAltitude(position.height, 0, placement),
              })),
              widthPixels: WIDTH_PIXELS,
              color,
              id,
            }
          : { positions: onGround(positions), widthPixels: WIDTH_PIXELS, color, clampToGround: true, id },
      );
    }
  }
  return polylines;
}

/** One drawn file: its source in the scene and the last answer it was drawn from. */
interface FileEntry {
  file: GeofileTrack3DFile;
  source: Scene3DVectorSource<Scene3DPolyline>;
  /** Bumped per request, so an answer that arrives after a newer request went out is dropped. */
  requestSeq: number;
  /** The last response, kept so a change of colour or of placement is a redraw and not a fetch. */
  collection: unknown;
}

/**
 * Puts the chosen files' lines into the scene and keeps them there. Nothing is drawn until
 * `setFiles` names something, because which files are shown is the viewer's choice and it is
 * held outside this module — shared with the flat map, so a file turned on in one view is on in
 * the other.
 */
export function attachGeofileTracks3d(engine: GeofileTracks3DEngine): GeofileTracks3DHandle {
  const entries = new Map<string, FileEntry>();
  const opacity = new Map<string, number>();
  let placement: Altitude3DPlacement = ANCHORED_TO_SURFACE;
  let state: GeofileTracks3DState = { ...EMPTY_GEOFILE_TRACKS_3D_STATE };
  const listeners = new Set<(next: GeofileTracks3DState) => void>();
  let inFlight = 0;
  let settleTimer: number | undefined;
  let detached = false;

  const publish = (next: GeofileTracks3DState) => {
    if (next.loading === state.loading) {
      return;
    }
    state = next;
    for (const listener of [...listeners]) {
      listener(state);
    }
  };

  const draw = (entry: FileEntry) => {
    entry.source.replace(
      geofileTrackPolylines(entry.collection, entry.file.id, geofileStrokeColour(entry.file), placement),
    );
  };

  const loadFile = async (entry: FileEntry) => {
    const bounds = engine.getVisibleBounds();
    if (!bounds) {
      return; // The camera is not looking at the globe; there is no box to ask about.
    }
    const seq = ++entry.requestSeq;
    inFlight += 1;
    publish({ loading: true });
    try {
      const collection = await fetchGeofileFeatureCollection(entry.file.id, boundsToBbox(bounds));
      // Dropped when a newer request for the file went out meanwhile, and when the file was
      // turned off while this was in the air: its source is gone from the scene, and an entry
      // for the same file turned on again is a different entry with requests of its own.
      if (detached || entry.requestSeq !== seq || entries.get(entry.file.id) !== entry) {
        return;
      }
      entry.collection = collection;
      draw(entry);
    } catch {
      // Keep what is already drawn: a dropped request is usually a network hiccup, and blanking
      // a track the viewer is following would be a worse answer than showing it a moment stale.
    } finally {
      inFlight -= 1;
      if (inFlight === 0 && !detached) {
        publish({ loading: false });
      }
    }
  };

  const load = () => {
    if (detached) {
      return;
    }
    for (const entry of entries.values()) {
      void loadFile(entry);
    }
  };

  const onViewChanged = () => {
    window.clearTimeout(settleTimer);
    settleTimer = window.setTimeout(load, SETTLE_MILLISECONDS);
  };

  const unsubscribeView = engine.onViewChanged(onViewChanged);

  return {
    setFiles(files, visibleIds) {
      if (detached) {
        return;
      }
      // Only a file whose import has finished has rows to ask for; one still importing is listed
      // in the panel with its status and offering it here would be a switch that draws nothing.
      const wanted = new Map(
        files
          .filter((file) => visibleIds.has(file.id) && file.importStatus === 'imported')
          .map((file) => [file.id, file]),
      );
      for (const [id, entry] of [...entries]) {
        if (!wanted.has(id)) {
          entry.source.remove();
          entries.delete(id);
        }
      }
      for (const [id, file] of wanted) {
        const existing = entries.get(id);
        if (existing) {
          // The catalogue hands over fresh objects on every refetch; only a colour that actually
          // changed is worth a redraw, and it is a redraw of what is held rather than a fetch.
          const recolored = geofileStrokeColour(existing.file) !== geofileStrokeColour(file);
          existing.file = file;
          if (recolored && existing.collection !== undefined) {
            draw(existing);
          }
          continue;
        }
        const source = engine.createPolylineSource(geofileLayerId(id));
        source.setOpacity(opacity.get(id) ?? 1);
        const entry: FileEntry = { file, source, requestSeq: 0, collection: undefined };
        entries.set(id, entry);
        void loadFile(entry);
      }
    },
    setOpacity(geofileId, next) {
      if (opacity.get(geofileId) === next) {
        return;
      }
      opacity.set(geofileId, next);
      entries.get(geofileId)?.source.setOpacity(next);
    },
    setAltitudePlacement(next) {
      if (samePlacement(next, placement)) {
        return;
      }
      placement = next;
      for (const entry of entries.values()) {
        if (entry.collection !== undefined) {
          draw(entry);
        }
      }
    },
    reload() {
      load();
    },
    getState() {
      return state;
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    detach() {
      detached = true;
      window.clearTimeout(settleTimer);
      unsubscribeView();
      listeners.clear();
      for (const entry of entries.values()) {
        entry.source.remove();
      }
      entries.clear();
    },
  };
}
