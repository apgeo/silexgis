// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from './client.ts';
import {
  publicLiveTripsPollInterval,
  publicTripPollInterval,
  publicTripRefetchInterval,
  type PublicLiveTripList,
  type PublicTripEnvelope,
  type TripTrackingState,
} from './hooks.ts';

/** A published trip as the query holds it, said only in the parts this decision reads. */
const published = (state: TripTrackingState, pictures = 0, maps = 0): PublicTripEnvelope =>
  ({
    state,
    model:
      pictures === 0 && maps === 0
        ? null
        : {
            pictures: Array.from({ length: pictures }, (_, i) => ({
              stationName: `p.g.${i}`,
              thumbnailUrl: `/api/v1/files/f${i}/thumbnail?size=480&token=sig`,
              caption: null,
            })),
            rasterMaps: Array.from({ length: maps }, (_, i) => ({
              title: `Sheet ${i}`,
              viewKind: 'plan',
              imageUrl: `/api/v1/files/m${i}/thumbnail?size=1200&token=sig`,
              points: [],
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

  /**
   * The map sheets go stale the same way and are kept fresh by the same read — sharper,
   * even: a sheet's picture is fetched when its tab is first opened, which on an article
   * about a finished trip is however long after the page loaded the reader took to scroll
   * there. One interval, deliberately not a second scheme.
   */
  it('keeps asking for a closed trip that carries map sheets, at the picture rate', () => {
    const withMaps = publicTripPollInterval(published('closed', 0, 1));

    expect(withMaps).toBe(publicTripPollInterval(published('closed', 1)));

    // The twin: with neither kind of signed URL in the envelope, nothing expires and the
    // page is read once — a model alone is not what keeps a tab asking.
    expect(publicTripPollInterval(published('closed', 0, 0))).toBe(false);
  });

  it('keeps the live interval for an armed trip, pictures or not', () => {
    // The party is what that page is being read for; pictures never slow it down.
    expect(publicTripPollInterval(published('armed', 3))).toBe(
      publicTripPollInterval(published('armed')),
    );
  });
});

/**
 * The end of a publication, as the query lives through it.
 *
 * A share's grace runs out or a link is taken back, and from then on the route answers 404 for
 * good while the query still holds the last envelope it read. The interval is decided on both:
 * an answer that will never change stops the clock, and a fault that may — no signal, a server
 * coming back — keeps it, so the page resumes by itself.
 */
describe('when a published trip stops being asked about', () => {
  it('stops for good once the link has been refused, whatever the last envelope said', () => {
    expect(publicTripRefetchInterval(published('armed'), new ApiError(404))).toBe(false);
    expect(publicTripRefetchInterval(published('closed', 0, 1), new ApiError(404))).toBe(false);
    expect(publicTripRefetchInterval(published('closed', 2), new ApiError(410))).toBe(false);
  });

  it('keeps asking through a fault that may clear by itself', () => {
    const armed = publicTripPollInterval(published('armed'));
    expect(publicTripRefetchInterval(published('armed'), new TypeError('Failed to fetch'))).toBe(armed);
    expect(publicTripRefetchInterval(published('armed'), new ApiError(503))).toBe(armed);
    expect(publicTripRefetchInterval(published('armed'), new ApiError(429))).toBe(armed);
    expect(publicTripRefetchInterval(published('armed'), null)).toBe(armed);
  });
});

/** The parties being followed in the cave, said only in the parts this decision reads. */
const followed = (...states: TripTrackingState[]): PublicLiveTripList =>
  ({
    trips: states.map((state, index) => ({ tripLogId: `trip-${index}`, state })),
    more: false,
  }) as unknown as PublicLiveTripList;

/**
 * The list of parties being followed is a statement about now, and it is kept fresh only while
 * "now" can still change: a row whose watch has closed is a party that is out, and a list of such
 * rows is read once. A refusal that will not change stops the clock for good.
 */
describe('how the parties being followed are kept fresh', () => {
  it('is re-read at the followed pace while any party is still underground', () => {
    expect(publicLiveTripsPollInterval(followed('closed', 'armed'), null)).toBe(
      publicTripPollInterval(published('armed')),
    );
  });

  it('is read once when every watch in it has closed, and when it is empty', () => {
    expect(publicLiveTripsPollInterval(followed('closed', 'closed'), null)).toBe(false);
    expect(publicLiveTripsPollInterval(followed(), null)).toBe(false);
    expect(publicLiveTripsPollInterval(undefined, null)).toBe(false);
  });

  it('stops for good once the link has been refused, and not for a fault that may clear', () => {
    expect(publicLiveTripsPollInterval(followed('armed'), new ApiError(404))).toBe(false);
    expect(publicLiveTripsPollInterval(followed('armed'), new ApiError(503))).toBe(
      publicTripPollInterval(published('armed')),
    );
  });
});
