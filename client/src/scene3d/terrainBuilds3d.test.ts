// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  boundsCentre,
  extentBox,
  extentContains,
  extentSizeKm,
  finerBuildAt,
  terrainCandidates,
  type TerrainBuildChoice,
  type TerrainConfig,
} from './terrainBuilds3d.ts';

/** A rectangle of about 40 km by 30 km over the mountains, as an extent is drawn. */
function rectangle(west: number, south: number, east: number, north: number) {
  return {
    type: 'Polygon',
    coordinates: [
      [
        [west, south],
        [east, south],
        [east, north],
        [west, north],
        [west, south],
      ],
    ],
  };
}

/** A build covering the box given, at the level given. */
function aBuild(
  id: string,
  depth: number,
  extent: unknown = rectangle(25.0, 45.0, 25.5, 45.3),
  overrides: Partial<TerrainBuildChoice> = {},
): TerrainBuildChoice {
  return {
    id,
    extent: extent as TerrainBuildChoice['extent'],
    requestedMaxDepth: depth,
    url: `/terrain/${id}/`,
    attribution: null,
    surveyHeightOffsetM: 0,
    isDrawn: false,
    finishedAt: '2026-08-12T10:00:00Z',
    ...overrides,
  };
}

describe('terrainCandidates', () => {
  const configured = {
    url: '/elevation/',
    attribution: '© Copernicus',
    surveyHeightOffsetM: 43,
    origin: 'configured' as const,
    buildId: null,
  };
  const own = {
    url: '/terrain/b1/',
    attribution: null,
    surveyHeightOffsetM: 0,
    origin: 'build' as const,
    buildId: 'b1',
  };
  const config: TerrainConfig = {
    terrain: configured,
    terrainFallback: own,
    terrainBuilds: [aBuild('b2', 14), aBuild('b1', 12, undefined, { isDrawn: true })],
  };

  it('tries the configured address first and the installation’s own pyramid after it', () => {
    expect(terrainCandidates(config, undefined)).toEqual([
      { url: '/elevation/', attribution: '© Copernicus', surveyHeightOffsetM: 43, buildId: null },
      { url: '/terrain/b1/', surveyHeightOffsetM: 0, buildId: 'b1' },
    ]);
  });

  it('tries the chosen build alone, with no fallback behind it', () => {
    // The viewer asked for this one by name. Falling back to another when it cannot be read would
    // draw ground they did not ask for under a control that still names the one they did.
    expect(terrainCandidates(config, 'b2')).toEqual([
      { url: '/terrain/b2/', surveyHeightOffsetM: 0, buildId: 'b2' },
    ]);
  });

  it('treats a choice the server no longer lists as no choice at all', () => {
    expect(terrainCandidates(config, 'gone')).toHaveLength(2);
  });

  it('has nothing to try before the configuration arrives, or when it names nothing', () => {
    expect(terrainCandidates(undefined, undefined)).toEqual([]);
    expect(terrainCandidates({ terrain: null, terrainFallback: null, terrainBuilds: [] }, undefined)).toEqual([]);
  });
});

describe('extentBox and extentSizeKm', () => {
  it('reads the box of a polygon and sizes it in kilometres', () => {
    const box = extentBox(rectangle(25.0, 45.0, 25.5, 45.3));
    expect(box).toEqual({ west: 25.0, south: 45.0, east: 25.5, north: 45.3 });

    // Half a degree of longitude at 45.15° north is about 39 km; three tenths of a degree of
    // latitude is about 33 km. A viewer choosing between builds reads these as "39 × 33 km".
    const size = extentSizeKm(rectangle(25.0, 45.0, 25.5, 45.3))!;
    expect(size.widthKm).toBeCloseTo(0.5 * 111.32 * Math.cos((45.15 * Math.PI) / 180), 2);
    expect(size.heightKm).toBeCloseTo(0.3 * 111.32, 2);
  });

  it('has no box for a shape that is not a polygon', () => {
    expect(extentBox({ type: 'Point', coordinates: [25, 45] })).toBeUndefined();
    expect(extentBox(null)).toBeUndefined();
    expect(extentSizeKm({ type: 'Polygon', coordinates: [] })).toBeUndefined();
  });
});

describe('extentContains', () => {
  const extent = rectangle(25.0, 45.0, 25.5, 45.3);

  it('answers inside for a point in the polygon and outside for one past its edge', () => {
    expect(extentContains(extent, 25.25, 45.15)).toBe(true);
    expect(extentContains(extent, 25.6, 45.15)).toBe(false);
    expect(extentContains(extent, 25.25, 44.9)).toBe(false);
  });

  it('walks the outline when the box alone would say yes', () => {
    // An L-shaped extent: its box covers the notch, the outline does not.
    const lShape = {
      type: 'Polygon',
      coordinates: [
        [
          [0, 0],
          [2, 0],
          [2, 1],
          [1, 1],
          [1, 2],
          [0, 2],
          [0, 0],
        ],
      ],
    };
    expect(extentContains(lShape, 0.5, 1.5)).toBe(true);
    expect(extentContains(lShape, 1.5, 1.5)).toBe(false);
  });

  it('reads every part of a multipolygon', () => {
    const twoParts = {
      type: 'MultiPolygon',
      coordinates: [rectangle(0, 0, 1, 1).coordinates, rectangle(5, 5, 6, 6).coordinates],
    };
    expect(extentContains(twoParts, 5.5, 5.5)).toBe(true);
    expect(extentContains(twoParts, 3, 3)).toBe(false);
  });
});

describe('finerBuildAt', () => {
  const region = aBuild('region', 12, rectangle(24.0, 44.0, 27.0, 47.0));
  const massif = aBuild('massif', 14, rectangle(25.0, 45.0, 25.5, 45.3));
  const massifNewer = aBuild('massif-2', 14, rectangle(25.0, 45.0, 25.5, 45.3));
  const builds = [massifNewer, massif, region];

  it('offers the finer build covering the place, and nothing over a place it does not cover', () => {
    expect(finerBuildAt(builds, 25.25, 45.15, 12)?.id).toBe('massif-2');
    expect(finerBuildAt(builds, 26.5, 46.5, 12)).toBeUndefined();
  });

  it('offers nothing that is not finer than what is drawn', () => {
    // Already on the massif build: the region build covers the place too, but coarser.
    expect(finerBuildAt(builds, 25.25, 45.15, 14)).toBeUndefined();
  });

  it('offers any build at all over the bare ellipsoid, and the finest of them', () => {
    expect(finerBuildAt(builds, 25.25, 45.15, undefined)?.id).toBe('massif-2');
    expect(finerBuildAt(builds, 26.5, 46.5, undefined)?.id).toBe('region');
  });

  it('leaves out builds the viewer declined, and goes to the next finest', () => {
    expect(finerBuildAt(builds, 25.25, 45.15, 12, new Set(['massif-2']))?.id).toBe('massif');
    expect(finerBuildAt(builds, 25.25, 45.15, 12, new Set(['massif-2', 'massif']))).toBeUndefined();
  });
});

describe('boundsCentre', () => {
  it('is the middle of the box, on the ellipsoid', () => {
    expect(boundsCentre([25.0, 45.0, 25.5, 45.3])).toEqual({
      longitude: 25.25,
      latitude: 45.15,
      height: 0,
    });
    expect(boundsCentre(undefined)).toBeUndefined();
  });
});
