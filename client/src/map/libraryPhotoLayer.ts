// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type FeatureLike from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Icon, Style } from 'ol/style';
import { fetchLibraryPhotoFeatures, type LibraryPhotoSource } from '../api/hooks.ts';
import { declutterOption } from './declutter.ts';
import { libraryPhotoPalette } from './markerPalette.ts';

/**
 * Photographs held by a photo library this installation does not own, drawn as one overlay per
 * library.
 *
 * One overlay each rather than one merged overlay carrying a source column, because the libraries
 * are independent installations with their own databases, their own storage and their own uptime:
 * a merged overlay would have one loading state and one failure state to describe two things that
 * fail separately, and switching one of them off to see what the other holds is the whole reason
 * somebody runs both. Both are drawn from this one module, so the loader, the staleness guard and
 * the pin are written once.
 *
 * Nothing here is held in React state — the sources, the layers, the debounce timer and the
 * request counters live in this module for the whole life of the page, the way every other
 * OpenLayers object in this application does. What React holds is which libraries are switched on
 * and the plain-data load state published below.
 */

/** The id prefix, so the layer panel and the click handler can recognise these without a list. */
export const LIBRARY_PHOTO_LAYER_PREFIX = 'library-photos:';

export function libraryPhotoLayerId(source: LibraryPhotoSource): string {
  return `${LIBRARY_PHOTO_LAYER_PREFIX}${source}`;
}

/** Which library a layer id belongs to, or undefined when it is not one of these layers. */
export function libraryPhotoSourceOf(
  layerId: string | undefined,
): LibraryPhotoSource | undefined {
  return layerId?.startsWith(LIBRARY_PHOTO_LAYER_PREFIX)
    ? layerId.slice(LIBRARY_PHOTO_LAYER_PREFIX.length)
    : undefined;
}

const format = new GeoJSON();

interface SourceState {
  /** The OL source. Module-level: OpenLayers objects never live in React state. */
  readonly features: VectorSource;
  enabled: boolean;
  /** Monotonic per library, so a slow answer for one cannot discard the other's. */
  requestSeq: number;
  /** Whether the reader asked to see the photographs themselves rather than pins. */
  pictures: boolean;
}

// `globalThis.Map` because `Map` in this module is OpenLayers' — the same shadowing the layer
// panel already works around.
const states = new globalThis.Map<LibraryPhotoSource, SourceState>();

function stateOf(source: LibraryPhotoSource): SourceState {
  let state = states.get(source);
  if (!state) {
    state = { features: new VectorSource(), enabled: false, requestSeq: 0, pictures: false };
    states.set(source, state);
  }
  return state;
}

const cameraSvg = (fill: string) =>
  "<svg xmlns='http://www.w3.org/2000/svg' width='26' height='26' viewBox='0 0 24 24'>" +
  // The dashed ring is the whole of what separates a foreign photograph from one of ours, and it
  // is the same mark this map already uses for an entrance shown somewhere other than where it
  // was surveyed: a position we are repeating rather than a position we recorded.
  `<circle cx='12' cy='12' r='11' fill='${fill}' stroke='${libraryPhotoPalette.stroke}'` +
  " stroke-width='1.5' stroke-dasharray='2.6 2'/>" +
  `<rect x='5.5' y='8.5' width='13' height='8.5' rx='1.6' fill='${libraryPhotoPalette.stroke}'/>` +
  `<rect x='9' y='6.8' width='6' height='2.2' rx='0.8' fill='${libraryPhotoPalette.stroke}'/>` +
  `<circle cx='12' cy='12.7' r='2.6' fill='${fill}'/></svg>`;

const pinStyle = (fill: string) =>
  // Always drawn: at the default declutter mode a pin that lost a contest for space would be a
  // photograph nobody can click, which is the opposite of what decluttering is for.
  [
    new Style({
      image: new Icon({
        src: `data:image/svg+xml;utf8,${encodeURIComponent(cameraSvg(fill))}`,
        scale: 1,
        declutterMode: 'obstacle',
      }),
    }),
  ];

// Built once per library and reused for every feature of every frame. A style allocated per
// feature is what turns a few thousand points into a map that will not pan. The hue is looked up
// rather than switched on, so a library this build has no colour for still gets a pin — in the
// shared foreign-photograph hue, which is wrong-looking rather than invisible.
const styles = new globalThis.Map<LibraryPhotoSource, Style[]>();

/**
 * Above this many photographs in view, pins are drawn whatever the reader asked for.
 *
 * A count and not a zoom. Zoom is a proxy for density and a poor one: a valley holding four hundred
 * photographs and a country holding four hundred are the same problem for the browser and a very
 * different one for a zoom threshold. The cost being bounded here is real — every picture is a
 * request through this application into a neighbouring library, and the comment above is the other
 * half of it: a style built per feature is what turns a few thousand points into a map that will
 * not pan.
 */
export const LIBRARY_PHOTO_PICTURE_LIMIT = 180;

/**
 * References whose picture failed, per library. A failed picture falls back to its pin and is
 * never asked for again.
 *
 * This is the balloon's rule, and it binds harder here. A failure in a balloon is one request; a
 * failure in a marker style is one per photograph in view, on every frame. And against a library
 * whose originals have gone away it is not merely wasteful: in one of the two products, asking for
 * a picture whose original cannot be resolved is itself what removes the photograph from the index.
 * A retry loop would be a deletion loop.
 */
const failedPictures = new globalThis.Map<LibraryPhotoSource, Set<string>>();

/** Loaded picture styles, keyed by library and reference, so panning re-uses rather than re-fetches. */
const pictureStyles = new globalThis.Map<string, Style>();
const pictureLoads = new Set<string>();

function pictureStyleFor(
  source: LibraryPhotoSource,
  reference: string,
  template: string,
): Style | undefined {
  const key = `${source}\u0000${reference}`;
  const ready = pictureStyles.get(key);
  if (ready) {
    return ready;
  }
  if (pictureLoads.has(key) || failedPictures.get(source)?.has(reference)) {
    return undefined; // in flight, or already known bad — the pin stands in either case
  }

  // Replaced through a function rather than with a string: `$&` and its siblings are substitution
  // syntax in a replacement, and a reference is foreign text that must not reach into the template
  // around it. The same guard the balloon applies to the same value.
  const encoded = encodeURIComponent(reference);
  const src = template.replace('{reference}', () => encoded).replace('{size}', () => 'small');

  pictureLoads.add(key);
  const image = new Image();
  image.decoding = 'async';
  image.addEventListener('load', () => {
    pictureLoads.delete(key);
    pictureStyles.set(
      key,
      new Style({
        image: new Icon({
          img: image,
          // A small square, drawn a little larger than the pin it replaces so that a photograph
          // reads as a photograph at a glance rather than as a slightly odd marker.
          width: 34,
          height: 34,
          declutterMode: 'obstacle',
        }),
      }),
    );
    // The frame that asked for this one has long since been drawn: ask for another.
    stateOf(source).features.changed();
  });
  image.addEventListener('error', () => {
    pictureLoads.delete(key);
    let failed = failedPictures.get(source);
    if (!failed) {
      failed = new Set<string>();
      failedPictures.set(source, failed);
    }
    failed.add(reference);
  });
  image.src = src;
  return undefined;
}

function styleOf(source: LibraryPhotoSource): Style[] {
  let style = styles.get(source);
  if (!style) {
    const hue =
      (libraryPhotoPalette as Record<string, string>)[source] ?? libraryPhotoPalette.photoprism;
    style = pinStyle(hue);
    styles.set(source, style);
  }
  return style;
}

/**
 * The style one photograph is drawn in.
 *
 * The pin is the answer in every case but one: pictures asked for, few enough of them to be worth
 * the requests, a library that publishes a picture URL at all, and a picture that has already
 * arrived. Anything else — too many, none asked for, stopped at the server's byte gate, still
 * loading, or loaded and failed — is a pin. Which is the point: the pin is the thing that can
 * never go missing, so a photograph is never silently absent from the map.
 */
function styleForFeature(source: LibraryPhotoSource, feature: FeatureLike): Style[] {
  const pin = styleOf(source);
  const state = stateOf(source);
  if (!state.pictures) {
    return pin;
  }
  const load = getLibraryPhotoLoadState(source);
  if (!load.pictureUrlTemplate || load.shownCount > LIBRARY_PHOTO_PICTURE_LIMIT) {
    return pin;
  }
  const reference = feature.get('reference') as unknown;
  if (typeof reference !== 'string' || reference.length === 0) {
    return pin;
  }
  return [pictureStyleFor(source, reference, load.pictureUrlTemplate) ?? pin[0]];
}

export function createLibraryPhotoLayer(source: LibraryPhotoSource): VectorLayer {
  // Stacking comes from the overlay group's collection order, not a fixed zIndex — the group
  // renumbers every child on add and remove, so a zIndex passed here would be overwritten.
  const layer = new VectorLayer({
    source: stateOf(source).features,
    style: (feature) => styleForFeature(source, feature),
    declutter: declutterOption(),
  });
  layer.set('id', libraryPhotoLayerId(source));
  layer.setVisible(false); // opt-in overlay
  return layer;
}

/**
 * What the last attempt against one library produced, for the surface that has to explain it.
 *
 * `reach` is the honest part. An overlay drawing no pins has three quite different reasons and
 * they must not be one state: the library answered and holds nothing here, the library did not
 * answer at all, or no library is configured — and the third is carried by there being no layer
 * rather than by a value here.
 */
export interface LibraryPhotoLoadState {
  libraryName: string;
  /** Pins currently drawn for this library. */
  shownCount: number;
  /**
   * Whether this installation's own point cap stopped the answer short. The truncation message is
   * keyed off this and never off `omittedCount`: a library asked for a clamped count truncates
   * with nothing to count, so a surface keyed off the number would go silent for exactly the case
   * it exists to describe.
   */
  truncated: boolean;
  /**
   * How many the cap held back, where that is knowable — which is only when the whole answer was
   * in hand before the cap was applied. Zero alongside `truncated: true` is a normal answer, not a
   * contradiction. A count of the cap, never of anything else.
   */
  omittedCount: number;
  /**
   * When these positions were read from the library. Null only before anything has been read: an
   * answer that exists was read at some moment, and while a library is not answering the pins on
   * the map are the last positions it gave rather than what it holds now.
   */
  readAt: string | null;
  /**
   * Where one picture is fetched from, with `{reference}` and `{size}` still in it. Null when this
   * library's pictures are stopped — the server's byte gate, published. A client reading null
   * requests no pictures rather than showing broken ones, which is the difference between a flag
   * and a guard.
   */
  pictureUrlTemplate: string | null;
  /** Whether the reader asked to see the photographs themselves rather than pins. */
  pictures: boolean;
  /**
   * Asked for, and refused because there are too many here.
   *
   * Carried rather than recomputed by whatever displays it, so the rule lives in one place: a
   * surface that worked out for itself when pictures stop would be a second copy of the ceiling,
   * and the two would part company the first time either moved.
   */
  picturesSuppressed: boolean;
  reach: 'idle' | 'loading' | 'ok' | 'unreachable';
}

export type LibraryPhotoLoadStates = Partial<Record<string, LibraryPhotoLoadState>>;

const IDLE: LibraryPhotoLoadState = {
  libraryName: '',
  shownCount: 0,
  truncated: false,
  omittedCount: 0,
  readAt: null,
  pictureUrlTemplate: null,
  pictures: false,
  picturesSuppressed: false,
  reach: 'idle',
};

let loadStates: LibraryPhotoLoadStates = {};
const listeners = new Set<(states: LibraryPhotoLoadStates) => void>();

export function getLibraryPhotoLoadStates(): LibraryPhotoLoadStates {
  return loadStates;
}

export function getLibraryPhotoLoadState(source: LibraryPhotoSource): LibraryPhotoLoadState {
  return loadStates[source] ?? IDLE;
}

/** Subscribes to load-state changes; returns an unsubscribe function. */
export function subscribeLibraryPhotoLoadStates(
  listener: (states: LibraryPhotoLoadStates) => void,
): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function publish(source: LibraryPhotoSource, next: LibraryPhotoLoadState): void {
  // Replaced rather than mutated: the panel holds this in React state, and a mutated object is
  // the same object, so the row would keep whatever it last rendered.
  loadStates = { ...loadStates, [source]: next };
  for (const listener of listeners) {
    listener(loadStates);
  }
}

let activeReload: (() => void) | undefined;

/**
 * Enables or disables loading for one library; enabling loads the current extent at once.
 * Disabling clears the pins, because a hidden overlay holding the last viewport's photographs
 * would draw them again over wherever the map has since moved to, for the moment before the first
 * answer arrives.
 */
export function setLibraryPhotosEnabled(source: LibraryPhotoSource, value: boolean): void {
  const state = stateOf(source);
  if (state.enabled === value) {
    return;
  }
  state.enabled = value;
  if (value) {
    activeReload?.();
  } else {
    // A request already in flight must not land on a cleared source: bumping the counter is what
    // makes its answer stale, the same way a newer request does.
    state.requestSeq += 1;
    state.features.clear(true);
    publish(source, IDLE);
  }
}

/**
 * Switches one library's overlay between pins and the photographs themselves.
 *
 * Only the styling changes: nothing is re-fetched, because the positions already drawn are the same
 * positions either way. Turning it off keeps whatever pictures have already been loaded, so turning
 * it back on is instant and costs the library nothing.
 */
export function setLibraryPhotoPictures(source: LibraryPhotoSource, value: boolean): void {
  const state = stateOf(source);
  if (state.pictures === value) {
    return;
  }
  state.pictures = value;
  const load = getLibraryPhotoLoadState(source);
  publish(source, {
    ...load,
    pictures: value,
    picturesSuppressed: value && load.shownCount > LIBRARY_PHOTO_PICTURE_LIMIT,
  });
  state.features.changed();
}

/** Whether one library is currently drawing photographs rather than pins. */
export function getLibraryPhotoPictures(source: LibraryPhotoSource): boolean {
  return stateOf(source).pictures;
}

/** Bbox loading on moveend (debounced), stale answers discarded — one request per library. */
export function attachLibraryPhotoLoader(map: Map): () => void {
  let timer: number | undefined;

  const loadOne = async (source: LibraryPhotoSource, bbox: string) => {
    const state = stateOf(source);
    const seq = ++state.requestSeq;
    publish(source, { ...getLibraryPhotoLoadState(source), reach: 'loading' });
    try {
      const collection = await fetchLibraryPhotoFeatures(source, bbox);
      if (seq !== state.requestSeq) {
        return; // a newer request, or the layer being switched off, superseded this one
      }
      state.features.clear(true);
      state.features.addFeatures(
        format.readFeatures(collection, { featureProjection: 'EPSG:3857' }),
      );
      publish(source, {
        libraryName: collection.libraryName,
        shownCount: collection.features.length,
        truncated: collection.truncated,
        omittedCount: collection.omittedCount,
        readAt: collection.readAt,
        pictureUrlTemplate: collection.pictureUrlTemplate,
        pictures: state.pictures,
        picturesSuppressed:
          state.pictures && collection.features.length > LIBRARY_PHOTO_PICTURE_LIMIT,
        reach: 'ok',
      });
    } catch {
      if (seq !== state.requestSeq) {
        return;
      }
      // The pins already drawn stay where they are. They are the last positions this library gave,
      // and a map that empties itself when one request fails is making the claim "there is nothing
      // here", which is a different and false answer. The surface says the library did not answer,
      // and the next moveend tries again — there is no retry loop of its own, because a library
      // that is down should not be hammered by a map nobody is panning.
      publish(source, { ...getLibraryPhotoLoadState(source), reach: 'unreachable' });
    }
  };

  const load = () => {
    const size = map.getSize();
    if (!size) {
      return;
    }
    const extent = transformExtent(map.getView().calculateExtent(size), 'EPSG:3857', 'EPSG:4326');
    const bbox = extent.map((n) => n.toFixed(5)).join(',');
    // One request per enabled library, never one request for both: they are separate
    // installations with separate uptime, and a joined request is as slow as the slower of them
    // and as broken as the more broken one.
    for (const [source, state] of states) {
      if (state.enabled) {
        void loadOne(source, bbox);
      }
    }
  };

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(load, 250);
  };

  map.on('moveend', onMoveEnd);
  activeReload = load;
  return () => {
    map.un('moveend', onMoveEnd);
    window.clearTimeout(timer);
    activeReload = undefined;
  };
}
