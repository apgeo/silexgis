// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { dragValue } from './pointer.ts';
import { ease, scenarioLength, scenarioProblems, type Scenario } from './scenario.ts';

const opening = { at: 0, kind: 'camera', to: { heading: 0 } } as const;

function scenario(items: Scenario['items']): Scenario {
  return { name: 'test', items };
}

describe('a presentation scenario', () => {
  it('is as long as its last item runs', () => {
    expect(
      scenarioLength(
        scenario([opening, { at: 2, duration: 5, kind: 'caption', text: 'a' }, { at: 6, kind: 'fade', to: 1 }]),
      ),
    ).toBe(7);
  });

  it('may overlap a camera move with a pointer action, and captions with anything', () => {
    expect(
      scenarioProblems(
        scenario([
          opening,
          { at: 0, duration: 10, kind: 'camera', to: { headingBy: 360 } },
          { at: 2, duration: 3, kind: 'basemap', name: 'OpenTopoMap' },
          { at: 2, duration: 3, kind: 'caption', text: 'a' },
          { at: 3, duration: 3, kind: 'caption', text: 'b' },
        ]),
      ),
    ).toEqual([]);
  });

  it('refuses two camera moves, or two pointer actions, at once', () => {
    const problems = scenarioProblems(
      scenario([
        opening,
        { at: 0, duration: 10, kind: 'camera', to: { headingBy: 360 } },
        { at: 5, duration: 2, kind: 'camera', to: { pitch: -10 } },
        { at: 2, duration: 3, kind: 'basemap', name: 'A' },
        { at: 4, duration: 3, kind: 'layer', label: 'B', on: true },
      ]),
    );
    expect(problems).toHaveLength(2);
    expect(problems[0]).toMatch(/^camera:/);
    expect(problems[1]).toMatch(/^pointer:/);
  });

  it('accepts items placed back to back despite rounding in their start times', () => {
    const items: Scenario['items'] = [opening];
    for (let i = 0; i < 12; i += 1) {
      items.push({ at: 14 + i * 3.4, duration: 3.4, kind: 'basemap', name: `B${i}` });
    }
    expect(scenarioProblems(scenario(items))).toEqual([]);
  });

  it('needs an opening camera position at 0 s', () => {
    expect(scenarioProblems(scenario([{ at: 1, kind: 'camera', to: {} }]))).toEqual([
      'the first camera item must start at 0 s, so the opening view is defined',
    ]);
  });

  it('refuses a colour it cannot read and a camera range of nothing', () => {
    expect(
      scenarioProblems(
        scenario([
          { at: 0, kind: 'camera', to: { range: 0 } },
          { at: 1, kind: 'modelColor', to: 'orange' },
        ]),
      ),
    ).toEqual([
      'item 0 puts the camera at a range that is not above zero',
      'item 1 names the colour "orange", which is not written #rrggbb',
    ]);
  });

  it('eases from 0 to 1 whatever the curve', () => {
    for (const kind of ['linear', 'in', 'out', 'inOut', 'smooth'] as const) {
      expect(ease(kind, 0)).toBe(0);
      expect(ease(kind, 1)).toBe(1);
      expect(ease(kind, 2)).toBe(1);
    }
    expect(ease('inOut', 0.5)).toBeCloseTo(0.5);
  });

  it('drags a slider through every listed value in turn', () => {
    expect(dragValue([100, 0, 100], 0, 'linear')).toBe(100);
    expect(dragValue([100, 0, 100], 0.5, 'linear')).toBe(0);
    expect(dragValue([100, 0, 100], 0.75, 'linear')).toBe(50);
    expect(dragValue([100, 0, 100], 1, 'linear')).toBe(100);
  });
});
