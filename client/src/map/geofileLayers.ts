// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style, Text } from 'ol/style';
import type { FeatureLike } from 'ol/Feature';
import { fetchGeofileFeatureCollection, type GeofileInfo } from '../api/hooks.ts';
import { declutterOption } from './declutter.ts';
import { GEOFILE_LABEL_PROPERTY } from './geofileProperties.ts';
import { getOverlayGroup } from './mapContext.ts';

// One vector layer per visible geofile, keyed by geofile id, living in the shared
// overlay group. Layers share a single moveend-driven bbox loader; style overrides
// come from the geofile's style jsonb.

export const GEOFILE_LAYER_PREFIX = 'geofile:';
const RASTER_LAYER_ID_PREFIX = 'raster:';
const format = new GeoJSON();

interface GeofileStyleOverrides {
  stroke?: string;
  fill?: string;
  point?: string;
}

/**
 * The zoom from which an imported point is drawn with its name beside it.
 *
 * Chosen against what the data is: at 15 a screen holds roughly a kilometre across, which is the
 * scale at which individual waypoints stop being a cloud and start being places somebody is
 * navigating between. Below it the names are drawn on top of one another and say nothing; above
 * it there is room, and reading a waypoint's name without clicking it is most of why the layer is
 * on at all.
 *
 * Expressed as a zoom and converted to a resolution once, because a style function is handed a
 * resolution and asking the view for its zoom inside one is a lookup per feature per frame.
 */
const LABEL_FROM_ZOOM = 15;

/**
 * Web-Mercator resolution at {@link LABEL_FROM_ZOOM}: the width of one screen pixel in metres.
 * 156543.03392804097 is the resolution at zoom 0 (the equator's circumference over 256 pixels),
 * halving with every zoom level.
 */
const LABEL_RESOLUTION = 156543.03392804097 / 2 ** LABEL_FROM_ZOOM;

function labelStyle(feature: FeatureLike, colour: string): Text | undefined {
  const label = feature.get(GEOFILE_LABEL_PROPERTY) as unknown;
  if (typeof label !== 'string' || label.trim().length === 0) {
    return undefined;
  }

  return new Text({
    text: label,
    font: '12px system-ui, sans-serif',
    offsetY: -14,
    fill: new Fill({ color: colour }),
    // A halo rather than a background plate: these are drawn over aerial imagery as often as over
    // a map, and dark text on light imagery is as unreadable as light on dark. An outline is
    // legible over both without covering the ground it sits on.
    stroke: new Stroke({ color: 'rgba(255, 255, 255, 0.9)', width: 3 }),
    // Only the label competes for space when decluttering is on. The marker underneath is always
    // drawn, so switching decluttering on can never make a point disappear — it can only make its
    // name give way to a neighbour's.
    declutterMode: 'declutter',
    overflow: false,
  });
}

/**
 * The style for one imported file's features.
 *
 * A function rather than a fixed style, because the label depends on both the feature (its name)
 * and the view (whether there is room for it). Geometry other than a point is left unlabelled: a
 * track's name would be drawn at the middle of a line that may run off both edges of the screen.
 */
function layerStyle(overrides: GeofileStyleOverrides | null): (feature: FeatureLike, resolution: number) => Style {
  const stroke = overrides?.stroke ?? '#2f54eb';
  const fill = overrides?.fill ?? 'rgba(47, 84, 235, 0.12)';
  const point = overrides?.point ?? overrides?.stroke ?? '#2f54eb';

  // Built once and reused across every feature and every frame. A style object allocated per
  // feature per redraw is what turns a few thousand waypoints into a map that will not pan.
  const base = new Style({
    stroke: new Stroke({ color: stroke, width: 2 }),
    fill: new Fill({ color: fill }),
    // An obstacle, not a competitor: the marker is ALWAYS drawn, and labels move out of its
    // way. Left at the default, decluttering would hide overlapping markers themselves, which
    // would mean switching it on made features vanish — the opposite of what it is for.
    image: new CircleStyle({
      declutterMode: 'obstacle',
      radius: 5,
      fill: new Fill({ color: point }),
      stroke: new Stroke({ color: '#ffffff', width: 1.5 }),
    }),
  });

  return (feature, resolution) => {
    const labelled =
      resolution <= LABEL_RESOLUTION && feature.getGeometry()?.getType() === 'Point'
        ? labelStyle(feature, stroke)
        : undefined;
    // Assigned rather than branching between two Style objects: setText(undefined) is how a style
    // stops carrying a label, and keeping one object means the layer is not rebuilt on every
    // zoom step across the threshold.
    base.setText(labelled);
    return base;
  };
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
  const overlays = getOverlayGroup().getLayers();

  // Remove layers that are no longer wanted.
  for (const layer of [...overlays.getArray()]) {
    const id = layer.get('id') as string | undefined;
    if (id?.startsWith(GEOFILE_LAYER_PREFIX) && !wanted.has(id.slice(GEOFILE_LAYER_PREFIX.length))) {
      overlays.remove(layer);
    }
  }

  // Add missing layers and refresh styles + opacity on existing ones.
  for (const [geofileId, geofile] of wanted) {
    const layerId = GEOFILE_LAYER_PREFIX + geofileId;
    const opacity = opacityById.get(geofileId) ?? 1;
    const existing = overlays.getArray()
      .find((l) => l.get('id') === layerId) as VectorLayer | undefined;
    if (existing) {
      existing.setStyle(layerStyle(parseOverrides(geofile)));
      existing.setOpacity(opacity);
      continue;
    }

    const layer = new VectorLayer({
      source: new VectorSource(),
      opacity,
      style: layerStyle(parseOverrides(geofile)),
      // Read at construction because that is the only time it can be given, and read from the
      // shared setting rather than passed in, so a file made visible while decluttering is on
      // comes up under the same rule as everything already drawn.
      declutter: declutterOption(),
    });
    layer.set('id', layerId);
    layer.set('name', geofile.name);
    // Default stacking slot: above rasters and existing geofiles, below the
    // built-in overlays (bottom→top collection order; users may re-drag later).
    const insertAt = overlays.getArray().filter((l) => {
      const id = l.get('id') as string | undefined;
      return id?.startsWith(RASTER_LAYER_ID_PREFIX) || id?.startsWith(GEOFILE_LAYER_PREFIX);
    }).length;
    overlays.insertAt(insertAt, layer);
    void loadLayer(map, geofileId, layer);
  }
}

/** Bbox loading for all geofile layers on moveend (debounced); returns a detach fn. */
export function attachGeofileLoader(map: Map): () => void {
  let timer: number | undefined;

  const loadAll = () => {
    for (const layer of getOverlayGroup().getLayers().getArray()) {
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
