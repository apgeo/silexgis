// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';

import type { CaveOverburdenSample } from '../../api/hooks.ts';
import { overburdenGapReason, overburdenSeries } from './overburdenSeries.ts';

const reading = (
  distanceAlongM: number,
  overburdenM: number,
): CaveOverburdenSample => ({
  distanceAlongM,
  longitude: 25.1,
  latitude: 46.2,
  passageAltitudeM: 900,
  outcome: 'sampled',
  groundAltitudeM: 900 + overburdenM,
  overburdenM,
  pathIndex: 0,
  segmentIndex: 0,
});

const uncovered = (
  distanceAlongM: number,
  outcome: 'outsideCoverage' | 'noData',
): CaveOverburdenSample => ({
  distanceAlongM,
  longitude: 25.1,
  latitude: 46.2,
  passageAltitudeM: 900,
  outcome,
  groundAltitudeM: null,
  overburdenM: null,
  pathIndex: 0,
  segmentIndex: 0,
});

describe('overburdenSeries', () => {
  it('carries every reading across in the order it arrived', () => {
    const series = overburdenSeries([reading(0, 40), reading(10, 55), reading(20, 61)]);

    expect(series.x).toEqual([0, 10, 20]);
    expect(series.overburdenM).toEqual([40, 55, 61]);
    // The shading runs from the passage up to the thickness overhead wherever there is one.
    expect(series.base).toEqual([0, 0, 0]);
  });

  it('leaves a hole where the ground could not be read, and never a nought', () => {
    const series = overburdenSeries([
      reading(0, 40),
      uncovered(10, 'outsideCoverage'),
      uncovered(20, 'noData'),
      reading(30, 61),
    ]);

    // The whole point of the module: a missing reading is null in every array, so the curve
    // breaks. A nought would draw the passage arriving at the surface; a value carried over from
    // the neighbour would assert a thickness nobody measured.
    expect(series.overburdenM).toEqual([40, null, null, 61]);
    expect(series.base).toEqual([0, null, null, 0]);
    expect(series.overburdenM).not.toContain(0);
    // The distances stay, so the gap is a gap at a place rather than a shortened axis.
    expect(series.x).toEqual([0, 10, 20, 30]);
  });

  it('reports a surface below the passage as measured rather than clamping it away', () => {
    const series = overburdenSeries([reading(0, -3.5)]);

    expect(series.overburdenM).toEqual([-3.5]);
  });

  it('is empty for no readings at all', () => {
    const series = overburdenSeries([]);

    expect(series.x).toEqual([]);
    expect(series.overburdenM).toEqual([]);
    expect(series.base).toEqual([]);
  });
});

describe('overburdenGapReason', () => {
  it('says there is no gap when every reading got a ground height', () => {
    expect(overburdenGapReason([reading(0, 40), reading(10, 55)])).toBe('none');
  });

  it('separates ground nobody has modelled from a hole in the model', () => {
    expect(overburdenGapReason([reading(0, 40), uncovered(10, 'outsideCoverage')])).toBe(
      'outsideCoverage',
    );
    expect(overburdenGapReason([reading(0, 40), uncovered(10, 'noData')])).toBe('noData');
    expect(
      overburdenGapReason([uncovered(0, 'outsideCoverage'), uncovered(10, 'noData')]),
    ).toBe('mixed');
  });
});
