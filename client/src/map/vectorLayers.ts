// SPDX-License-Identifier: AGPL-3.0-or-later
import { applyBackground, applyStyle } from 'ol-mapbox-style';
import { PMTilesVectorSource } from 'ol-pmtiles';
import { unByKey } from 'ol/Observable';
import GeoJSON from 'ol/format/GeoJSON';
import MVT from 'ol/format/MVT';
import VectorLayer from 'ol/layer/Vector';
import VectorTileLayer from 'ol/layer/VectorTile';
import VectorSource from 'ol/source/Vector';
import VectorTileSource from 'ol/source/VectorTile';
import { PMTiles } from 'pmtiles';
import type { MapLayerInfo } from '../api/hooks.ts';

/**
 * What a vector catalogue entry needs in order to be drawn: its data address, the vector options
 * and the zoom band — the same fields whether the entry came from the layer list or is the
 * background a shared view was saved with.
 */
export type VectorEntry = Pick<MapLayerInfo, 'urlTemplate' | 'attribution' | 'minZoom' | 'maxZoom'> & {
  vector: NonNullable<MapLayerInfo['vector']>;
  /** A basemap paints its style's background colour under the shapes; an overlay does not. */
  isBase?: boolean;
};

export interface VectorLayerBuildOptions {
  /** The reader's language code (`ro`, `en`), for a style whose labels follow the reader. */
  language: string;
  /** Where the layer sits in the stack. */
  zIndex?: number;
  /** Whether the layer starts shown. Default false, as the catalogue's other layers do. */
  visible?: boolean;
  /** How documents are fetched; the page's own `fetch` unless a test hands it another. */
  fetch?: typeof fetch;
  /** The address relative addresses are read against; the page's own unless a test says otherwise. */
  baseUrl?: string;
  /**
   * Fetch nothing — no style, no TileJSON, no archive header — until the layer is first shown. For
   * a list of backgrounds of which a reader shows one, so that opening the map does not send the
   * browser to every service the catalogue names. Off by default.
   */
  deferUntilShown?: boolean;
}

/** A vector layer for one catalogue entry, and when its data and style are in place. */
export interface VectorLayerBuild {
  layer: VectorTileLayer | VectorLayer;
  /**
   * Settles once the layer has its source and, when the entry names one, its style: true when
   * all of it arrived, false when something did not — the data still draws, in the map's plain
   * default look, or nothing draws when the data itself could not be reached. Never rejects.
   */
  ready: Promise<boolean>;
}

/**
 * Where a label font is fetched from: the fonts this application serves itself. A style's own
 * font names are mapped onto them (see {@link localiseStyle}), so a vector background never sends
 * the reader's browser to a font service the installation did not choose — which the style library
 * otherwise does, to a public CDN, for every font a style names.
 */
export const MAP_FONT_TEMPLATE = '/map-fonts/{font-family}/{fontweight}{-fontstyle}.css';

/** The label fonts this application serves, by the weight and slant a style asks for. */
const SERVED_FONTS = {
  regular: 'Noto Sans Regular',
  medium: 'Noto Sans Medium',
  bold: 'Noto Sans Bold',
  italic: 'Noto Sans Italic',
} as const;

/** A MapLibre style document, as far as this module reads it. */
export interface StyleDocument {
  layers?: StyleLayer[];
  sources?: Record<string, { type?: string } & Record<string, unknown>>;
  [key: string]: unknown;
}

interface StyleLayer {
  id?: string;
  type?: string;
  layout?: Record<string, unknown>;
  [key: string]: unknown;
}

/**
 * Builds the OpenLayers layer that draws one vector catalogue entry, with its style applied.
 *
 * The entry's address is its data and the style only its look, so the source is built from the
 * entry and the style's own source definition is never used to fetch anything: a z/x/y template
 * or a TileJSON document (`mvt`), one PMTiles archive (`pmtiles`), or one GeoJSON file
 * (`geojson`). The style's layers drawing `styleSource` (or its first vector source) are applied
 * to that data. The tiles are asked for up to the entry's `maxZoom` and enlarged past it.
 *
 * The layer is returned at once and fills in as its documents arrive; `ready` says when.
 */
export function createVectorLayer(entry: VectorEntry, options: VectorLayerBuildOptions): VectorLayerBuild {
  const fetchDocument = options.fetch ?? ((input: RequestInfo | URL, init?: RequestInit) => fetch(input, init));
  const base = options.baseUrl ?? document.baseURI;
  const dataUrl = resolveAddress(entry.urlTemplate, base);
  const attributions = entry.attribution ?? undefined;
  const { min, max } = vectorZoomBand(entry);
  const common = { minZoom: min, visible: options.visible ?? false, zIndex: options.zIndex };

  if (entry.vector.format === 'geojson') {
    const layer = new VectorLayer({ ...common });
    const load = () => {
      layer.setSource(new VectorSource({ url: dataUrl, format: new GeoJSON(), attributions }));
      return styled(layer, entry, options, fetchDocument, base);
    };
    return { layer, ready: whenShown(layer, options, load) };
  }

  // Labels kept upright and placed again as the map turns, and never drawn over one another.
  const layer = new VectorTileLayer({ ...common, declutter: true, renderMode: 'hybrid' });
  const load = async () => {
    const source = await vectorTileSource(entry, dataUrl, max, attributions, fetchDocument);
    if (!source) {
      return false;
    }
    layer.setSource(source);
    return styled(layer, entry, options, fetchDocument, base);
  };
  return { layer, ready: whenShown(layer, options, load) };
}

/** Runs a layer's loading now, or — when asked to wait — the first time the layer is shown. */
function whenShown(
  layer: VectorTileLayer | VectorLayer,
  options: VectorLayerBuildOptions,
  load: () => Promise<boolean>,
): Promise<boolean> {
  if (!options.deferUntilShown || layer.getVisible()) {
    return load();
  }
  return new Promise((resolve) => {
    const key = layer.on('change:visible', () => {
      if (layer.getVisible()) {
        unByKey(key);
        load().then(resolve, () => resolve(false));
      }
    });
  });
}

/**
 * The zoom band a vector entry is drawn in: from its `minZoom`, and tiles asked for up to its
 * `maxZoom` — the last level they hold — past which the last level is enlarged, staying sharp.
 */
export function vectorZoomBand(entry: Pick<VectorEntry, 'minZoom' | 'maxZoom'>): { min: number; max: number } {
  const min = entry.minZoom ?? 0;
  const max = entry.maxZoom && entry.maxZoom > min ? entry.maxZoom : 14;
  return { min, max };
}

/** The tile source of an `mvt` or `pmtiles` entry, or null when its data cannot be reached. */
async function vectorTileSource(
  entry: VectorEntry,
  dataUrl: string,
  maxZoom: number,
  attributions: string | undefined,
  fetchDocument: typeof fetch,
): Promise<VectorTileSource | null> {
  if (entry.vector.format === 'pmtiles') {
    // The archive's header is read once here, so that an archive that is not there settles
    // `ready` instead of failing inside the source, where nothing would hear of it.
    try {
      await new PMTiles(dataUrl).getHeader();
    } catch {
      return null;
    }
    return new PMTilesVectorSource({ url: dataUrl, attributions, maxZoom });
  }

  // A template names the tile's coordinates; an address naming none of them is the service's
  // TileJSON document, which says where this build's tiles are.
  if (entry.urlTemplate.includes('{z}')) {
    return new VectorTileSource({ format: new MVT(), url: dataUrl, maxZoom, attributions });
  }
  const tileJson = await readJson<{ tiles?: unknown }>(fetchDocument, dataUrl);
  const template = Array.isArray(tileJson?.tiles) && typeof tileJson.tiles[0] === 'string' ? tileJson.tiles[0] : null;
  if (!template) {
    return null;
  }
  return new VectorTileSource({ format: new MVT(), url: resolveAddress(template, dataUrl), maxZoom, attributions });
}

/** Applies the entry's style to the layer; true when there is none or it was applied. */
async function styled(
  layer: VectorTileLayer | VectorLayer,
  entry: VectorEntry,
  options: VectorLayerBuildOptions,
  fetchDocument: typeof fetch,
  base: string,
): Promise<boolean> {
  if (!entry.vector.style) {
    return true;
  }
  const styleUrl = resolveAddress(entry.vector.style, base);
  const styleDocument = await readJson<StyleDocument>(fetchDocument, styleUrl);
  if (!styleDocument) {
    return false;
  }
  const style = localiseStyle(styleDocument, entry.vector.labelField, options.language);
  try {
    await applyStyle(layer, style, {
      source: entry.vector.styleSource ?? '',
      updateSource: false,
      styleUrl,
      webfonts: MAP_FONT_TEMPLATE,
    });
    if (entry.isBase) {
      await applyBackground(layer, style, { styleUrl });
    }
    return true;
  } catch {
    // A style whose layers draw nothing of this data, or whose icons did not arrive: the shapes
    // still draw, in the plain default look.
    return false;
  }
}

/**
 * An address read against another: an absolute one as it stands, a relative one — a source the
 * installation serves itself — against the page or the document that named it, keeping a tile
 * template's placeholders as written rather than percent-encoded.
 */
export function resolveAddress(address: string, base: string): string {
  if (/^[a-z][a-z0-9+.-]*:/i.test(address)) {
    return address;
  }
  return new URL(address, base).href.replace(/%7B/gi, '{').replace(/%7D/gi, '}');
}

/** A JSON document, or null when it could not be fetched or read. */
async function readJson<T>(fetchDocument: typeof fetch, url: string): Promise<T | null> {
  try {
    const response = await fetchDocument(url);
    if (!response.ok) {
      return null;
    }
    return (await response.json()) as T;
  } catch {
    return null;
  }
}

/**
 * A style made ready for this application: every label font mapped onto the fonts it serves, and
 * — when the entry names a label field — every name label reading the reader's language first.
 *
 * A name label is a symbol layer whose text reads a property starting with `name` (`name`,
 * `name:latin`, `name_en`…). Its text becomes the label field in the reader's language, falling
 * back to the plain `name`; labels of anything else — road numbers, house numbers, heights — are
 * left as the style wrote them. The document handed in is not changed.
 */
export function localiseStyle(document: StyleDocument, labelField: string | null, language: string): StyleDocument {
  const field = labelField?.replace('{lang}', language) ?? null;
  return {
    ...document,
    layers: (document.layers ?? []).map((layer) => {
      if (layer.type !== 'symbol' || !layer.layout) {
        return layer;
      }
      const layout = { ...layer.layout };
      if (layout['text-font'] !== undefined) {
        layout['text-font'] = servedFonts(layout['text-font']);
      }
      if (field && layout['text-field'] !== undefined && readsName(layout['text-field'])) {
        layout['text-field'] = ['coalesce', ['get', field], ['get', 'name']];
      }
      return { ...layer, layout };
    }),
  };
}

/** A font stack as the fonts this application serves: by the first font's weight and slant. */
function servedFonts(stack: unknown): unknown {
  const literal = Array.isArray(stack) && stack[0] === 'literal' && Array.isArray(stack[1]) ? stack[1] : stack;
  if (!Array.isArray(literal) || literal.length === 0 || !literal.every((font) => typeof font === 'string')) {
    // An expression choosing a font by zoom or by feature: drawn as the style says.
    return stack;
  }
  const first = (literal[0] as string).toLowerCase();
  if (first.includes('italic') || first.includes('oblique')) {
    return [SERVED_FONTS.italic];
  }
  if (/bold|black|heavy|semibold|extrabold/.test(first)) {
    return [SERVED_FONTS.bold];
  }
  if (first.includes('medium')) {
    return [SERVED_FONTS.medium];
  }
  return [SERVED_FONTS.regular];
}

/** Whether a label's text reads a name property: an expression `["get", "name…"]`, or a `{name…}` token. */
function readsName(textField: unknown): boolean {
  if (typeof textField === 'string') {
    return /\{name[^}]*\}/.test(textField);
  }
  if (Array.isArray(textField)) {
    if (textField[0] === 'get' && typeof textField[1] === 'string') {
      return textField[1].startsWith('name');
    }
    return textField.some(readsName);
  }
  if (textField && typeof textField === 'object') {
    return Object.values(textField).some(readsName);
  }
  return false;
}
