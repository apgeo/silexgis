// SPDX-License-Identifier: AGPL-3.0-or-later
import { transform } from 'ol/proj';
import { describe, expect, it, vi } from 'vitest';
import { canPlace, installationDefinition, registerProjection, resolveProjection } from './projections.ts';

// The installation's stated Stereo 70 definition, datum shift included — the same string the
// server's registry and the coordinate formats hold to.
const STEREO_70 =
  '+proj=sterea +lat_0=46 +lon_0=25 +k=0.99975 +x_0=500000 +y_0=500000 +ellps=krass ' +
  '+towgs84=2.329,-147.042,-92.08,-0.309,0.325,0.497,5.69 +units=m +no_defs';

describe('the coordinate systems the map can place a raster in', () => {
  it('places the two systems OpenLayers knows, and nothing it was never given', () => {
    expect(canPlace('EPSG:3857')).toBe(true);
    expect(canPlace('EPSG:4326')).toBe(true);
    expect(canPlace('EPSG:31700')).toBe(false);
  });

  it('places a system whose definition the installation sent, where the national grid puts it', () => {
    registerProjection('EPSG:3844', STEREO_70);

    expect(canPlace('EPSG:3844')).toBe(true);
    // The grid's own origin (false easting and northing) lies at 25°E 46°N.
    const [lon, lat] = transform([500000, 500000], 'EPSG:3844', 'EPSG:4326');
    expect(lon).toBeCloseTo(25, 2);
    expect(lat).toBeCloseTo(46, 2);
  });

  it('looks a system up in the installation’s registry once, and says when it cannot be placed', async () => {
    const lookup = vi.fn(async (code: string) => (code === 'EPSG:31700' ? STEREO_70 : null));

    await expect(resolveProjection('EPSG:31700', lookup)).resolves.toBe(true);
    expect(canPlace('EPSG:31700')).toBe(true);
    // Already placeable: not asked again.
    await expect(resolveProjection('EPSG:31700', lookup)).resolves.toBe(true);
    expect(lookup).toHaveBeenCalledTimes(1);

    await expect(resolveProjection('EPSG:2056', lookup)).resolves.toBe(false);
    expect(canPlace('EPSG:2056')).toBe(false);
    // A definition proj4 cannot read is no definition.
    await expect(resolveProjection('EPSG:2100', async () => '+proj=nothing-like-this')).resolves.toBe(false);
  });

  it('asks only the installation’s own registry, and only for an EPSG code', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(STEREO_70, { status: 200 }));
    try {
      await expect(installationDefinition('EPSG:3844')).resolves.toBe(STEREO_70);
      expect(fetchSpy).toHaveBeenCalledWith('/api/v1/crs/3844.proj4', expect.anything());

      fetchSpy.mockClear();
      await expect(installationDefinition('ESRI:102008')).resolves.toBeNull();
      expect(fetchSpy).not.toHaveBeenCalled();

      fetchSpy.mockResolvedValueOnce(new Response('not found', { status: 404 }));
      await expect(installationDefinition('EPSG:999999')).resolves.toBeNull();
      fetchSpy.mockRejectedValueOnce(new TypeError('offline'));
      await expect(installationDefinition('EPSG:3035')).resolves.toBeNull();
    } finally {
      fetchSpy.mockRestore();
    }
  });
});
