// SPDX-License-Identifier: AGPL-3.0-or-later
import { unByKey } from 'ol/Observable';
import WebGLTileLayer from 'ol/layer/WebGLTile';
import GeoTIFF, { type SourceInfo } from 'ol/source/GeoTIFF';
import type { MapLayerInfo } from '../api/hooks.ts';
import { registerProjection } from './projections.ts';

/**
 * What a cloud-optimised GeoTIFF catalogue entry needs in order to be drawn: the file's address,
 * its options and its credit — the same fields whether it came from the layer list or is the
 * background a shared view was saved with.
 */
export type CogEntry = Pick<MapLayerInfo, 'urlTemplate' | 'attribution' | 'minZoom'> & {
  cog: NonNullable<MapLayerInfo['cog']>;
};

/**
 * What OpenLayers is asked to read from the file: the bands the entry names, its nodata value
 * and its value range, each only when the entry gives it — otherwise the file's own.
 */
export function cogSourceInfo(entry: CogEntry): SourceInfo {
  const { bands, nodata, min, max } = entry.cog;
  return {
    url: entry.urlTemplate,
    ...(bands && bands.length > 0 ? { bands: [...bands] } : {}),
    ...(nodata !== null && nodata !== undefined ? { nodata } : {}),
    ...(min !== null && min !== undefined && max !== null && max !== undefined ? { min, max } : {}),
  };
}

/**
 * The layer that draws a catalogue GeoTIFF, read straight from its server with range requests.
 *
 * The file is placed in the coordinate system the catalogue names, never the one its own header
 * claims, and that system's definition — carried with the entry — is registered first, so a
 * Stereo 70 file is placed where the server and the coordinate formats place Romanian positions.
 * Samples are scaled across the entry's value range, or the file's own statistics, or its data
 * type's; one band is drawn grey, two grey with transparency, three as colour, four as colour with
 * transparency, which is OpenLayers' own way of drawing them. A photograph stored as YCbCr is
 * turned into colour.
 */
export function cogLayerFor(entry: CogEntry, zIndex: number, { deferUntilShown = false } = {}): WebGLTileLayer {
  if (entry.cog.projectionDefinition) {
    registerProjection(entry.cog.projection, entry.cog.projectionDefinition);
  }
  const layer = new WebGLTileLayer({ minZoom: entry.minZoom ?? 0, visible: false, zIndex });
  const attach = () =>
    layer.setSource(
      new GeoTIFF({
        sources: [cogSourceInfo(entry)],
        projection: entry.cog.projection,
        convertToRGB: 'auto',
        attributions: entry.attribution ?? undefined,
      }),
    );
  // The source reads the file's header as soon as it is made; one of a list of backgrounds the
  // reader has not chosen asks nothing of the server holding it.
  if (!deferUntilShown) {
    attach();
  } else {
    const key = layer.on('change:visible', () => {
      if (layer.getVisible()) {
        unByKey(key);
        attach();
      }
    });
  }
  return layer;
}
