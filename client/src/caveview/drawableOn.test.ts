// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { drawableOn, placeOnModel } from './drawableOn.ts';

const MODEL = 'model-1';
const OTHER = 'model-2';

describe('drawableOn', () => {
  it('draws a place on the survey it was measured in, and on no other', () => {
    expect(drawableOn(MODEL, MODEL)).toBe(true);
    expect(drawableOn(MODEL, OTHER)).toBe(false);
  });

  /**
   * The shape a deleted survey leaves behind, which is the one the four copies of this comparison
   * disagreed about. Removing a survey model nulls the pointer on every position that named it and
   * leaves the station name standing, so this is an ordinary record rather than a broken row — and
   * nothing can check that name against the drawing on screen any more.
   */
  it('refuses a place whose survey is gone, and a surface that does not know its own survey', () => {
    expect(drawableOn(null, MODEL)).toBe(false);
    expect(drawableOn(undefined, MODEL)).toBe(false);
    expect(drawableOn(MODEL, null)).toBe(false);
    expect(drawableOn(MODEL, undefined)).toBe(false);
    // Two absences are not a match either: nothing is drawn on nothing.
    expect(drawableOn(null, null)).toBe(false);
  });
});

describe('placeOnModel', () => {
  it('answers the station, the depth, and nothing at all, in the ordinary case', () => {
    expect(placeOnModel({ stationName: 'p.g.7', depthM: null, surveyModelId: MODEL }, MODEL))
      .toEqual({ kind: 'station', station: 'p.g.7' });
    expect(placeOnModel({ stationName: null, depthM: 35, surveyModelId: MODEL }, MODEL))
      .toEqual({ kind: 'depth', depthM: 35 });
    // No place claimed at all — which is a different thing from a place that cannot be drawn, and
    // is left to the caller to say in its own words.
    expect(placeOnModel({ stationName: null, depthM: null, surveyModelId: null }, MODEL)).toBeNull();
    // An empty station name is not a station.
    expect(placeOnModel({ stationName: '', depthM: null, surveyModelId: MODEL }, MODEL)).toBeNull();
  });

  it('says a place measured elsewhere is elsewhere rather than passing its name through', () => {
    expect(placeOnModel({ stationName: 'p.g.7', depthM: null, surveyModelId: OTHER }, MODEL))
      .toEqual({ kind: 'otherModel' });
    // The survey was deleted: the name outlives it and means nothing on the model now drawn.
    expect(placeOnModel({ stationName: 'p.g.7', depthM: null, surveyModelId: null }, MODEL))
      .toEqual({ kind: 'otherModel' });
    // A depth goes with its station rather than surviving the test: metres below a datum that has
    // been replaced is not a depth in the survey now in use.
    expect(placeOnModel({ stationName: null, depthM: 35, surveyModelId: OTHER }, MODEL))
      .toEqual({ kind: 'otherModel' });
  });
});
