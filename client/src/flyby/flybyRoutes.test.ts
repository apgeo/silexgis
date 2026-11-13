// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { surveyRoute, trackRoute, type FlybyLinePoint } from './flybyRoutes.ts';

const station = (longitude: number, latitude: number, height: number): FlybyLinePoint => ({ longitude, latitude, height });

// An invented survey: an entrance at 1000 m, a passage down to a junction, a branch going nowhere,
// and the way on down to the deepest point at 820 m — drawn as three lines that meet at stations.
const entrance = station(25, 45.5, 1000);
const junction = station(25.0005, 45.5004, 950);
const deepest = station(25.0012, 45.5001, 820);
const lines: FlybyLinePoint[][] = [
  [entrance, station(25.0002, 45.5001, 980), junction],
  [junction, station(25.0003, 45.5008, 945), station(25.0001, 45.501, 948)],
  [junction, station(25.0008, 45.5003, 900), station(25.001, 45.5002, 860), deepest],
];

describe('the way down a cave\'s survey', () => {
  it('goes from the highest point to the deepest through the legs that join them, not down the side branch', () => {
    const route = surveyRoute(lines)!;
    expect(route[0]).toEqual({ lon: 25, lat: 45.5, height: 1000 });
    expect(route.at(-1)).toEqual({ lon: 25.0012, lat: 45.5001, height: 820 });
    expect(route).toHaveLength(6);
    expect(route.some((point) => point.lat === 45.501)).toBe(false);
  });

  it('takes the way the lines give whichever direction they were drawn in', () => {
    const reversed = lines.map((line) => [...line].reverse()).reverse();
    expect(surveyRoute(reversed)).toEqual(surveyRoute(lines));
  });

  it('finds no way when the deepest point is on lines that do not meet the highest', () => {
    const apart = [lines[0], lines[1], [station(25.01, 45.51, 900), station(25.011, 45.511, 700)]];
    expect(surveyRoute(apart)).toBeUndefined();
  });

  it('finds no way when nothing is drawn, or nothing goes down', () => {
    expect(surveyRoute([])).toBeUndefined();
    expect(surveyRoute([[station(25, 45.5, 0), station(25.001, 45.5, 0)]])).toBeUndefined();
  });
});

describe('the line along an imported file', () => {
  it('is its longest line, laid on the ground', () => {
    const short = [
      { longitude: 25, latitude: 45.5 },
      { longitude: 25.001, latitude: 45.5 },
    ];
    const long = [
      { longitude: 25.1, latitude: 45.5 },
      { longitude: 25.12, latitude: 45.51 },
      { longitude: 25.14, latitude: 45.5 },
    ];
    const route = trackRoute([short, long], (longitude) => longitude * 10);
    expect(route?.map((point) => [point.lon, point.lat])).toEqual([
      [25.1, 45.5],
      [25.12, 45.51],
      [25.14, 45.5],
    ]);
    // On the ground the scene draws, wherever that is.
    expect(route?.map((point) => point.height)).toEqual([25.1 * 10, 25.12 * 10, 25.14 * 10]);
    expect(trackRoute([[{ longitude: 25, latitude: 45 }]], () => 0)).toBeUndefined();
  });
});
