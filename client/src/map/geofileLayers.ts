// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { fetchGeofileFeatureCollection, type GeofileInfo } from '../api/hooks.ts';

// One vector layer per visible geofile, keyed by geofile id. Layers share a single
// moveend-driven bbox loader; style overrides come from the geofile's style jsonb.

const GEOFILE_LAYER_PREFIX = 'geofile:';
const format = new GeoJSON();

interface GeofileStyleOverrides {
  stroke?: string;
  fill?: string;
  point?: string;
}

function layerStyle(overrides: GeofileStyleOverrides | null): Style {
  const stroke = overrides?.stroke ?? '#2f54eb';
  const fill = overrides?.fill ?? 'rgba(47, 84, 235, 0.12)';
  const point = overrides?.point ?? overrides?.stroke ?? '#2f54eb';
  return new Style({
    stroke: new Stroke({ color: stroke, width: 2 }),
    fill: new Fill({ color: fill }),
    image: new CircleStyle({
      radius: 5,
      fill: new Fill({ color: point }),
      stroke: new Stroke({ color: '#ffffff', width: 1.5 }),
    }),
  });
}

function parseOverrides(geofile: GeofileInfo): GeofileStyleOverrides | null {
  const style = geofile.style;
  return typeof style === 'object' && style !== null ? (style as GeofileStyleOverrides) : null;
}

/**
 * Reconciles the map's geofile layers with the wanted set. Call whenever the
 * visible-geofile selection or the geofile catalog changes.
 */
export function syncGeofileLayers(
  map: Map,
  geofiles: GeofileInfo[],
  visibleIds: ReadonlySet<string>,
  opacityById: ReadonlyMap<string, number> = new globalThis.Map(),
): void {
  const wanted = new globalThis.Map(
    geofiles.filter((g) => visibleIds.has(g.id) && g.importStatus === 'imported').map((g) => [g.id, g]),
  );

  // Remove layers that are no longer wanted.
  for (const layer of [...map.getLayers().getArray()]) {
    const id = layer.get('id') as string | undefined;
    if (id?.startsWith(GEOFILE_LAYER_PREFIX) && !wanted.has(id.slice(GEOFILE_LAYER_PREFIX.length))) {
      map.removeLayer(layer);
    }
  }

  // Add missing layers and refresh styles + opacity on existing ones.
  for (const [geofileId, geofile] of wanted) {
    const layerId = GEOFILE_LAYER_PREFIX + geofileId;
    const opacity = opacityById.get(geofileId) ?? 1;
    const existing = map.getLayers().getArray()
      .find((l) => l.get('id') === layerId) as VectorLayer | undefined;
    if (existing) {
      existing.setStyle(layerStyle(parseOverrides(geofile)));
      existing.setOpacity(opacity);
      continue;
    }

    const layer = new VectorLayer({
      source: new VectorSource(),
      zIndex: 8, // under entrances (10) and surface features (9)
      opacity,
      style: layerStyle(parseOverrides(geofile)),
    });
    layer.set('id', layerId);
    map.addLayer(layer);
    void loadLayer(map, geofileId, layer);
  }
}

/** Bbox loading for all geofile layers on moveend (debounced); returns a detach fn. */
export function attachGeofileLoader(map: Map): () => void {
  let timer: number | undefined;

  const loadAll = () => {
    for (const layer of map.getLayers().getArray()) {
      const id = layer.get('id') as string | undefined;
      if (id?.startsWith(GEOFILE_LAYER_PREFIX)) {
        void loadLayer(map, id.slice(GEOFILE_LAYER_PREFIX.length), layer as VectorLayer);
      }
    }
  };

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(loadAll, 250);
  };

  map.on('moveend', onMoveEnd);
  return () => {
    map.un('moveend', onMoveEnd);
    window.clearTimeout(timer);
  };
}

// Per-layer request sequencing lives on the layer instance so stale responses
// never clobber fresher ones.
async function loadLayer(map: Map, geofileId: string, layer: VectorLayer): Promise<void> {
  const size = map.getSize();
  if (!size) {
    return;
  }

  const extent = transformExtent(map.getView().calculateExtent(size), 'EPSG:3857', 'EPSG:4326');
  const bbox = extent.map((n) => n.toFixed(5)).join(',');
  const seq = ((layer.get('requestSeq') as number | undefined) ?? 0) + 1;
  layer.set('requestSeq', seq);

  try {
    const collection = await fetchGeofileFeatureCollection(geofileId, bbox);
    if (layer.get('requestSeq') !== seq) {
      return; // superseded
    }
    const source = layer.getSource() as VectorSource;
    source.clear(true);
    source.addFeatures(format.readFeatures(collection, { featureProjection: 'EPSG:3857' }));
  } catch {
    // Keep previous features on transient errors; next moveend retries.
  }
}
