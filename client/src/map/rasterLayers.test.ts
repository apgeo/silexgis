// SPDX-License-Identifier: AGPL-3.0-or-later
import { writeArrayBuffer } from 'geotiff';
import { getCenter } from 'ol/extent';
import Projection from 'ol/proj/Projection';
import { transformExtent } from 'ol/proj';
import GeoTIFF from 'ol/source/GeoTIFF';
import proj4 from 'proj4';
import { describe, expect, it, vi } from 'vitest';
import { placeRaster } from './rasterLayers.ts';
import { canPlace, resolveProjection } from './projections.ts';

// The installation's stated Stereo 70 definition, datum shift included.
const STEREO_70 =
  '+proj=sterea +lat_0=46 +lon_0=25 +k=0.99975 +x_0=500000 +y_0=500000 +ellps=krass ' +
  '+towgs84=2.329,-147.042,-92.08,-0.309,0.325,0.497,5.69 +units=m +no_defs';

/** A tiny one-band GeoTIFF whose top-left corner is at a given point of a coordinate system. */
function geoTiff(geoKeys: Record<string, number>, corner: [number, number], pixel: number): Blob {
  const buffer = writeArrayBuffer(new Uint8Array(16).fill(200), {
    width: 4,
    height: 4,
    ModelPixelScale: [pixel, pixel, 0],
    ModelTiepoint: [0, 0, 0, corner[0], corner[1], 0],
    GTRasterTypeGeoKey: 1,
    ...geoKeys,
  });
  return new Blob([buffer]);
}

// Bucharest, as the national grid writes it.
const BUCHAREST: [number, number] = [26.1, 44.43];
const bucharestInStereo70 = proj4('EPSG:4326', STEREO_70, BUCHAREST) as [number, number];

/**
 * An uploaded georeferenced map keeps the coordinate system it came in. These are the cases the
 * question "is an uploaded map in a non-web projection placed correctly?" turns on, read through
 * OpenLayers' own GeoTIFF reader as the map reads them.
 */
describe('placing an uploaded georeferenced map on the map', () => {
  it('cannot place a Stereo 70 map from OpenLayers alone — the map takes its system from the installation’s registry', async () => {
    const source = new GeoTIFF({
      sources: [{ blob: geoTiff({ GTModelTypeGeoKey: 1, ProjectedCSTypeGeoKey: 3844, ProjLinearUnitsGeoKey: 9001 }, bucharestInStereo70, 10) }],
    });
    const view = await source.getView();
    const projection = view.projection as Projection;

    // What the map had before it looked the system up: the file's code, and no way at all to
    // turn its coordinates into the map's — a layer shown like that cannot be drawn in place.
    expect(projection.getCode()).toBe('EPSG:3844');
    expect(canPlace('EPSG:3844')).toBe(false);
    expect(() => transformExtent(view.extent!, projection, 'EPSG:3857')).toThrow();

    const registry = vi.fn(async (code: string) => (code === 'EPSG:3844' ? STEREO_70 : null));
    await expect(placeRaster(source, (code) => resolveProjection(code, registry))).resolves.toBe(true);
    expect(registry).toHaveBeenCalledWith('EPSG:3844');

    // And now it lands where the national grid says it is (the picture is 40 m across).
    const [lon, lat] = getCenter(transformExtent(view.extent!, 'EPSG:3844', 'EPSG:4326'));
    expect(lon).toBeCloseTo(BUCHAREST[0], 3);
    expect(lat).toBeCloseTo(BUCHAREST[1], 3);
  });

  it('places a web-mercator or longitude-and-latitude map without asking anybody', async () => {
    const registry = vi.fn(async () => null);
    const mercator = new GeoTIFF({
      sources: [{ blob: geoTiff({ GTModelTypeGeoKey: 1, ProjectedCSTypeGeoKey: 3857, ProjLinearUnitsGeoKey: 9001 }, [2905000, 5525000], 10) }],
    });
    const geographic = new GeoTIFF({
      sources: [{ blob: geoTiff({ GTModelTypeGeoKey: 2, GeographicTypeGeoKey: 4326 }, [26.1, 44.43], 0.001) }],
    });

    await expect(placeRaster(mercator, (code) => resolveProjection(code, registry))).resolves.toBe(true);
    await expect(placeRaster(geographic, (code) => resolveProjection(code, registry))).resolves.toBe(true);
    expect(registry).not.toHaveBeenCalled();
  });

  it('does not place a map whose system the registry does not know, or that names none', async () => {
    const registry = vi.fn(async () => null);
    const unknown = new GeoTIFF({
      sources: [{ blob: geoTiff({ GTModelTypeGeoKey: 1, ProjectedCSTypeGeoKey: 2056, ProjLinearUnitsGeoKey: 9001 }, [2600000, 1200000], 10) }],
    });
    // A system written out in the file rather than named by a code: nothing to look up.
    const unnamed = new GeoTIFF({
      sources: [{ blob: geoTiff({ GTModelTypeGeoKey: 1, ProjectedCSTypeGeoKey: 32767 }, [500000, 500000], 10) }],
    });

    await expect(placeRaster(unknown, (code) => resolveProjection(code, registry))).resolves.toBe(false);
    await expect(placeRaster(unnamed, (code) => resolveProjection(code, registry))).resolves.toBe(false);
  });
});
