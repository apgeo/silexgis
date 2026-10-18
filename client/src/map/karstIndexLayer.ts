// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import type { FeatureLike } from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import type Map from 'ol/Map';
import VectorSource from 'ol/source/Vector';
import { Fill, Stroke, Style, Text } from 'ol/style';
import { ApiError } from '../api/client.ts';
import {
  fetchMapKarstIndex,
  type KarstIndexArea,
  type KarstIndexQuery,
  type KarstIndexWindow,
  type KarstificationClass,
} from '../api/hooks.ts';
import i18n from '../i18n';
import { clampToWorld } from './densityCells.ts';
import { karstIndexPalette as palette } from './markerPalette.ts';
import { attachWindowLoader } from './windowLoader.ts';

export const KARST_INDEX_LAYER_ID = 'karst-index';

// The karstification index of the karst areas and massifs in view, one fill per class.
//
// What is drawn is exactly what the server answered for this caller: the areas it may place
// exactly, with the outline the server sent. An area that is not in the answer is not drawn and
// nothing on the map says it exists — the server leaves out an area this caller may not place
// precisely so that its outline cannot be traced by panning, and drawing a placeholder for it
// here would undo that.

/** The classes, lowest first, with the one nothing could be measured for last. */
export const KARST_INDEX_CLASSES: readonly KarstificationClass[] = [
  'veryLow',
  'low',
  'moderate',
  'high',
  'veryHigh',
  'unknown',
];

const MAX_ZOOM = 24;

export interface KarstIndexLoadState {
  status: 'idle' | 'loading' | 'ok' | 'refused' | 'error';
  refusal?: string;
  /** How many areas the answer carried. */
  areaCount: number;
  /** The window held more areas than one answer carries; some are not drawn. */
  truncated: boolean;
  /** The most areas one answer carries here, once an answer arrived. */
  maxAreas: number | null;
}

const emptyState = (): KarstIndexLoadState => ({ status: 'idle', areaCount: 0, truncated: false, maxAreas: null });

const source = new VectorSource();
const format = new GeoJSON();
let loadState: KarstIndexLoadState = emptyState();
const listeners = new Set<(state: KarstIndexLoadState) => void>();

/** The features the layer is drawing, so what it draws can be asserted without a canvas. */
export function getKarstIndexSource(): VectorSource {
  return source;
}

export function getKarstIndexLoadState(): KarstIndexLoadState {
  return loadState;
}

/** Subscribes to load-state changes; returns an unsubscribe function. */
export function subscribeKarstIndexLoadState(listener: (state: KarstIndexLoadState) => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function publish(state: KarstIndexLoadState): void {
  loadState = state;
  for (const listener of listeners) {
    listener(state);
  }
}

// Hover and selection both outline an area and print its name. The ids are read by the style
// function, so the highlight survives a reload that recreates the features.
let hoveredId: string | null = null;
let selectedId: string | null = null;

export function setKarstIndexHovered(id: string | null): void {
  if (hoveredId !== id) {
    hoveredId = id;
    source.changed();
  }
}

export function setKarstIndexSelected(id: string | null): void {
  if (selectedId !== id) {
    selectedId = id;
    source.changed();
  }
}

export function createKarstIndexLayer(): VectorLayer {
  const layer = new VectorLayer({
    source,
    style: areaStyle,
    visible: false,
    // A massif and the karst areas inside it can both be in one answer. Drawing the larger first
    // keeps every smaller area visible and pickable on top of it.
    renderOrder: (a, b) => Number(b.get('areaKm2') ?? 0) - Number(a.get('areaKm2') ?? 0),
  });
  layer.set('id', KARST_INDEX_LAYER_ID);
  return layer;
}

/** What the tooltip and the highlighted label say of an area: its name and its class. */
export function karstIndexLabel(name: string | null | undefined, cls: KarstificationClass): string {
  const className = i18n.t(`karstArea.class.${cls}`);
  return name ? `${name} · ${className}` : className;
}

/** One feature per area, in map projection, carrying what its style and tooltip read. */
export function karstIndexFeatures(areas: readonly KarstIndexArea[]): Feature[] {
  const features: Feature[] = [];
  for (const area of areas) {
    const geometry = format.readGeometry(area.outline, {
      dataProjection: 'EPSG:4326',
      featureProjection: 'EPSG:3857',
    });
    const feature = new Feature({ geometry });
    feature.setId(area.id);
    feature.set('id', area.id);
    feature.set('name', area.name);
    feature.set('class', area.class);
    feature.set('score', area.score);
    feature.set('areaKm2', area.areaKm2 ?? 0);
    feature.set('label', karstIndexLabel(area.name, area.class));
    features.push(feature);
  }
  return features;
}

const styleCache = new globalThis.Map<string, Style>();

function classColours(cls: string) {
  return palette.classes[cls as KarstificationClass] ?? palette.classes.unknown;
}

function areaStyle(feature: FeatureLike): Style {
  const cls = String(feature.get('class') ?? 'unknown');
  const id = feature.get('id') as string | undefined;
  if (id && (id === hoveredId || id === selectedId)) {
    // Not cached: the label differs per area, and only one or two areas are highlighted at once.
    return new Style({
      fill: new Fill({ color: classColours(cls).fill }),
      stroke: new Stroke({ color: palette.highlight, width: 2.5 }),
      text: new Text({
        text: String(feature.get('label') ?? ''),
        font: '600 12px sans-serif',
        overflow: true,
        fill: new Fill({ color: palette.highlight }),
        stroke: new Stroke({ color: palette.labelHalo, width: 3 }),
      }),
    });
  }
  let style = styleCache.get(cls);
  if (!style) {
    const colours = classColours(cls);
    style = new Style({
      fill: new Fill({ color: colours.fill }),
      stroke: new Stroke({
        color: colours.stroke,
        width: 1,
        lineDash: cls === 'unknown' ? [4, 3] : undefined,
      }),
    });
    styleCache.set(cls, style);
  }
  return style;
}

function draw(answer: KarstIndexWindow): void {
  source.clear(true);
  source.addFeatures(karstIndexFeatures(answer.areas));
}

// ---------------------------------------------------------------------------
// Loading
// ---------------------------------------------------------------------------

export type KarstIndexFetch = (query: KarstIndexQuery, signal?: AbortSignal) => Promise<KarstIndexWindow>;

let enabled = false;
let activeReload: (() => void) | undefined;

/** Enables/disables loading; enabling loads the current window at once. */
export function setKarstIndexEnabled(value: boolean): void {
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

/**
 * Bbox loading on moveend, debounced; a newer window aborts the request it supersedes, and an
 * answer that arrives after its window was left is dropped rather than drawn.
 */
export function attachKarstIndexLoader(map: Map, fetchIndex: KarstIndexFetch = fetchMapKarstIndex): () => void {
  const loader = attachWindowLoader(map, {
    isEnabled: () => enabled,
    onStart: () => publish({ ...loadState, status: 'loading', refusal: undefined }),
    fetch: (window, view, signal) => {
      const bbox = clampToWorld(window)
        .map((n) => n.toFixed(5))
        .join(',');
      const zoom = Math.min(MAX_ZOOM, Math.max(0, Math.round(view.getZoom() ?? 0)));
      return fetchIndex({ bbox, zoom }, signal);
    },
    onAnswer: (answer) => {
      draw(answer);
      publish({
        status: 'ok',
        areaCount: answer.areas.length,
        truncated: answer.truncated,
        maxAreas: answer.maxAreas,
      });
    },
    onError: (error) => {
      // Nothing from an earlier window may stay drawn under a refusal or failure of this one.
      source.clear(true);
      const refused = error instanceof ApiError && error.status >= 400 && error.status < 500 && !!error.code;
      publish(
        refused
          ? { ...emptyState(), status: 'refused', refusal: (error as ApiError).code ?? undefined }
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
