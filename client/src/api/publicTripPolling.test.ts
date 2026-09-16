// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { publicTripPollInterval, type PublicTripEnvelope, type TripTrackingState } from './hooks.ts';

/** A published trip as the query holds it, said only in the two parts this decision reads. */
const published = (state: TripTrackingState, pictures = 0): PublicTripEnvelope =>
  ({
    state,
    model:
      pictures === 0
        ? null
        : {
            pictures: Array.from({ length: pictures }, (_, i) => ({
              stationName: `p.g.${i}`,
              thumbnailUrl: `/api/v1/files/f${i}/thumbnail?size=480&token=sig`,
              caption: null,
            })),
          },
  }) as PublicTripEnvelope;

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
  it('asks again about the party only while it is underground', () => {
    expect(publicTripPollInterval(published('armed'))).toBeTypeOf('number');
    expect(publicTripPollInterval(published('closed'))).toBe(false);
    expect(publicTripPollInterval(published('off'))).toBe(false);
    expect(publicTripPollInterval(undefined)).toBe(false);
  });

  it('asks more gently than the surface a co-ordinator watches', () => {
    // The signed-in watch is read every 30s by somebody with the tab open and a session behind
    // every request. This one is read by however many people were handed the link.
    expect(publicTripPollInterval(published('armed'))).toBeGreaterThan(30_000);
  });

  /**
   * The one thing that goes stale in a finished trip's answer: the signature on every picture URL.
   *
   * Those URLs are spent lazily — a thumbnail is fetched when somebody reaches the station it hangs
   * at, which on an article about a trip that ended months ago may be twenty minutes after the page
   * loaded. Under the ten minutes a signature lives, or the reader gets broken pictures; well over
   * the live interval, because nothing else about the page is moving.
   */
  it('keeps asking for a closed trip that publishes photographs, slowly, and only for them', () => {
    const withPictures = publicTripPollInterval(published('closed', 1));

    expect(withPictures).toBeGreaterThan(publicTripPollInterval(published('armed')) as number);
    expect(withPictures).toBeLessThan(10 * 60_000);

    // The twin, and the ordinary case: a club that has published no photographs has nothing that
    // expires, so its page is read once and the tab goes quiet for good.
    expect(publicTripPollInterval(published('closed', 0))).toBe(false);
  });

  it('keeps the live interval for an armed trip, pictures or not', () => {
    // The party is what that page is being read for; pictures never slow it down.
    expect(publicTripPollInterval(published('armed', 3))).toBe(
      publicTripPollInterval(published('armed')),
    );
  });
});
