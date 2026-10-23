// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type { FeatureLike } from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, RegularShape, Stroke, Style, Text } from 'ol/style';
import type ImageStyle from 'ol/style/Image';
import { fetchEntranceFeatures, fetchMapFeatures } from '../api/hooks/map.ts';
import { worldBbox } from '../geo/coords.ts';
import { declutterOption } from './declutter.ts';
import {
  FILTER_LAYER_PREFIX,
  filterLayerId,
  type FilterLayerSpec,
  type FilterLayerSymbol,
} from './filterLayerList.ts';
import { getOverlayGroup } from './mapContext.ts';
import { isMapFilterRefusal } from './mapFilters.ts';
import { filterLayerPalette, type FilterLayerColor } from './markerPalette.ts';

/**
 * The map layers drawn from saved filters, one per filter, each with a source and a loader of its
 * own: the same two map routes as the main layers (entrances, as clusters or points, and the
 * surface features), asked with that filter's id.
 *
 * A click on one of these answers as on the main layers (`selection.ts`): a marker selects its
 * cave entrance or feature through the ordinary selection path — the object is one the server
 * already handed this reader, at the point it handed it — and a cluster lists the members this
 * layer's filter keeps.
 */

const format = new GeoJSON();
const attached = new globalThis.Map<string, { detach: () => void; reload: () => void }>();
const refusalListeners = new Set<(savedFilterId: string, code: string) => void>();

/** Tells the page when a layer's filter was refused, so its row can say so. */
export function onFilterLayerRefusal(listener: (savedFilterId: string, code: string) => void): () => void {
  refusalListeners.add(listener);
  return () => {
    refusalListeners.delete(listener);
  };
}

export function createFilterLayer(spec: FilterLayerSpec): VectorLayer {
  const layer = new VectorLayer({ source: new VectorSource(), declutter: declutterOption() });
  layer.set('id', filterLayerId(spec.savedFilterId));
  layer.set('filterColor', spec.color);
  layer.set('filterSymbol', spec.symbol);
  layer.setStyle((feature) => filterLayerStyle(feature, layer.get('filterColor'), layer.get('filterSymbol')));
  return layer;
}

/**
 * Makes the overlay group hold exactly the filter layers listed: creates and attaches the missing,
 * removes and detaches the unlisted, and brings each one's colour, symbol, visibility and
 * transparency to what the list and the stored transparencies say.
 *
 * A layer whose filter is `unreadable` stays in the group, so its row keeps its place and can say
 * why it is empty, but it is emptied and asks the server nothing.
 */
export function syncFilterLayers(
  map: Map,
  specs: readonly FilterLayerSpec[],
  unreadable: ReadonlySet<string>,
  opacity: ReadonlyMap<string, number>,
): void {
  const group = getOverlayGroup();
  const wanted = new Set(specs.map((spec) => filterLayerId(spec.savedFilterId)));
  for (const layer of group.getLayers().getArray().slice()) {
    const id = layer.get('id') as string | undefined;
    if (id?.startsWith(FILTER_LAYER_PREFIX) && !wanted.has(id)) {
      attached.get(id)?.detach();
      attached.delete(id);
      group.getLayers().remove(layer);
    }
  }
  for (const spec of specs) {
    const id = filterLayerId(spec.savedFilterId);
    let layer = group.getLayers().getArray().find((candidate) => candidate.get('id') === id) as
      | VectorLayer
      | undefined;
    if (!layer) {
      layer = createFilterLayer(spec);
      group.getLayers().push(layer);
    }
    const blocked = unreadable.has(spec.savedFilterId.toLowerCase());
    const restyled = layer.get('filterColor') !== spec.color || layer.get('filterSymbol') !== spec.symbol;
    layer.set('filterColor', spec.color);
    layer.set('filterSymbol', spec.symbol);
    const wasBlocked = layer.get('filterUnreadable') === true;
    layer.set('filterUnreadable', blocked);
    if (layer.getVisible() !== spec.visible) {
      layer.setVisible(spec.visible);
    }
    const wantedOpacity = opacity.get(id) ?? 1;
    if (layer.getOpacity() !== wantedOpacity) {
      layer.setOpacity(wantedOpacity);
    }
    if (blocked) {
      layer.getSource()?.clear(true);
    }
    if (restyled) {
      layer.changed();
    }
    if (!attached.has(id)) {
      attached.set(id, attachFilterLayerLoader(map, layer, spec.savedFilterId));
    } else if (wasBlocked && !blocked) {
      attached.get(id)!.reload();
    }
  }
}

/** Detaches every filter layer's loader and removes the layers — for the page's unmount. */
export function detachFilterLayers(): void {
  const group = getOverlayGroup();
  for (const layer of group.getLayers().getArray().slice()) {
    const id = layer.get('id') as string | undefined;
    if (id?.startsWith(FILTER_LAYER_PREFIX)) {
      attached.get(id)?.detach();
      attached.delete(id);
      group.getLayers().remove(layer);
    }
  }
}

function attachFilterLayerLoader(
  map: Map,
  layer: VectorLayer,
  savedFilterId: string,
): { detach: () => void; reload: () => void } {
  let requestSeq = 0;
  let timer: number | undefined;
  const source = layer.getSource() as VectorSource;

  const load = async () => {
    // A hidden layer asks nothing; switching it back on loads it. An unreadable filter's layer
    // asks nothing either: the answer is already known.
    if (!layer.getVisible() || layer.get('filterUnreadable') === true) {
      return;
    }
    const size = map.getSize();
    if (!size) {
      return;
    }
    const view = map.getView();
    const bbox = worldBbox(transformExtent(view.calculateExtent(size), 'EPSG:3857', 'EPSG:4326'));
    const zoom = Math.round(view.getZoom() ?? 8);
    const seq = ++requestSeq;
    const [entrances, features] = await Promise.allSettled([
      fetchEntranceFeatures(bbox, zoom, { savedFilterId }),
      fetchMapFeatures(bbox, { savedFilterId }),
    ]);
    if (seq !== requestSeq) {
      return; // a newer request superseded this one
    }
    const refusal = [entrances, features].find(
      (answer): answer is PromiseRejectedResult => answer.status === 'rejected' && isMapFilterRefusal(answer.reason),
    );
    if (refusal) {
      // Never the unfiltered layer under the filter's name, and never what this filter drew
      // before it stopped being readable.
      source.clear(true);
      for (const listener of refusalListeners) {
        listener(savedFilterId, (refusal.reason as { code: string }).code);
      }
      return;
    }
    if (entrances.status !== 'fulfilled' || features.status !== 'fulfilled') {
      return; // a dropped request keeps what was drawn; the next move asks again
    }
    source.clear(true);
    source.addFeatures(format.readFeatures(entrances.value, { featureProjection: 'EPSG:3857' }));
    source.addFeatures(
      format
        .readFeatures(features.value, { featureProjection: 'EPSG:3857' })
        // A protected non-point feature the viewer may not see exactly arrives with no geometry.
        .filter((feature) => feature.getGeometry() !== undefined),
    );
  };

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(() => void load(), 250);
  };
  const onVisible = () => {
    if (layer.getVisible()) {
      void load();
    }
  };

  map.on('moveend', onMoveEnd);
  layer.on('change:visible', onVisible);
  void load();
  return {
    detach: () => {
      map.un('moveend', onMoveEnd);
      layer.un('change:visible', onVisible);
      window.clearTimeout(timer);
      requestSeq++; // an answer still on its way lands nowhere
    },
    reload: () => void load(),
  };
}

const styleCache = new globalThis.Map<string, Style>();

function shape(symbol: FilterLayerSymbol, color: string, radius: number, dashed: boolean): ImageStyle {
  const fill = new Fill({ color });
  const stroke = new Stroke({ color: '#ffffff', width: 2, lineDash: dashed ? [2, 2] : undefined });
  // An obstacle, not a competitor: the marker is always drawn and labels move out of its way.
  switch (symbol) {
    case 'square':
      return new RegularShape({ fill, stroke, points: 4, radius: radius + 1, angle: Math.PI / 4, declutterMode: 'obstacle' });
    case 'triangle':
      return new RegularShape({ fill, stroke, points: 3, radius: radius + 2, declutterMode: 'obstacle' });
    case 'diamond':
      return new RegularShape({ fill, stroke, points: 4, radius: radius + 2, declutterMode: 'obstacle' });
    default:
      return new CircleStyle({ fill, stroke, radius, declutterMode: 'obstacle' });
  }
}

/** Translucent version of a palette colour, for polygon fills. */
function translucent(hex: string, alpha: number): string {
  const value = Number.parseInt(hex.slice(1), 16);
  return `rgba(${(value >> 16) & 255}, ${(value >> 8) & 255}, ${value & 255}, ${alpha})`;
}

export function filterLayerStyle(
  feature: FeatureLike,
  colorKey: FilterLayerColor | undefined,
  symbol: FilterLayerSymbol | undefined,
): Style {
  const color = filterLayerPalette[colorKey ?? 'violet'] ?? filterLayerPalette.violet;
  const props = feature.getProperties();

  if (props.cluster === true) {
    // Not cached: the count is part of the picture.
    const count = Number(props.count ?? 0);
    return new Style({
      image: shape(symbol ?? 'circle', color, Math.min(24, 12 + Math.sqrt(count)), false),
      text: new Text({
        text: String(count),
        fill: new Fill({ color: '#ffffff' }),
        font: 'bold 12px sans-serif',
        declutterMode: 'none',
      }),
    });
  }

  const geometryType = feature.getGeometry()?.getType();
  const point = geometryType === 'Point' || geometryType === 'MultiPoint';
  const approximate = props.approximate === true;
  const key = `${colorKey}|${symbol}|${point ? (approximate ? 'pa' : 'p') : 'g'}`;
  let style = styleCache.get(key);
  if (!style) {
    style = point
      ? new Style({ image: shape(symbol ?? 'circle', color, approximate ? 8 : 7, approximate) })
      : new Style({
          stroke: new Stroke({ color, width: 2.5 }),
          fill: new Fill({ color: translucent(color, 0.15) }),
        });
    styleCache.set(key, style);
  }
  return style;
}
