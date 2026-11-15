// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { cameraPoseAt, modelTintAt, type CameraLeg, type Pose } from './runner.ts';

const pose = (heading: number, range: number): Pose => ({ heading, pitch: -30, range, lift: 0, aboveGround: null });

function legs(...spans: { at: number; duration: number; to: Pose; ease?: CameraLeg['ease'] }[]): CameraLeg[] {
  let from = pose(0, 1);
  return spans.map((span) => {
    const leg = { at: span.at, duration: span.duration, from, to: span.to, ease: span.ease };
    from = span.to;
    return leg;
  });
}

/** Degrees of heading turned per second around time `t` of move `index`. */
function turnRate(all: CameraLeg[], index: number, t: number): number {
  const h = 1e-4;
  return (cameraPoseAt(all, index, t + h).vector[0] - cameraPoseAt(all, index, t - h).vector[0]) / (2 * h);
}

describe('the camera of a presentation', () => {
  it('changes the range in equal proportions over equal times', () => {
    const [leg] = legs({ at: 0, duration: 10, to: pose(0, 100), ease: 'linear' });
    expect(Math.exp(cameraPoseAt([leg], 0, 5).vector[2])).toBeCloseTo(10);
  });

  it('flies smooth moves placed back to back as one curve, without stopping at the join', () => {
    const all = legs(
      { at: 0, duration: 10, to: pose(90, 3), ease: 'smooth' },
      { at: 10, duration: 20, to: pose(450, 3), ease: 'smooth' },
    );
    expect(cameraPoseAt(all, 0, 10).vector[0]).toBeCloseTo(90);
    // The same rate on both sides of the join, and not zero.
    const before = turnRate(all, 0, 10 - 1e-3);
    const after = turnRate(all, 1, 10 + 1e-3);
    expect(before).toBeGreaterThan(5);
    expect(after).toBeCloseTo(before, 1);
    // At rest at the very start and the very end.
    expect(Math.abs(turnRate(all, 0, 1e-3))).toBeLessThan(0.1);
    expect(Math.abs(turnRate(all, 1, 30 - 1e-3))).toBeLessThan(0.1);
  });

  it('stops at the join of a smooth move and one that is not', () => {
    const all = legs(
      { at: 0, duration: 10, to: pose(90, 3), ease: 'smooth' },
      { at: 10, duration: 10, to: pose(180, 3), ease: 'inOut' },
    );
    expect(Math.abs(turnRate(all, 0, 10 - 1e-3))).toBeLessThan(0.1);
  });
});

describe('the colour of the walls in a presentation', () => {
  it('fades a colour in from the walls’ own, then changes it, then gives them back their own', () => {
    const items = [
      { at: 2, duration: 2, kind: 'modelColor', to: '#ff0000', ease: 'linear' },
      { at: 6, duration: 2, kind: 'modelColor', to: '#0000ff', strength: 1, ease: 'linear' },
      { at: 10, duration: 2, kind: 'modelColor', to: null, ease: 'linear' },
    ] as const;
    expect(modelTintAt(items, 1)).toBeUndefined();
    expect(modelTintAt(items, 3)).toEqual({ red: 1, green: 0, blue: 0, amount: 0.4 });
    expect(modelTintAt(items, 7)).toEqual({ red: 0.5, green: 0, blue: 0.5, amount: 0.9 });
    expect(modelTintAt(items, 11)).toMatchObject({ red: 0, blue: 1, amount: 0.5 });
    expect(modelTintAt(items, 12)).toBeUndefined();
  });
});
