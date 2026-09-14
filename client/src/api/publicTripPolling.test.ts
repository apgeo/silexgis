// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { publicTripPollInterval } from './hooks.ts';

/**
 * How often a published trip is asked about, and — the part that matters — when it stops being
 * asked about at all.
 *
 * A follow link is handed round a club and opened in tabs nobody closes. A page that went on
 * polling after the party came out would be an unknown number of strangers' browsers asking a
 * server about a finished trip indefinitely, on a route that has no session to rate-limit against
 * an account. Nothing else on this page enforces that, so it is checked here.
 */
describe('how a published trip is kept fresh', () => {
  it('asks again only while the party is underground', () => {
    expect(publicTripPollInterval('armed')).toBeTypeOf('number');
    expect(publicTripPollInterval('closed')).toBe(false);
    expect(publicTripPollInterval('off')).toBe(false);
    expect(publicTripPollInterval(undefined)).toBe(false);
  });

  it('asks more gently than the surface a co-ordinator watches', () => {
    // The signed-in watch is read every 30s by somebody with the tab open and a session behind
    // every request. This one is read by however many people were handed the link.
    expect(publicTripPollInterval('armed')).toBeGreaterThan(30_000);
  });
});
