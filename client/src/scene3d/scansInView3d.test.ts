// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { CaveScanInView } from '../api/hooks.ts';
import { chooseScansInView, SCANS_IN_VIEW_MIN_BYTES, scansInViewLimitsInForce } from './scansInView3d.ts';

const config = {
  scansInViewMinZoom: 14,
  scansInViewMaxCaves: 6,
  scansInViewMaxBytes: 256 * 1024 * 1024,
  scansInViewMaxCavesLimit: 30,
  scansInViewMaxBytesLimit: 2 * 1024 * 1024 * 1024,
};

function scan(caveId: string, sizeBytes: number): CaveScanInView {
  return {
    caveId,
    caveName: caveId,
    surveyModelId: `model-${caveId}`,
    modelName: 'scan.laz',
    tilesetUrl: `/tiles/${caveId}/tileset.json`,
    anchorLongitude: 25,
    anchorLatitude: 45,
    anchorHeightM: 1000,
    headingDeg: 0,
    pitchDeg: 0,
    rollDeg: 0,
    pointCount: 1,
    tiledPointCount: 1,
    sizeBytes,
  };
}

describe('scansInViewLimitsInForce', () => {
  it('follows the installation where the person set nothing', () => {
    expect(scansInViewLimitsInForce(config)).toEqual({ minZoom: 14, maxCaves: 6, maxBytes: 256 * 1024 * 1024 });
  });

  it('holds a personal value between its floor and the installation ceiling', () => {
    const limits = scansInViewLimitsInForce(config, { minZoom: 40, maxCaves: 100, maxBytes: 1 });
    expect(limits).toEqual({ minZoom: 22, maxCaves: 30, maxBytes: SCANS_IN_VIEW_MIN_BYTES });
  });

  it('sets a personal value aside when the ceiling did not arrive', () => {
    const limits = scansInViewLimitsInForce(
      { ...config, scansInViewMaxBytesLimit: Number.NaN },
      { maxBytes: 512 * 1024 * 1024 },
    );
    expect(limits.maxBytes).toBe(config.scansInViewMaxBytes);
  });
});

describe('chooseScansInView', () => {
  const limits = { minZoom: 14, maxCaves: 2, maxBytes: 100 };

  it('takes nearest first and stops at the first scan over the byte budget', () => {
    const chosen = chooseScansInView({ items: [scan('a', 60), scan('b', 50), scan('c', 10)], total: 3 }, limits);
    expect(chosen.taken.map((item) => item.caveId)).toEqual(['a']);
    expect(chosen.limitedBy).toBe('bytes');
  });

  it('stops at the count and says so, including caves the server only counted', () => {
    const chosen = chooseScansInView({ items: [scan('a', 10), scan('b', 10)], total: 5 }, limits);
    expect(chosen.taken).toHaveLength(2);
    expect(chosen.limitedBy).toBe('count');
  });

  it('takes the selected cave first, whatever its size', () => {
    const chosen = chooseScansInView({ items: [scan('a', 10), scan('b', 500)], total: 2 }, limits, 'b');
    expect(chosen.taken.map((item) => item.caveId)).toEqual(['b']);
    expect(chosen.limitedBy).toBe('bytes');
  });

  it('reports no limit when everything in view was taken', () => {
    expect(chooseScansInView({ items: [scan('a', 10)], total: 1 }, limits)).toEqual({ taken: [scan('a', 10)] });
  });
});
