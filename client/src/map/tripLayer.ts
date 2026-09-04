// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type { FeatureLike } from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, RegularShape, Stroke, Style } from 'ol/style';
import { fetchTripLogFeatures, type TripLogFeatureCollection } from '../api/hooks.ts';
import { declutterOption } from './declutter.ts';
import { tripPalette as palette } from './markerPalette.ts';

export const TRIP_LAYER_ID = 'trips';

const source = new VectorSource();
const format = new GeoJSON();

/** Where the trip worked: the shape it drew of itself, filled so it reads as the solid statement. */
const sketchStyle = new Style({
  image: new CircleStyle({
    radius: 6,
    fill: new Fill({ color: palette.sketch }),
    stroke: new Stroke({ color: palette.stroke, width: 2 }),
  }),
  stroke: new Stroke({ color: palette.sketch, width: 3 }),
  fill: new Fill({ color: palette.sketchFill }),
});

/** Where its party met: hollow, so it cannot be mistaken for where the party actually went. */
const meetingStyle = new Style({
  image: new CircleStyle({
    radius: 6,
    fill: new Fill({ color: palette.meetingCentre }),
    stroke: new Stroke({ color: palette.meeting, width: 3 }),
  }),
});

/**
 * A position inherited from a cave the trip names, for a trip that stated none of its own — which
 * is most of a club's historical archive, because a spreadsheet records which cave and not where.
 *
 * Square, and in a colour neither of the other two uses, because it is a different kind of claim:
 * the coordinate belongs to the cave, not to the trip, and a reader must be able to see that
 * without being told.
 */
const derivedStyle = new Style({
  image: new RegularShape({
    points: 4,
    radius: 6,
    angle: Math.PI / 4,
    fill: new Fill({ color: palette.derived }),
    stroke: new Stroke({ color: palette.stroke, width: 2 }),
  }),
});

/**
 * Which of the three a shape is, read from the shape's own word for itself and never guessed from
 * the geometry. The answer labels every feature for exactly this reason: a meeting point and a
 * sketch are both often a single point, so geometry cannot tell them apart, and a map that drew
 * them alike would put a car park where the reader read a cave.
 *
 * An unrecognised word falls to the sketch style rather than to nothing at all: a dot in the wrong
 * shade is a defect somebody can see, and a silently undrawn trip is not.
 */
export function tripStyleFor(feature: Pick<FeatureLike, 'get'>): Style {
  const kind = feature.get('kind');
  if (kind === 'meeting') {
    return meetingStyle;
  }
  if (kind === 'cave') {
    return derivedStyle;
  }
  return sketchStyle;
}

export function createTripLayer(): VectorLayer {
  // Stacking comes from the overlay group's collection order, not a fixed zIndex.
  const layer = new VectorLayer({ source, style: tripStyleFor, declutter: declutterOption() });
  layer.set('id', TRIP_LAYER_ID);
  layer.setVisible(false); // opt-in overlay
  return layer;
}

/** The live trip source, so a test or a pop-out can read what is currently drawn. */
export function getTripSource(): VectorSource {
  return source;
}

// ---------------------------------------------------------------------------
// The narrowings the layer asks with
// ---------------------------------------------------------------------------

/**
 * What the layer is currently asking for, in the same spelling the trip listing uses so a filter
 * carried from the list to the map survives the journey unchanged.
 *
 * Held as module state and read inside the loader on every fetch, rather than passed in as a
 * parameter: the loader also runs on `moveend`, from a map that knows nothing about filters, and
 * a panned map must ask the same question the panel last set.
 */
export interface TripLayerFilter {
  /** Inclusive day bounds; an absent bound is no bound rather than today. */
  from?: string;
  to?: string;
  types?: string[];
  states?: string[];
  visibilities?: string[];
  /** Undefined is no opinion, which is not the same as "nothing went wrong". */
  hadIncident?: boolean;
}

let filter: TripLayerFilter = {};

export function getTripLayerFilter(): TripLayerFilter {
  return filter;
}

/** Sets the narrowings and re-asks straight away, so the map never lags the panel. */
export function setTripLayerFilter(next: TripLayerFilter): void {
  filter = next;
  activeReload?.();
}

// ---------------------------------------------------------------------------
// What the last answer said about itself
// ---------------------------------------------------------------------------

/**
 * What the panel needs to explain the overlay honestly, and the three states are deliberate: a
 * request that failed and a window that genuinely holds nothing look identical on an empty map,
 * and "no trips here" is a claim that may only be made once an answer has arrived.
 */
export interface TripLoadState {
  status: 'idle' | 'loading' | 'ok' | 'error';
  /** Trips drawn, counted as trips and never as shapes — see {@link summariseTripFeatures}. */
  shownTripCount: number;
  /** The server stopped at its cap; what is drawn is a slice and not the whole window. */
  truncated: boolean;
  /** Trips in the date window with no position anywhere — reported, never silently dropped. */
  unlocatedCount: number;
}

const emptyLoadState: TripLoadState = {
  status: 'idle',
  shownTripCount: 0,
  truncated: false,
  unlocatedCount: 0,
};

let loadState: TripLoadState = emptyLoadState;
const listeners = new Set<(state: TripLoadState) => void>();

export function getTripLoadState(): TripLoadState {
  return loadState;
}

/** Subscribes to load-state changes; returns an unsubscribe function. */
export function subscribeTripLoadState(listener: (state: TripLoadState) => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function publish(state: TripLoadState): void {
  loadState = state;
  for (const listener of listeners) {
    listener(state);
  }
}

/**
 * What an answer amounts to, for the panel to report.
 *
 * The count is of **distinct trips and never of features**, because one trip answers with up to
 * three shapes — the sketch it drew, where its party met, and a cave it names — and a reader
 * comparing this figure against the trip list is counting records. The list of features is not a
 * count of anything a reader recognises.
 */
export function summariseTripFeatures(collection: TripLogFeatureCollection): TripLoadState {
  const ids = new Set<string>();
  for (const feature of collection.features ?? []) {
    const id = (feature.properties as Record<string, unknown> | undefined)?.id;
    if (typeof id === 'string') {
      ids.add(id);
    }
  }
  return {
    status: 'ok',
    shownTripCount: ids.size,
    truncated: collection.truncated === true,
    unlocatedCount: collection.unlocatedCount ?? 0,
  };
}

// ---------------------------------------------------------------------------
// Loading
// ---------------------------------------------------------------------------

// Loading only runs while the overlay is enabled — no wasted fetches when it is toggled off.
let enabled = false;
let activeReload: (() => void) | undefined;

/** Enables/disables loading; enabling triggers an immediate load of the current extent. */
export function setTripsEnabled(value: boolean): void {
  enabled = value;
  if (enabled) {
    activeReload?.();
  } else {
    // A hidden layer states nothing, so the panel must not go on reporting the last answer as
    // though it were still on screen.
    source.clear(true);
    publish(emptyLoadState);
  }
}

/** Forces a refetch of the current extent (e.g. after changing the window or the narrowings). */
export function reloadTrips(): void {
  activeReload?.();
}

/** Bbox loading on moveend (debounced), stale responses discarded — mirrors the entrance loader. */
export function attachTripLoader(map: Map): () => void {
  let requestSeq = 0;
  let timer: number | undefined;

  const load = async () => {
    if (!enabled) {
      return;
    }
    const view = map.getView();
    const size = map.getSize();
    if (!size) {
      return;
    }
    const extent = transformExtent(view.calculateExtent(size), 'EPSG:3857', 'EPSG:4326');
    const bbox = extent.map((n) => n.toFixed(5)).join(',');
    const seq = ++requestSeq;
    publish({ ...loadState, status: 'loading' });
    try {
      const collection = await fetchTripLogFeatures(bbox, filter);
      if (seq !== requestSeq) {
        return; // a newer request superseded this one
      }
      source.clear(true);
      source.addFeatures(format.readFeatures(collection, { featureProjection: 'EPSG:3857' }));
      publish(summariseTripFeatures(collection));
    } catch {
      if (seq !== requestSeq) {
        return;
      }
      // The previously drawn shapes stay; the next moveend retries. What must not survive is the
      // claim that they are a complete answer to the question now being asked.
      publish({ ...emptyLoadState, status: 'error' });
    }
  };

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(() => void load(), 250);
  };

  map.on('moveend', onMoveEnd);
  activeReload = () => void load();
  return () => {
    map.un('moveend', onMoveEnd);
    window.clearTimeout(timer);
    activeReload = undefined;
  };
}
