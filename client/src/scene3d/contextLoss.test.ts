// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';

import {
  CONTEXT_LOSS_REPEAT_WINDOW_MS,
  createContextLossPolicy,
} from './contextLoss.ts';

describe('what to do about a graphics context the browser has taken away', () => {
  const at = (times: number[]) => {
    let i = 0;
    return () => times[Math.min(i++, times.length - 1)];
  };

  it('rebuilds silently the first time, because one transient loss is the ordinary case', () => {
    const policy = createContextLossPolicy(at([0]));

    expect(policy.recordLoss()).toBe('recovering');
  });

  it('tells the viewer rather than rebuilding again when the loss repeats straight away', () => {
    // A machine that cannot hold a context at all: it goes as soon as the rebuilt scene draws.
    const policy = createContextLossPolicy(at([0, 1_000, 1_500]));

    expect(policy.recordLoss()).toBe('recovering');
    policy.recordRecovered();

    expect(policy.recordLoss()).toBe('lost');
  });

  it('rebuilds again for a loss far enough after the last one to be a new fault', () => {
    // The clock is read when the recovery is armed and again at the next loss, in that order.
    const policy = createContextLossPolicy(at([0, CONTEXT_LOSS_REPEAT_WINDOW_MS + 1]));

    expect(policy.recordLoss()).toBe('recovering');
    policy.recordRecovered();

    // A driver reset in the morning and another after lunch are two faults, not one repeating.
    expect(policy.recordLoss()).toBe('recovering');
  });

  it('treats a second report arriving before the rebuild has finished as the same loss', () => {
    // Browsers may raise the event more than once for a single loss, and a rebuild that has not
    // yet drawn has neither succeeded nor failed — counting the repeat as a fresh fault would
    // abandon a recovery that was still working.
    const policy = createContextLossPolicy(at([0]));

    expect(policy.recordLoss()).toBe('recovering');
    expect(policy.recordLoss()).toBe('lost');
  });

  it('counts a loss as the first one however long the scene has been open', () => {
    // No rebuild has happened, so there is no repeat window to be inside — a scene left open all
    // day and then losing its context must still recover by itself.
    const policy = createContextLossPolicy(at([9_000_000]));

    expect(policy.recordLoss()).toBe('recovering');
  });
});
