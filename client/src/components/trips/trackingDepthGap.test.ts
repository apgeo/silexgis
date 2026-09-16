// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  DEPTH_GAP_FLOOR_M,
  depthGapToleranceM,
  recordedDepthGap,
  trackingDepthGap,
} from './trackingDepthGap.ts';

/** A candidate as the resolution route answers one: a station, how deep it is, how far off it is. */
function candidate(stationName: string, depthM: number, deltaM: number) {
  return { stationName, surveyName: null, depthM, deltaM };
}

/**
 * How far is far enough to be worth saying.
 *
 * <b>Every case here is a cave, not a number.</b> The rule has to hold at both ends of the range
 * caves come in — a 40 m sump bypass and a 900 m system with fifty metres of pitch between
 * stations — and a threshold that is right for one is wrong for the other in a way that shows up
 * only as silence or as noise. So the cases are named for the cave they describe.
 */
describe('how far a station may sit from the depth that was reported', () => {
  it('holds a floor near the surface, where a proportion would collapse', () => {
    // A quarter of 2 m is 50 cm, so a proportion on its own would remark on a party reported two
    // metres inside an entrance being placed at the entrance station. The floor is what stops it.
    expect(depthGapToleranceM(2)).toBe(DEPTH_GAP_FLOOR_M);
    expect(depthGapToleranceM(0)).toBe(DEPTH_GAP_FLOOR_M);
  });

  it('opens up as the cave gets deeper, where a fixed figure would cry wolf', () => {
    // 600 m down in a big system: a quarter of that is 150 m, which is wide — and has to be, or
    // every report in a survey with fifty metres between rebelays carries a warning.
    expect(depthGapToleranceM(600)).toBe(150);
    // The sign is nothing. −120 and 120 are the same place, which is what the report field says.
    expect(depthGapToleranceM(-120)).toBe(depthGapToleranceM(120));
  });

  it('crosses from one to the other at sixty metres', () => {
    // Below 60 m the floor is the larger; above it the proportion is. Asserted because it is the
    // one place a change to either constant silently changes the other's reach.
    expect(depthGapToleranceM(59)).toBe(DEPTH_GAP_FLOOR_M);
    expect(depthGapToleranceM(61)).toBeGreaterThan(DEPTH_GAP_FLOOR_M);
  });
});

describe('the gap between a reported depth and the station it was recorded at', () => {
  it('says nothing about a survey that simply has no station at that exact depth', () => {
    // 120 m reported, a station 40 cm away. This is the ordinary case and the one a noisy rule
    // would ruin: a coordinator who is warned every time stops reading the warnings.
    const gap = trackingDepthGap(120, candidate('p.g.119', 119.6, 0.4));

    expect(gap).not.toBeNull();
    expect(gap!.wide).toBe(false);
    expect(gap!.gapM).toBe(0.4);
  });

  it('says so when a mistyped depth lands at the bottom of the cave', () => {
    // The twin of the case above, and the defect itself: −1200 typed for −120 against a 140 m
    // cave. Resolution has no tolerance in it, so the deepest station there is comes back as
    // confidently as a station half a metre away would.
    const gap = trackingDepthGap(1200, candidate('p.g.140', 139.4, 1060.6));

    expect(gap!.wide).toBe(true);
    // And it carries what the row has to be able to say: where it landed, and how deep that is.
    expect(gap!.stationName).toBe('p.g.140');
    expect(gap!.stationDepthM).toBe(139.4);
  });

  it('stays quiet about a thinly-surveyed deep system and speaks about a shallow one', () => {
    // The same fifteen-metre gap, in two caves. At 600 m it is one pitch and means nothing; at
    // 30 m it is half the cave — and the tolerance still holds it, because fifteen metres is the
    // floor rather than the thing being measured against.
    expect(trackingDepthGap(600, candidate('deep.44', 585, 15))!.wide).toBe(false);
    expect(trackingDepthGap(30, candidate('shallow.9', 15, 15))!.wide).toBe(false);
    // One metre more in the shallow cave, and it is past the floor.
    expect(trackingDepthGap(30, candidate('shallow.9', 14, 16))!.wide).toBe(true);
    // A quarter of 600 is 150, so the deep cave has to be a very long way out before it speaks —
    // which is the proportion doing its job rather than an oversight.
    expect(trackingDepthGap(600, candidate('deep.1', 440, 160))!.wide).toBe(true);
  });

  it('answers nothing at all rather than a sound-looking zero when there is no candidate', () => {
    // A reader who was never given the station, a filter that has moved since the report was
    // taken, an answer that has not come back yet: all three arrive as "no candidate", and none of
    // them is evidence that the position is sound. Silence is the only honest reading.
    expect(trackingDepthGap(1200, undefined)).toBeNull();
    expect(trackingDepthGap(null, candidate('p.g.140', 139.4, 1060.6))).toBeNull();
  });
});

/**
 * Measuring the gap for a report that is already on the log.
 *
 * <b>The hard part is not the arithmetic, it is knowing when the arithmetic is about this row.</b>
 * A stored report records a station and a number; it records nothing about the model, datum or
 * filter it was resolved under, and all three are editable while a party is underground. Asking
 * today's configuration what a stored number means therefore answers a different question, and the
 * two coincide only while nothing has moved. Every case below is one of the ways they come apart.
 */
describe('the gap on a report already recorded', () => {
  const WATCH_MODEL = 'model-1';
  /** A report of 120 m that landed forty centimetres from its station — correct, and quiet. */
  const exact = { askedDepthM: 120, stationName: 'p.g.119', surveyModelId: WATCH_MODEL };
  /** And the defect: 1200 typed for 120, stored at the bottom of a 140 m cave. */
  const mistyped = { askedDepthM: 1200, stationName: 'p.g.140', surveyModelId: WATCH_MODEL };

  it('measures a report that nothing has moved under, and says when it is far out', () => {
    // The positive twin every silence below is measured against: with the configuration still the
    // one the report was resolved under, the row does speak, and says how far out it was.
    const gap = recordedDepthGap(mistyped, WATCH_MODEL, [candidate('p.g.140', 139.4, 1060.6)]);

    expect(gap!.wide).toBe(true);
    expect(gap!.gapM).toBe(1060.6);
  });

  it('measures it and stays quiet when the report all but landed on its station', () => {
    const gap = recordedDepthGap(exact, WATCH_MODEL, [candidate('p.g.119', 119.6, 0.4)]);

    expect(gap).not.toBeNull();
    expect(gap!.wide).toBe(false);
  });

  it('says nothing about a report made against a survey the watch has since left', () => {
    // A corrected survey arriving mid-trip is a supported act, and station names commonly survive
    // a re-survey — so the new model may well hold a station of the same name near the mistyped
    // depth, and would answer a small, reassuring distance about a party stored a kilometre from
    // where they were reported. A depth is metres below a datum, the datum belongs to a model, and
    // a number measured against a model that has been replaced is not a depth in this one.
    // Asserted with an answer that is wide as well as one that is narrow, so that the silence is
    // the guard refusing to measure across two models and not the tolerance happening to be quiet.
    expect(
      recordedDepthGap({ ...mistyped, surveyModelId: 'the-older-survey' }, WATCH_MODEL, [
        candidate('p.g.140', 139.4, 1060.6),
      ]),
    ).toBeNull();
    expect(
      recordedDepthGap({ ...mistyped, surveyModelId: 'the-older-survey' }, WATCH_MODEL, [
        candidate('p.g.140', 1199.5, 0.5),
      ]),
    ).toBeNull();
  });

  it('says nothing once the datum has moved under a report that was exact', () => {
    // The reference station is set to the real entrance thirty metres lower — an ordinary edit,
    // made from the card at the top of the same screen. Every station is thirty metres deeper than
    // it was, so 120 m now resolves to a different station entirely. The recorded station is still
    // in the answer, just no longer first; matching it anywhere in the list would draw a thirty
    // metre warning on a report that was forty centimetres out, and do it to every row at once.
    const gap = recordedDepthGap(exact, WATCH_MODEL, [
      candidate('p.g.150', 120.2, 0.2),
      candidate('p.g.119', 89.6, 30.4),
    ]);

    expect(gap).toBeNull();
  });

  it('says nothing when the filter no longer offers the station that was recorded', () => {
    expect(recordedDepthGap(mistyped, WATCH_MODEL, [candidate('q.4', 131, 1069)])).toBeNull();
  });

  it('says nothing while the answer has not come back, and nothing without a place to measure', () => {
    // An unanswered question is not evidence that a position is sound, and neither is a report
    // that named a station outright rather than a depth.
    expect(recordedDepthGap(mistyped, WATCH_MODEL, undefined)).toBeNull();
    expect(recordedDepthGap(mistyped, WATCH_MODEL, [])).toBeNull();
    expect(
      recordedDepthGap({ ...mistyped, askedDepthM: null }, WATCH_MODEL, [
        candidate('p.g.140', 139.4, 1060.6),
      ]),
    ).toBeNull();
    expect(
      recordedDepthGap({ ...mistyped, stationName: null }, WATCH_MODEL, [
        candidate('p.g.140', 139.4, 1060.6),
      ]),
    ).toBeNull();
  });

  it('says nothing when either side names no model at all', () => {
    // Nothing writes a station without the survey it belongs to, and a watch on no model resolves
    // nothing — so either shape means something upstream is not what it claims, and refusing to
    // measure is the safe end of that rather than treating two nulls as agreement.
    const answer = [candidate('p.g.140', 139.4, 1060.6)];
    expect(recordedDepthGap({ ...mistyped, surveyModelId: null }, WATCH_MODEL, answer)).toBeNull();
    expect(recordedDepthGap({ ...mistyped, surveyModelId: null }, null, answer)).toBeNull();
    expect(recordedDepthGap(mistyped, null, answer)).toBeNull();
    expect(recordedDepthGap(mistyped, undefined, answer)).toBeNull();
  });
});
