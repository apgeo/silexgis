// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import { LineString } from 'ol/geom';
import Draw from 'ol/interaction/Draw';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat, toLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Stroke, Style } from 'ol/style';
import { terrainProfilePalette } from './markerPalette.ts';
import { findOverlayLayer, getOverlayGroup, getWorkspaceMap } from './mapContext.ts';

export const TERRAIN_PROFILE_LAYER_ID = 'terrain-profile-line';

/** The most points one profile request may carry; a longer drawing is thinned to fit. */
export const TERRAIN_PROFILE_MAX_VERTICES = 200;

// A line drawn to ask "what does the ground do along here". The line is only the question, so it
// is kept on the map while the answer is read and removed when the person puts the tool away.

const source = new VectorSource();

const lineStyle = new Style({
  stroke: new Stroke({ color: terrainProfilePalette.line, width: 3, lineDash: [8, 6] }),
});

/** The features the layer is drawing, so what it draws can be asserted without a canvas. */
export function getTerrainProfileSource(): VectorSource {
  return source;
}

function ensureLayer(): void {
  if (findOverlayLayer(TERRAIN_PROFILE_LAYER_ID)) return;
  const layer = new VectorLayer({ source, style: lineStyle });
  layer.set('id', TERRAIN_PROFILE_LAYER_ID);
  getOverlayGroup().getLayers().push(layer);
}

/** Keeps the first and last vertex and spreads the rest evenly, so a long drawing fits one request. */
export function thinVertices<T>(points: readonly T[], max = TERRAIN_PROFILE_MAX_VERTICES): T[] {
  if (points.length <= max) return [...points];
  const out: T[] = [];
  for (let i = 0; i < max; i++) {
    out.push(points[Math.round((i * (points.length - 1)) / (max - 1))]);
  }
  return out;
}

export interface LonLat {
  longitude: number;
  latitude: number;
}

/** Shows a profile line (or none) in longitude/latitude, replacing what was shown. */
export function showTerrainProfileLine(points: readonly LonLat[] | null): void {
  source.clear(true);
  if (!points || points.length < 2) return;
  ensureLayer();
  source.addFeature(
    new Feature(new LineString(points.map((p) => fromLonLat([p.longitude, p.latitude])))),
  );
}

/**
 * Arms a one-line draw on the workspace map. `onLine` receives the finished line in longitude and
 * latitude, already thinned to what one request may carry. The interaction disarms itself when the
 * line is finished; the returned function disarms it early.
 */
export function armTerrainProfileDraw(onLine: (points: LonLat[]) => void): () => void {
  const map = getWorkspaceMap();
  const draw = new Draw({ type: 'LineString', minPoints: 2 });
  let armed = true;
  const disarm = () => {
    if (!armed) return;
    armed = false;
    map.removeInteraction(draw);
  };
  draw.on('drawend', (event) => {
    const coords = (event.feature.getGeometry() as LineString).getCoordinates();
    const points = thinVertices(coords).map((c) => {
      const [longitude, latitude] = toLonLat(c);
      return { longitude, latitude };
    });
    // Disarmed after the event has finished propagating, so the click that ended the line is not
    // also taken as a selection click by the interaction underneath.
    setTimeout(disarm, 0);
    onLine(points);
  });
  map.addInteraction(draw);
  return disarm;
}
