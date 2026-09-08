// SPDX-License-Identifier: AGPL-3.0-or-later
import { Point } from 'ol/geom';
import { afterEach, describe, expect, it } from 'vitest';
import { setOverburdenHighlight } from '../workspace/overburdenHighlight.ts';
import {
  attachOverburdenHighlight,
  getOverburdenHighlightSource,
} from './overburdenHighlightLayer.ts';

const reading = {
  caveId: 'cave-a',
  longitude: 24,
  latitude: 46,
  altitudeM: 820,
  label: '48.3 m of rock overhead',
};

afterEach(() => {
  setOverburdenHighlight(null);
});

describe('the overburden highlight overlay', () => {
  it('marks the one place the pressed reading came from, and writes the figure beside it', () => {
    // The flat map draws the plan of a vertical measurement: the mark stands over a passage some
    // tens of metres below it, and nothing in the plan says how far. So the figure is written
    // beside the mark rather than left to be read off the drawing.
    const detach = attachOverburdenHighlight();
    setOverburdenHighlight(reading);

    const features = getOverburdenHighlightSource().getFeatures();
    expect(features).toHaveLength(1);
    expect(features[0].getGeometry()).toBeInstanceOf(Point);
    expect(features[0].get('label')).toBe('48.3 m of rock overhead');
    detach();
  });

  it('marks whatever was pressed before the map was opened', () => {
    // The curve is read on a cave's page, which has no map on it. A viewer who presses a reading
    // and then opens the map would otherwise arrive at an empty one with nothing to press to fix
    // it, because the announcement went out before this layer existed.
    setOverburdenHighlight(reading);
    const detach = attachOverburdenHighlight();

    expect(getOverburdenHighlightSource().getFeatures()).toHaveLength(1);
    detach();
  });

  it('shows one reading at a time and clears when the panel unpicks', () => {
    // Two marks with nothing to say which point on the curve each belongs to would answer the
    // question worse than one of them alone.
    const detach = attachOverburdenHighlight();
    setOverburdenHighlight(reading);
    setOverburdenHighlight({ ...reading, longitude: 24.01, label: '12.0 m of rock overhead' });
    expect(getOverburdenHighlightSource().getFeatures()).toHaveLength(1);

    setOverburdenHighlight(null);
    expect(getOverburdenHighlightSource().getFeatures()).toHaveLength(0);
    detach();
  });

  it('stops drawing once the map page has gone', () => {
    // Detaching has to unsubscribe, not merely stop being looked at: a page that left the map and
    // then pressed another reading would otherwise still be filling a source nobody is drawing.
    const detach = attachOverburdenHighlight();
    detach();
    setOverburdenHighlight(reading);
    expect(getOverburdenHighlightSource().getFeatures()).toHaveLength(0);
  });
});
