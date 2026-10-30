// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type { FeatureLike } from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import { Icon, Style } from 'ol/style';
import { fetchLibraryPhotoFeatures, type LibraryPhotoSource } from '../api/hooks.ts';
import { clampToWorld } from './densityCells.ts';
import { attachWindowLoader, type WindowLoader } from './windowLoader.ts';
import {
  libraryPictureFailed,
  libraryPictureUrl,
  markLibraryPictureFailed,
} from '../photolibrary/pictureUrl.ts';
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
  /** Whether the reader asked to see the photographs themselves rather than pins. */
  pictures: boolean;
}

// `globalThis.Map` because `Map` in this module is OpenLayers' — the same shadowing the layer
// panel already works around.
const states = new globalThis.Map<LibraryPhotoSource, SourceState>();

function stateOf(source: LibraryPhotoSource): SourceState {
  let state = states.get(source);
  if (!state) {
    state = { features: new VectorSource(), enabled: false, pictures: false };
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

/** Loaded picture styles, keyed by library and reference, so panning re-uses rather than re-fetches. */
const pictureStyles = new globalThis.Map<string, Style>();
const pictureLoads = new Set<string>();

/**
 * The style one photograph's own picture is drawn in, once it has arrived.
 *
 * A picture that failed falls back to its pin and is never asked for again, and which pictures
 * those are is remembered in one place for the whole application — the ledger imported above. The
 * rule binds hardest here: a failure in a balloon is one request, while a failure in a marker style
 * is one per photograph in view on every frame, and against a library whose originals have gone
 * away that is a deletion loop rather than a slow map.
 */
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
  if (pictureLoads.has(key) || libraryPictureFailed(source, reference)) {
    return undefined; // in flight, or already known bad — the pin stands in either case
  }

  const src = libraryPictureUrl(template, reference, 'small');
  if (!src) {
    return undefined;
  }

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
    markLibraryPictureFailed(source, reference);
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
   * The rectangle these positions were read for, in the form the server takes one. Null before
   * anything has been read.
   *
   * Carried because it is the rectangle the library answered, which is not always the rectangle on
   * screen: a balloon is opened after a pan as readily as before one. Anything that has to ask the
   * server about a single photograph has to say where to look for it, and the only rectangle that
   * is certain to contain it is the one it arrived in.
   */
  bbox: string | null;
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
  bbox: null,
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
    // A request already in flight must not land on a cleared source: the window loader drops an
    // answer that arrives for a layer that is no longer enabled.
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

/**
 * Loads each enabled library's pins for the map's window, one window loader per library.
 *
 * Per library, never joined: the libraries are separate installations with separate uptime, so
 * each has its own request in flight, aborted by the next window for that library alone.
 */
export function attachLibraryPhotoLoader(map: Map): () => void {
  const loaders = new globalThis.Map<LibraryPhotoSource, WindowLoader>();

  const loaderFor = (source: LibraryPhotoSource): WindowLoader => {
    let loader = loaders.get(source);
    if (!loader) {
      const state = stateOf(source);
      loader = attachWindowLoader(map, {
        isEnabled: () => state.enabled,
        onStart: () => publish(source, { ...getLibraryPhotoLoadState(source), reach: 'loading' }),
        // Clamped to the world because the server refuses a window past it, and a map zoomed far
        // out reports one.
        fetch: (window, _view, signal) => {
          const bbox = clampToWorld(window)
            .map((n) => n.toFixed(5))
            .join(',');
          return fetchLibraryPhotoFeatures(source, bbox, signal).then((collection) => ({
            collection,
            bbox,
          }));
        },
        onAnswer: ({ collection, bbox }) => {
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
            bbox,
            pictureUrlTemplate: collection.pictureUrlTemplate,
            pictures: state.pictures,
            picturesSuppressed:
              state.pictures && collection.features.length > LIBRARY_PHOTO_PICTURE_LIMIT,
            reach: 'ok',
          });
        },
        // The pins already drawn stay where they are. They are the last positions this library
        // gave, and a map that empties itself when one request fails is making the claim "there
        // is nothing here", which is a different and false answer. The surface says the library
        // did not answer, and the next moveend tries again — there is no retry loop of its own,
        // because a library that is down should not be hammered by a map nobody is panning.
        onError: () =>
          publish(source, { ...getLibraryPhotoLoadState(source), reach: 'unreachable' }),
      });
      loaders.set(source, loader);
    }
    return loader;
  };

  // A library that is switched on after the map was attached has no loader yet; this makes one
  // for every library the layer knows of and asks each enabled one for the window on screen.
  const reload = () => {
    for (const [source, state] of states) {
      const loader = loaderFor(source);
      if (state.enabled) {
        loader.reload();
      }
    }
  };

  // Loaders for the libraries already known listen to the map from now on; one made later by
  // `reload` starts listening when it is made, which is when its library is first switched on.
  for (const source of states.keys()) {
    loaderFor(source);
  }
  activeReload = reload;
  return () => {
    for (const loader of loaders.values()) {
      loader.detach();
    }
    loaders.clear();
    activeReload = undefined;
  };
}
