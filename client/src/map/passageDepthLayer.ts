// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import type { FeatureLike } from 'ol/Feature';
import MultiLineString from 'ol/geom/MultiLineString';
import VectorLayer from 'ol/layer/Vector';
import type Map from 'ol/Map';
import { fromLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Stroke, Style } from 'ol/style';
import { ApiError } from '../api/client.ts';
import { fetchMapPassageDepth, type PassageDepthLine, type PassageDepthWindow } from '../api/hooks.ts';
import { clampToWorld } from './densityCells.ts';
import { passageDepthPalette as palette } from './markerPalette.ts';
import { attachWindowLoader } from './windowLoader.ts';

export const PASSAGE_DEPTH_LAYER_ID = 'passage-depth';

// Cave passage coloured by the rock over it.
//
// What is drawn is exactly what the server answered for this caller: the caves it may place
// exactly, at the cover it read. A cave that is not in the answer is not drawn and nothing here
// says it exists — the server leaves out a cave this caller may not place precisely so that its
// passage cannot be traced by panning, and a placeholder for it would undo that. The layer is
// deliberately not interactive: a click or a hover on it answers nothing, so the hit-test skips it.

export interface PassageDepthLoadState {
  status: 'idle' | 'loading' | 'ok' | 'refused' | 'error';
  refusal?: string;
  /** The HTTP status of a refusal: 403 means the caller holds no right to read elevation. */
  refusalStatus?: number;
  /** False once an answer says no elevation raster is built. */
  hasTerrain: boolean;
  /** Caves the answer drew from. */
  caveCount: number;
  /** The window held more caves than one answer draws; some are not drawn. */
  truncated: boolean;
  maxCaves: number | null;
  /** The class edges in metres, as the server defines them, for the legend. */
  classEdgesM: readonly number[];
}

const emptyState = (): PassageDepthLoadState => ({
  status: 'idle',
  hasTerrain: true,
  caveCount: 0,
  truncated: false,
  maxCaves: null,
  classEdgesM: [],
});

const source = new VectorSource();
let loadState: PassageDepthLoadState = emptyState();
const listeners = new Set<(state: PassageDepthLoadState) => void>();

/** The features the layer is drawing, so what it draws can be asserted without a canvas. */
export function getPassageDepthSource(): VectorSource {
  return source;
}

export function getPassageDepthLoadState(): PassageDepthLoadState {
  return loadState;
}

/** Subscribes to load-state changes; returns an unsubscribe function. */
export function subscribePassageDepthLoadState(listener: (state: PassageDepthLoadState) => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function publish(state: PassageDepthLoadState): void {
  loadState = state;
  for (const listener of listeners) {
    listener(state);
  }
}

export function createPassageDepthLayer(): VectorLayer {
  const layer = new VectorLayer({ source, style: lineStyle, visible: false });
  layer.set('id', PASSAGE_DEPTH_LAYER_ID);
  return layer;
}

/** One feature per cave and class, in map projection, carrying the class its style reads. */
export function passageDepthFeatures(lines: readonly PassageDepthLine[]): Feature[] {
  return lines.map((line) => {
    const feature = new Feature({
      geometry: new MultiLineString(line.paths.map((path) => path.map(([lon, lat]) => fromLonLat([lon, lat])))),
    });
    feature.set('caveId', line.caveId);
    feature.set('depthClass', line.depthClass);
    return feature;
  });
}

const styleCache = new globalThis.Map<number, Style[]>();

/** The style of a class: a light casing under the coloured line. Cached, there are only a few classes. */
function lineStyle(feature: FeatureLike): Style[] {
  const cls = Math.max(0, Math.min(Number(feature.get('depthClass') ?? 0), palette.classes.length - 1));
  let styles = styleCache.get(cls);
  if (!styles) {
    styles = [
      new Style({ stroke: new Stroke({ color: palette.casing, width: 5, lineCap: 'round' }) }),
      new Style({ stroke: new Stroke({ color: palette.classes[cls], width: 3, lineCap: 'round' }) }),
    ];
    styleCache.set(cls, styles);
  }
  return styles;
}

function draw(answer: PassageDepthWindow): void {
  source.clear(true);
  source.addFeatures(passageDepthFeatures(answer.lines));
}

let enabled = false;
let activeReload: (() => void) | undefined;

/** Enables/disables loading; enabling loads the current window at once. */
export function setPassageDepthEnabled(value: boolean): void {
  if (value === enabled) {
    return;
  }
  enabled = value;
  if (enabled) {
    activeReload?.();
  } else {
    source.clear(true);
    publish(emptyState());
  }
}

export type PassageDepthFetch = (bbox: string, signal?: AbortSignal) => Promise<PassageDepthWindow>;

/**
 * Bbox loading on moveend, debounced; a newer window aborts the request it supersedes, and an
 * answer that arrives after its window was left is dropped rather than drawn.
 */
export function attachPassageDepthLoader(map: Map, fetchWindow: PassageDepthFetch = fetchMapPassageDepth): () => void {
  const loader = attachWindowLoader(map, {
    isEnabled: () => enabled,
    onStart: () => publish({ ...loadState, status: 'loading', refusal: undefined }),
    fetch: (window, _view, signal) =>
      fetchWindow(
        clampToWorld(window)
          .map((n) => n.toFixed(5))
          .join(','),
        signal,
      ),
    onAnswer: (answer) => {
      draw(answer);
      publish({
        status: 'ok',
        hasTerrain: answer.hasTerrain,
        caveCount: answer.caveCount,
        truncated: answer.truncated,
        maxCaves: answer.maxCaves,
        classEdgesM: answer.classEdgesM,
      });
    },
    onError: (error) => {
      // Nothing from an earlier window may stay drawn under a refusal or failure of this one.
      source.clear(true);
      const refused = error instanceof ApiError && error.status >= 400 && error.status < 500 && !!error.code;
      publish(
        refused
          ? { ...emptyState(), status: 'refused', refusal: (error as ApiError).code ?? undefined, refusalStatus: (error as ApiError).status }
          : { ...emptyState(), status: 'error' },
      );
    },
  });

  activeReload = loader.reload;
  return () => {
    loader.detach();
    activeReload = undefined;
  };
}
