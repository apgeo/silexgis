// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { ProcessingProfile } from '../../api/hooks.ts';
import { changedProfileKeys, preferredProfileId } from './recordingProfile.ts';

/** Invented defaults and profiles in the shape a step's schema and the profile list answer with. */
const defaults = { values: { filter_points: { decimation: 0.01 }, lidar: { nr_iter: 1000, range: 70 } } };

function profile(id: string, values: unknown, builtIn = false): ProcessingProfile {
  return {
    id,
    stepId: 'hdmapping.odometry',
    toolVersion: null,
    name: id,
    description: null,
    scope: builtIn ? 'installation' : 'personal',
    ownerUserId: null,
    cavingGroupId: null,
    values: values as ProcessingProfile['values'],
    origin: builtIn ? 'builtIn' : 'edited',
    builtIn,
    mayEdit: !builtIn,
    mayDelete: !builtIn,
    createdAt: '2026-11-08T00:00:00Z',
    updatedAt: '2026-11-08T00:00:00Z',
  };
}

describe('what a profile changes from the defaults', () => {
  it('names only the keys whose value differs, and a key the defaults lack', () => {
    expect(changedProfileKeys({ values: {} }, defaults)).toEqual([]);
    expect(changedProfileKeys({ values: { lidar: { nr_iter: 1000 } } }, defaults)).toEqual([]);
    expect(
      changedProfileKeys({ values: { filter_points: { decimation: 0.05 }, lidar: { nr_iter: 1000, extra: 1 } } }, defaults),
    ).toEqual(['values.filter_points.decimation', 'values.lidar.extra']);
  });
});

describe('the profile an upload pre-selects', () => {
  const builtInDefaults = profile('defaults', { values: {} }, true);
  const fast = profile('fast', { values: { lidar: { nr_iter: 300 } } }, true);
  const mine = profile('mine', { values: { lidar: { range: 30 } } });
  const offered = [fast, builtInDefaults, mine];

  it('is the one chosen last while it is still offered', () => {
    const remembered = [{ stepId: 'hdmapping.odometry', profileId: 'mine', chosenAt: '2026-11-08T00:00:00Z' }];
    expect(preferredProfileId('hdmapping.odometry', offered, remembered, defaults)).toBe('mine');
  });

  it('falls back to the built-in that changes nothing, and to none when there is no such profile', () => {
    const gone = [{ stepId: 'hdmapping.odometry', profileId: 'deleted', chosenAt: '2026-11-08T00:00:00Z' }];
    expect(preferredProfileId('hdmapping.odometry', offered, gone, defaults)).toBe('defaults');
    expect(preferredProfileId('hdmapping.odometry', [fast, mine], undefined, defaults)).toBeNull();
  });
});
