// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { accessKeyIsUsable, accessKeyLifetimeChoices } from './accessKeys.ts';

describe('accessKeyLifetimeChoices', () => {
  it('offers what the server offers, shortest first, starting on the stated default', () => {
    expect(accessKeyLifetimeChoices({ maxDays: 365, offeredDays: [365, 30, 90], defaultDays: 365 })).toEqual({
      days: [30, 90, 365],
      initial: 365,
    });
  });

  it('drops a lifetime above the installation ceiling even if it was listed', () => {
    const { days } = accessKeyLifetimeChoices({ maxDays: 90, offeredDays: [30, 90, 365], defaultDays: 90 });
    expect(days).toEqual([30, 90]);
  });

  it('starts on the longest offered lifetime when the stated default is not among them', () => {
    expect(accessKeyLifetimeChoices({ maxDays: 60, offeredDays: [30, 90], defaultDays: 365 }).initial).toBe(30);
  });

  it('has nothing to start on when nothing fits', () => {
    expect(accessKeyLifetimeChoices({ maxDays: 10, offeredDays: [30], defaultDays: 30 })).toEqual({
      days: [],
      initial: undefined,
    });
  });
});

describe('accessKeyIsUsable', () => {
  it('is true only for an active key', () => {
    expect(accessKeyIsUsable({ state: 'active' })).toBe(true);
    expect(accessKeyIsUsable({ state: 'expired' })).toBe(false);
    expect(accessKeyIsUsable({ state: 'revoked' })).toBe(false);
  });
});
