// SPDX-License-Identifier: AGPL-3.0-or-later
import WebGLTileLayer, { type Style as WebGLTileStyle } from 'ol/layer/WebGLTile';
import GeoTIFF from 'ol/source/GeoTIFF';
import type { TerrainDerivativeLayerInfo } from '../api/hooks.ts';
import { getOverlayGroup } from './mapContext.ts';

/**
 * The shaded and coloured pictures of the ground, drawn over the map.
 *
 * A prefix of their own rather than the one uploaded rasters use. The two are reconciled by
 * separate passes, each removing every layer carrying its prefix that is not in its own wanted
 * set, so sharing one would have each pass delete the other's layers on every render.
 */
export const TERRAIN_DERIVATIVE_LAYER_PREFIX = 'terrain-derivative:';

/**
 * A build's elevation is several rasters at several pixel sizes, never merged onto a common grid,
 * so a picture of a build is one file per raster and one map layer per file. The layer id carries
 * the version the file was computed at: a recomputation writes new bytes at a new address, and a
 * layer kept because its id matched would otherwise go on drawing the previous picture for as long
 * as the page stayed open.
 */
function layerIdOf(derivative: TerrainDerivativeLayerInfo, rasterId: number): string {
  return `${TERRAIN_DERIVATIVE_LAYER_PREFIX}${derivative.id}:${derivative.version}:${rasterId}`;
}

/**
 * Which picture a map layer belongs to, read back out of its id.
 *
 * One picture is several layers, one per raster of the build, and they are shown and faded as one
 * thing — so a transparency dragged on any of them is remembered against the picture, not against
 * the file that happened to be under the pointer.
 */
export function terrainDerivativeIdOf(layerId: string): string | undefined {
  if (!layerId.startsWith(TERRAIN_DERIVATIVE_LAYER_PREFIX)) return undefined;
  return layerId.slice(TERRAIN_DERIVATIVE_LAYER_PREFIX.length).split(':')[0];
}

/**
 * The span of values a single-band picture is stretched across for display.
 *
 * Steepness and facing have real bounds and are stated exactly. Ruggedness, topographic position
 * and roughness are height differences in metres with no upper bound at all, so what is given here
 * is a display range chosen to suit ordinary karst relief, not a measurement: ground beyond it is
 * drawn at the end of the ramp rather than wrongly. Saturation at the top of a ramp is visible to a
 * reader; a picture rendered as though its numbers ran to the limits of its number type is not — it
 * is uniformly black, which looks like missing data rather than a display choice.
 */
const DISPLAY_RANGE: Partial<Record<TerrainDerivativeLayerInfo['derivative'], [number, number]>> = {
  // Degrees from horizontal. A slope asked for as a percentage runs past this and saturates above
  // 45 degrees; the unit is settled when the picture is asked for and is not carried on the layer.
  slope: [0, 90],
  aspect: [0, 360],
  ruggednessIndex: [0, 50],
  positionIndex: [-25, 25],
  roughness: [0, 100],
};

/** Dark where the value is low, pale where it is high. */
const GREY_RAMP = [
  'interpolate',
  ['linear'],
  ['band', 1],
  0,
  '#000000',
  1,
  '#ffffff',
];

/**
 * Facing is a compass direction, so it is drawn with a ramp that comes back to where it started:
 * north is the same colour whether it is reached from 359 degrees or from 1, and a hillside facing
 * just east of north must not read as the opposite of one facing just west of it.
 */
const ASPECT_RAMP = [
  'interpolate',
  ['linear'],
  ['band', 1],
  0,
  '#e15759',
  0.25,
  '#f0c419',
  0.5,
  '#59a14f',
  0.75,
  '#4e79a7',
  1,
  '#e15759',
];

/**
 * How one raster of one picture is turned into a map layer.
 *
 * The two families are drawn differently and must be. Shaded relief comes back as whole bytes and
 * coloured relief as red, green and blue, so both are read as a picture and shown as one. Steepness,
 * facing, ruggedness, topographic position and roughness come back as one band of floating-point
 * measurements — degrees, metres — and reading those as a picture asks the reader's browser to
 * interpret a slope of 30 degrees as a brightness on a scale that runs to the largest number the
 * type can hold. The result is not an error: it is a layer that draws, and is black everywhere.
 */
function sourceFor(derivative: TerrainDerivativeLayerInfo, url: string): {
  source: GeoTIFF;
  style?: WebGLTileStyle;
} {
  const range = DISPLAY_RANGE[derivative.derivative];
  if (!range) {
    return { source: new GeoTIFF({ sources: [{ url }], convertToRGB: true }) };
  }

  const [min, max] = range;
  return {
    source: new GeoTIFF({ sources: [{ url, min, max }], normalize: true }),
    style: terrainDerivativeStyle(derivative.derivative),
  };
}

/**
 * How a measured picture is coloured, or nothing where the picture is already a picture.
 *
 * Exported so that the expressions can be compiled where they are cheap to compile. They are read
 * by the drawing hardware and nowhere else, so a mistake in one is a failure at first draw on a
 * reader's machine rather than anywhere a check would see it.
 */
export function terrainDerivativeStyle(
  kind: TerrainDerivativeLayerInfo['derivative'],
): WebGLTileStyle | undefined {
  if (!DISPLAY_RANGE[kind]) return undefined;
  return { color: kind === 'aspect' ? ASPECT_RAMP : GREY_RAMP };
}

/** Reconciles the computed terrain overlays with the wanted set and applies their opacity. */
export function syncTerrainDerivativeLayers(
  derivatives: TerrainDerivativeLayerInfo[],
  visibleIds: ReadonlySet<string>,
  opacityById: ReadonlyMap<string, number>,
): void {
  const wanted = new globalThis.Map<string, { derivative: TerrainDerivativeLayerInfo; url: string }>();
  for (const derivative of derivatives) {
    if (derivative.status !== 'ready' || !visibleIds.has(derivative.id)) continue;
    for (const raster of derivative.rasters) {
      wanted.set(layerIdOf(derivative, raster.id), { derivative, url: raster.url });
    }
  }

  const overlays = getOverlayGroup().getLayers();
  for (const layer of [...overlays.getArray()]) {
    const id = layer.get('id') as string | undefined;
    if (id?.startsWith(TERRAIN_DERIVATIVE_LAYER_PREFIX) && !wanted.has(id)) {
      overlays.remove(layer);
    }
  }

  for (const [layerId, { derivative, url }] of wanted) {
    const opacity = opacityById.get(derivative.id) ?? 1;
    const existing = overlays.getArray().find((l) => l.get('id') === layerId);
    if (existing) {
      existing.setOpacity(opacity);

      // The signature in the address expires, which is why the listing is re-read while the page
      // is open — and a layer that kept the address it was built with would go on asking for
      // ranges of the file with a signature that has lapsed. The reader answers a refusal with a
      // blank tile, so the picture would simply stop filling in as somebody panned, with nothing
      // on screen saying why. The source is rebuilt only when the address has actually changed.
      if (existing.get('url') !== url) {
        existing.set('url', url);
        (existing as WebGLTileLayer).setSource(sourceFor(derivative, url).source);
      }

      existing.set('stale', derivative.stale);
      continue;
    }

    const { source, style } = sourceFor(derivative, url);
    const layer = new WebGLTileLayer({
      source,
      ...(style ? { style } : {}),
      opacity,
    });
    layer.set('id', layerId);
    layer.set('name', derivative.name);
    layer.set('stale', derivative.stale);
    layer.set('url', url);

    // Bottom of the overlay stack, beneath every vector overlay: these are pictures of the ground
    // and everything else is drawn on top of the ground.
    overlays.insertAt(0, layer);
  }
}
