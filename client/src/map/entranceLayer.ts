// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type { FeatureLike } from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Icon, Stroke, Style, Text } from 'ol/style';
import { fetchEntranceFeatures } from '../api/hooks.ts';
import { worldBbox } from '../geo/coords.ts';
import { handleMapFilterError, mapFilterParams } from './mapFilters.ts';
import { entrancePalette as palette } from './markerPalette.ts';
import { declutterOption } from './declutter.ts';
import { entranceSymbolReference, onEntranceSymbolsChanged } from './entranceSymbols.ts';
import { symbolImage, type SymbolImage } from './symbolLibrary.ts';

export const ENTRANCE_LAYER_ID = 'entrances';

const source = new VectorSource();
const format = new GeoJSON();

/** The live entrance source (individual entrances + low-zoom cluster features). */
export function getEntranceSource(): VectorSource {
  return source;
}

export function createEntranceLayer(): VectorLayer {
  // Stacking comes from the overlay group's collection order, not a fixed zIndex.
  const layer = new VectorLayer({ source, style: entranceStyle, declutter: declutterOption() });
  layer.set('id', ENTRANCE_LAYER_ID);
  return layer;
}

/**
 * Bbox/zoom loading strategy: reload on moveend (debounced),
 * stale responses discarded. Returns a detach function.
 */
let activeReload: (() => void) | undefined;

/** Forces a refetch of the current extent (e.g. after changing map filters). */
export function reloadEntrances(): void {
  activeReload?.();
}

export function attachEntranceLoader(map: Map): () => void {
  let requestSeq = 0;
  let timer: number | undefined;

  const load = async () => {
    const view = map.getView();
    const size = map.getSize();
    if (!size) {
      return;
    }
    const extent = transformExtent(view.calculateExtent(size), 'EPSG:3857', 'EPSG:4326');
    const bbox = worldBbox(extent);
    const zoom = Math.round(view.getZoom() ?? 8);
    const seq = ++requestSeq;
    try {
      const collection = await fetchEntranceFeatures(bbox, zoom, mapFilterParams());
      if (seq !== requestSeq) {
        return; // a newer request superseded this one
      }
      source.clear(true);
      source.addFeatures(format.readFeatures(collection, { featureProjection: 'EPSG:3857' }));
    } catch (error) {
      // Keep previous features on transient errors; next moveend retries. A refused filter is
      // not transient, and what was drawn before it was asked for is not what it selects.
      if (seq === requestSeq) {
        handleMapFilterError(error, () => source.clear(true));
      }
    }
  };

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(() => void load(), 250);
  };

  map.on('moveend', onMoveEnd);
  void load();
  activeReload = () => void load();
  return () => {
    map.un('moveend', onMoveEnd);
    window.clearTimeout(timer);
    activeReload = undefined;
  };
}

// Styles are built once per distinct look and shared by every feature that has it. The layer asks
// for a style per feature on every frame, and with hundreds of entrances in view, building a fresh
// style (and with it a fresh image) each time is what makes panning stutter; a shared style is
// also what lets OpenLayers rasterise each symbol image once.
const clusterStyles = new globalThis.Map<number, Style>();
const pointStyles = new globalThis.Map<string, Style>();

// A different choice of symbol restyles what is already drawn; nothing else would redraw it.
onEntranceSymbolsChanged(() => source.changed());

export function entranceStyle(feature: FeatureLike): Style {
  const props = feature.getProperties();

  if (props.cluster === true) {
    const count = Number(props.count ?? 0);
    let style = clusterStyles.get(count);
    if (!style) {
      style = new Style({
        // An obstacle, not a competitor: the marker is ALWAYS drawn, and labels move out of its
        // way. Left at the default, decluttering would hide overlapping markers themselves, which
        // would mean switching it on made features vanish — the opposite of what it is for.
        image: new CircleStyle({
          declutterMode: 'obstacle',
          radius: Math.min(24, 12 + Math.sqrt(count)),
          fill: new Fill({ color: palette.cluster }),
          stroke: new Stroke({ color: palette.stroke, width: 2 }),
        }),
        text: new Text({
          text: String(count),
          fill: new Fill({ color: palette.stroke }),
          font: 'bold 12px sans-serif',
          // Part of the bubble it is written in, not a label competing for space beside it.
          declutterMode: 'none',
        }),
      });
      clusterStyles.set(count, style);
    }
    return style;
  }

  return entrancePointStyle(props.approximate === true);
}

/**
 * The marker of one entrance: the installation's chosen symbol, or — when that names nothing this
 * build ships — the plain dot entrances were drawn with before symbols could be chosen. Built once
 * per distinct image and shared.
 */
function entrancePointStyle(approximate: boolean): Style {
  const image = symbolImage(entranceSymbolReference(approximate), {
    color: approximate ? palette.approximate : palette.point,
    approximate,
  });
  const key = image ? `${image.src}|${image.scale}` : `fallback|${approximate}`;
  let style = pointStyles.get(key);
  if (!style) {
    style = buildEntrancePointStyle(approximate, image);
    pointStyles.set(key, style);
  }
  return style;
}

function buildEntrancePointStyle(approximate: boolean, image: SymbolImage | undefined): Style {
  if (image) {
    // Always drawn, for the same reason as the plain circle below.
    return new Style({ image: new Icon({ src: image.src, scale: image.scale, declutterMode: 'obstacle' }) });
  }
  // A reference this build cannot read (a symbol file removed since it was chosen): the plain
  // marker entrances were drawn with before symbols could be chosen at all.
  return new Style({
    // An obstacle, not a competitor: the marker is ALWAYS drawn, and labels move out of its
    // way. Left at the default, decluttering would hide overlapping markers themselves, which
    // would mean switching it on made features vanish — the opposite of what it is for.
    image: new CircleStyle({
      declutterMode: 'obstacle',
      radius: approximate ? 8 : 7,
      fill: new Fill({ color: approximate ? palette.approximate : palette.point }),
      stroke: new Stroke({ color: palette.stroke, width: 2, lineDash: approximate ? [2, 2] : undefined }),
    }),
  });
}
