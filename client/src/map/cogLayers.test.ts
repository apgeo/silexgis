// SPDX-License-Identifier: AGPL-3.0-or-later
import WebGLTileLayer from 'ol/layer/WebGLTile';
import { describe, expect, it, vi } from 'vitest';

// The real source starts reading the file the moment it is made; this one only keeps what it was given.
const made: Record<string, unknown>[] = [];
vi.mock('ol/source/GeoTIFF', async () => {
  const { default: DataTile } = await import('ol/source/DataTile');
  return {
    default: class extends DataTile {
      constructor(options: Record<string, unknown>) {
        super({});
        made.push(options);
      }
    },
  };
});

const { cogLayerFor, cogSourceInfo } = await import('./cogLayers.ts');
const { canPlace } = await import('./projections.ts');
type CogEntry = Parameters<typeof cogLayerFor>[0];

const STEREO_70 =
  '+proj=sterea +lat_0=46 +lon_0=25 +k=0.99975 +x_0=500000 +y_0=500000 +ellps=krass ' +
  '+towgs84=2.329,-147.042,-92.08,-0.309,0.325,0.497,5.69 +units=m +no_defs';

function entry(cog: Partial<CogEntry['cog']> = {}, over: Partial<CogEntry> = {}): CogEntry {
  return {
    urlTemplate: 'https://data.example.invalid/lidar.tif',
    attribution: '© Agency',
    minZoom: 0,
    ...over,
    cog: { bands: null, nodata: null, min: null, max: null, projection: 'EPSG:3857', projectionDefinition: null, ...cog },
  };
}

describe('a catalogue GeoTIFF on the flat map', () => {
  it('reads the file as it stores itself unless the entry says which bands, nodata and range', () => {
    expect(cogSourceInfo(entry())).toEqual({ url: 'https://data.example.invalid/lidar.tif' });
    expect(cogSourceInfo(entry({ bands: [4, 3, 2], nodata: 0, min: 1, max: 4000 }))).toEqual({
      url: 'https://data.example.invalid/lidar.tif',
      bands: [4, 3, 2],
      nodata: 0,
      min: 1,
      max: 4000,
    });
    // A nodata of zero is a value, not an absence.
    expect(cogSourceInfo(entry({ nodata: 0 })).nodata).toBe(0);
  });

  it('places the file in the system the catalogue names, its definition registered first', () => {
    expect(canPlace('EPSG:3844')).toBe(false);

    const layer = cogLayerFor(
      entry({ projection: 'EPSG:3844', projectionDefinition: STEREO_70 }, { minZoom: 8 }),
      0.5,
    );

    expect(canPlace('EPSG:3844')).toBe(true);
    expect(layer).toBeInstanceOf(WebGLTileLayer);
    expect(layer.getVisible()).toBe(false);
    expect(layer.getMinZoom()).toBe(8);
    expect(layer.getZIndex()).toBe(0.5);
    const options = made.at(-1)!;
    expect(options.projection).toBe('EPSG:3844');
    expect(options.convertToRGB).toBe('auto');
    expect(options.attributions).toBe('© Agency');
    expect(options.sources).toEqual([{ url: 'https://data.example.invalid/lidar.tif' }]);
  });
});
