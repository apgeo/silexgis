// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type { FeatureLike } from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Icon, Stroke, Style } from 'ol/style';
import { fetchMapFeatures, type FeatureType } from '../api/hooks.ts';
import { onSurfaceFeaturesChanged } from '../workspace/surfaceFeatureRefresh.ts';
import {
  getFeatureTypeSymbol,
  onFeatureTypeCatalogChanged,
  setFeatureTypeCatalog,
} from './featureTypeCatalog.ts';
import { getMapTagFilter } from './mapFilters.ts';
import { featureSymbolUrl, surfaceFeaturePalette as palette } from './markerPalette.ts';

// The cross-kind features overlay. The layer id keeps its historical string:
// saved views persist overlay ids (visibility, opacity, stacking), and renaming
// it would silently drop the layer from every view saved before the rename.
export const SURFACE_FEATURE_LAYER_ID = 'surface-features';

const source = new VectorSource();
const format = new GeoJSON();

/** The live feature source — the edit controller draws into and snaps against it. */
export function getSurfaceFeatureSource(): VectorSource {
  return source;
}

// Feature-type names and symbols live in a catalog of their own, free of any drawing library,
// because the 3D scene needs the same answers and is loaded as a separate chunk — reaching them
// through this layer would put OpenLayers in it. This layer only has to redraw when they change.
const iconCache = new globalThis.Map<string, Icon>();

onFeatureTypeCatalogChanged(() => source.changed()); // restyle loaded features with the fresh names

export function setFeatureTypeSymbols(types: FeatureType[]): void {
  setFeatureTypeCatalog(types);
}

export { getFeatureTypeName, getFeatureTypeNameByCode } from './featureTypeCatalog.ts';

// Selection highlight: the styling reads this id so the highlight survives
// bbox reloads (features are recreated, the id is stable).
let selectedFeatureId: string | null = null;

export function setSelectedSurfaceFeature(id: string | null): void {
  if (selectedFeatureId !== id) {
    selectedFeatureId = id;
    source.changed();
  }
}

export function createSurfaceFeatureLayer(): VectorLayer {
  // Stacking comes from the overlay group's collection order, not a fixed zIndex.
  const layer = new VectorLayer({ source, style: featureStyle });
  layer.set('id', SURFACE_FEATURE_LAYER_ID);
  return layer;
}

/** Bbox loading on moveend (debounced), stale responses discarded; returns a detach fn. */
export function attachSurfaceFeatureLoader(map: Map): () => void {
  let requestSeq = 0;
  let timer: number | undefined;

  const load = async () => {
    const view = map.getView();
    const size = map.getSize();
    if (!size) {
      return;
    }
    const extent = transformExtent(view.calculateExtent(size), 'EPSG:3857', 'EPSG:4326');
    const bbox = extent.map((n) => n.toFixed(5)).join(',');
    const seq = ++requestSeq;
    try {
      const collection = await fetchMapFeatures(bbox, { tag: getMapTagFilter() ?? undefined });
      if (seq !== requestSeq) {
        return; // a newer request superseded this one
      }
      source.clear(true);
      source.addFeatures(
        format
          .readFeatures(collection, { featureProjection: 'EPSG:3857' })
          // A protected non-point feature the viewer may not see exactly arrives as a
          // readable row with a null geometry — nothing to draw, list or hit-test here.
          .filter((feature) => feature.getGeometry() !== undefined),
      );
    } catch {
      // Keep previous features on transient errors; next moveend retries.
    }
  };

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(() => void load(), 250);
  };

  map.on('moveend', onMoveEnd);
  void load();
  // A write elsewhere in the application invalidates this extent, and this layer is not the only
  // view of those features any more, so the news is subscribed to rather than delivered here.
  const unsubscribeChanges = onSurfaceFeaturesChanged(() => void load());
  return () => {
    map.un('moveend', onMoveEnd);
    window.clearTimeout(timer);
    unsubscribeChanges();
  };
}

function featureStyle(feature: FeatureLike): Style | Style[] {
  const geometryType = feature.getGeometry()?.getType();
  const selected = selectedFeatureId !== null && feature.get('id') === selectedFeatureId;

  // MultiPoint features (from imported multi-part geodata) style like points: OpenLayers
  // draws the image style at every point of the geometry.
  if (geometryType === 'Point' || geometryType === 'MultiPoint') {
    // A translucent halo behind the symbol marks the selected feature.
    const halo = selected
      ? [new Style({
          image: new CircleStyle({
            radius: 14,
            fill: new Fill({ color: palette.highlightHalo }),
            stroke: new Stroke({ color: palette.highlight, width: 2 }),
          }),
        })]
      : [];

    // Server rows carry their symbol file; pending locally drawn features only
    // carry the armed featureTypeId, resolved through the catalog instead.
    const symbolProp = feature.get('symbol') as string | null | undefined;
    const symbol = symbolProp ?? getFeatureTypeSymbol(feature.get('featureTypeId'));
    if (symbol) {
      let icon = iconCache.get(symbol);
      if (!icon) {
        icon = new Icon({ src: featureSymbolUrl(symbol), scale: 0.5 });
        iconCache.set(symbol, icon);
      }
      return [...halo, new Style({ image: icon })];
    }
    return [...halo, new Style({
      image: new CircleStyle({
        radius: 6,
        fill: new Fill({ color: palette.point }),
        stroke: new Stroke({ color: palette.stroke, width: 2 }),
      }),
    })];
  }

  if (selected) {
    return [
      new Style({ stroke: new Stroke({ color: palette.highlight, width: 6 }) }),
      new Style({
        stroke: new Stroke({ color: palette.line, width: 2.5 }),
        fill: new Fill({ color: palette.highlightHalo }),
      }),
    ];
  }

  return new Style({
    stroke: new Stroke({ color: palette.line, width: 2.5 }),
    fill: new Fill({ color: palette.fill }),
  });
}
