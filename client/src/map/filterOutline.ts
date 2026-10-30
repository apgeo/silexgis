// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import Draw from 'ol/interaction/Draw';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import type { Polygon } from 'ol/geom';
import { toLonLat } from 'ol/proj';
import { Fill, Stroke, Style } from 'ol/style';
import { MAX_POLYGON_POSITIONS, isOnGlobe } from '../filters/spatial.ts';

/** Degrees a position is rounded to: about a tenth of a metre, far finer than any filter can use. */
const PRECISION = 6;

const round = (n: number) => Number(n.toFixed(PRECISION));

/**
 * The ground box on screen as one closed ring, kept on the globe. A view zoomed out past the world
 * reaches beyond ±180 and ±90, which an outline may not; it is clamped to the edge, which only
 * ever loses ground that does not exist. Null for a box with no area (not laid out yet, or a
 * degenerate one), which no outline can be.
 */
export function viewOutline(
  extent: readonly [number, number, number, number] | undefined,
): number[][][] | null {
  if (extent === undefined || !extent.every(Number.isFinite)) {
    return null;
  }
  const west = round(Math.max(-180, extent[0]));
  const south = round(Math.max(-90, extent[1]));
  const east = round(Math.min(180, extent[2]));
  const north = round(Math.min(90, extent[3]));
  if (!(east > west) || !(north > south)) {
    return null;
  }
  return [[[west, south], [east, south], [east, north], [west, north], [west, south]]];
}

/**
 * The rings of a polygon drawn on the map (projected coordinates) as longitude/latitude, or null
 * when it cannot be an outline: off the globe, or more positions than one outline may hold.
 */
export function ringsOfDrawn(projected: readonly (readonly (readonly number[])[])[]): number[][][] | null {
  let positions = 0;
  const rings: number[][][] = [];
  for (const ring of projected) {
    positions += ring.length;
    const out: number[][] = [];
    for (const position of ring) {
      const [lon, lat] = toLonLat([position[0], position[1]]);
      if (!isOnGlobe(lon, lat)) {
        return null;
      }
      out.push([round(lon), round(lat)]);
    }
    rings.push(out);
  }
  return positions > MAX_POLYGON_POSITIONS || rings.length === 0 ? null : rings;
}

const SKETCH = new Style({
  stroke: new Stroke({ color: '#d4380d', width: 2, lineDash: [6, 4] }),
  fill: new Fill({ color: 'rgba(212, 56, 13, 0.12)' }),
});

/**
 * Lets the person draw one polygon on the map for a filter to use. It resolves with the rings of
 * the finished polygon, or null when they pressed Escape or the polygon could not be an outline.
 * The sketch is drawn on a layer of its own that goes away with the interaction; nothing about the
 * features on the map is touched, and the clicks that place corners are kept from selecting what
 * lies under them.
 */
export function drawFilterOutline(map: Map): Promise<number[][][] | null> {
  return new Promise((resolve) => {
    const source = new VectorSource();
    const layer = new VectorLayer({ source, style: SKETCH, zIndex: 10_000 });
    const draw = new Draw({ source, type: 'Polygon', stopClick: true });

    const finish = (rings: number[][][] | null) => {
      window.removeEventListener('keydown', onKey, true);
      map.removeInteraction(draw);
      map.removeLayer(layer);
      resolve(rings);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        draw.abortDrawing();
        finish(null);
      }
    };

    draw.on('drawend', (event) => {
      const polygon = event.feature.getGeometry() as Polygon;
      finish(ringsOfDrawn(polygon.getCoordinates()));
    });
    window.addEventListener('keydown', onKey, true);
    map.addLayer(layer);
    map.addInteraction(draw);
  });
}
