// SPDX-License-Identifier: AGPL-3.0-or-later
import { LineString, Point } from 'ol/geom';
import { afterEach, describe, expect, it } from 'vitest';
import { setClosestApproachLine } from '../workspace/closestApproachLine.ts';
import {
  attachClosestApproachLine,
  getClosestApproachSource,
} from './closestApproachLayer.ts';

const line = {
  caveAId: 'cave-a',
  caveBId: 'cave-b',
  from: { longitude: 24, latitude: 46, altitudeM: 900 },
  to: { longitude: 24.003, latitude: 46, altitudeM: 750 },
  label: '268.2 m',
};

afterEach(() => {
  setClosestApproachLine(null);
});

describe('the closest-approach overlay', () => {
  it('draws the line and marks both of its ends', () => {
    // The line between two caves that nearly touch is a few pixels long at any zoom showing both
    // of them, so an unmarked one would be indistinguishable from a rendering artefact.
    const detach = attachClosestApproachLine();
    setClosestApproachLine(line);

    const features = getClosestApproachSource().getFeatures();
    expect(features.filter((f) => f.getGeometry() instanceof LineString)).toHaveLength(1);
    expect(features.filter((f) => f.getGeometry() instanceof Point)).toHaveLength(2);

    // The length is written beside the line, already worded: the flat map draws the plan of a
    // three-dimensional measurement, so a reader scaling the drawing would get a smaller number.
    expect(features.find((f) => f.get('label'))?.get('label')).toBe('268.2 m');
    detach();
  });

  it('draws whatever was measured before the map was opened', () => {
    // The measurement is made on a cave's page, which has no map on it. A viewer who then opens
    // the map must not arrive at an empty one with nothing to press.
    setClosestApproachLine(line);
    const detach = attachClosestApproachLine();
    expect(getClosestApproachSource().getFeatures()).toHaveLength(3);
    detach();
  });

  it('takes the line down when the panel clears it', () => {
    const detach = attachClosestApproachLine();
    setClosestApproachLine(line);
    setClosestApproachLine(null);
    expect(getClosestApproachSource().getFeatures()).toEqual([]);
    detach();
  });

  it('stops listening once the map has gone', () => {
    const detach = attachClosestApproachLine();
    detach();
    setClosestApproachLine(line);
    expect(getClosestApproachSource().getFeatures()).toEqual([]);
  });
});
