// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/client.ts';
import { HELD_BACK_OFFLINE, readNotLanding } from './publicReadFreshness.ts';

/**
 * Whether a published page is still being kept up, as its reader means the question.
 *
 * <b>Two of the ways a page stops refreshing leave no failure behind</b>: a browser that knows it
 * is offline never sends the read, and a read the server asked to have back later is waiting
 * rather than failing. A page that looked only for a failure would go on looking live through
 * both, which is the one thing it must not do above a party somebody is waiting for.
 */
describe('whether the read feeding a published page is landing', () => {
  it('is landing while nothing has gone wrong', () => {
    expect(readNotLanding({ error: null })).toBeNull();
    expect(readNotLanding({ error: null, failureReason: null, isPaused: false })).toBeNull();
    expect(readNotLanding({ error: undefined })).toBeNull();
  });

  it('is not landing once a read has failed for good, and says which failure', () => {
    const failure = new TypeError('Failed to fetch');
    expect(readNotLanding({ error: failure, isPaused: true })).toBe(failure);
  });

  it('is not landing while the browser holds the read back for want of a connection', () => {
    expect(readNotLanding({ error: null, isPaused: true })).toBe(HELD_BACK_OFFLINE);
  });

  it('is not landing while it waits out a pause the server asked for', () => {
    const told = new ApiError(429, undefined, undefined, undefined, 30_000);
    expect(readNotLanding({ error: null, failureReason: told })).toBe(told);
  });

  it('is still landing through the few seconds of an ordinary retry', () => {
    // The twin of the case above: the same refusal without a named wait, a server fault and a
    // dropped request are all retried within seconds, and a warning that flashed for each of
    // them would be a warning nobody reads.
    for (const blip of [new ApiError(429), new ApiError(503), new TypeError('Failed to fetch')]) {
      expect(readNotLanding({ error: null, failureReason: blip, isPaused: false })).toBeNull();
    }
  });
});
