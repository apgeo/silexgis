// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../i18n';
import {
  ApiError,
  isSettledRefusal,
  lastReadETag,
  RETRY_AFTER_CEILING_MS,
  retryAfterOf,
  retryDelay,
  retryQuery,
  sendsTheReadingLanguage,
  threadsVersions,
} from './client.ts';

/**
 * <b>Whether the server has answered for good, which two different decisions now turn on.</b> The
 * retry policy reads it to decide whether to ask again; a screen still holding an earlier answer
 * reads it to decide whether to word a failure as a blip to wait out or as an answer to act on. A
 * page that stopped retrying while telling its reader it would refresh again by itself would be
 * the application contradicting itself over data that will never change, so the two are one rule
 * and this is where it is pinned.
 */
describe('a refusal the server has settled', () => {
  it('settles on a client error, whatever the screen holding it does next', () => {
    for (const status of [400, 401, 403, 404, 409, 422]) {
      expect(isSettledRefusal(new ApiError(status))).toBe(true);
    }
  });

  it('leaves everything that can still come good unsettled', () => {
    // Rate limiting clears when the window rolls over; a 5xx may be a single bad instant; a
    // network fault never reached the server at all and arrives as a plain Error.
    expect(isSettledRefusal(new ApiError(429))).toBe(false);
    expect(isSettledRefusal(new ApiError(500))).toBe(false);
    expect(isSettledRefusal(new ApiError(503))).toBe(false);
    expect(isSettledRefusal(new TypeError('Failed to fetch'))).toBe(false);
  });

  // The retry policy is this rule and not a second reading of it: a 4xx that stopped being
  // retried while some screen still called it transient is exactly the disagreement being avoided.
  it('is the same answer the retry policy acts on', () => {
    for (const error of [new ApiError(404), new ApiError(429), new ApiError(500), new Error('x')]) {
      expect(retryQuery(0, error)).toBe(!isSettledRefusal(error));
    }
  });
});

describe('retryQuery', () => {
  it('gives up immediately on a client error', () => {
    // The cost of getting this wrong is not the wasted calls: the page stays in its loading
    // state for the whole backoff, so a missing route reads as a slow one.
    for (const status of [400, 401, 403, 404, 422]) {
      expect(retryQuery(0, new ApiError(status))).toBe(false);
    }
  });

  it('waits out rate limiting and transient server faults', () => {
    // 429 clears when the fixed window rolls over, and a 5xx may be a single bad instant.
    expect(retryQuery(0, new ApiError(429))).toBe(true);
    expect(retryQuery(0, new ApiError(500))).toBe(true);
    expect(retryQuery(0, new ApiError(503))).toBe(true);
    // A network failure never reaches the server, so it arrives as a plain Error.
    expect(retryQuery(0, new TypeError('Failed to fetch'))).toBe(true);
  });

  it('stops retrying a retryable failure after three attempts', () => {
    expect(retryQuery(2, new ApiError(500))).toBe(true);
    expect(retryQuery(3, new ApiError(500))).toBe(false);
  });
});

describe('the language every request is made in', () => {
  /** What the middleware installed on the client puts on a request while that language is read. */
  async function headerSentWhileReadingIn(language: string) {
    await i18n.changeLanguage(language);
    // Absolute because a Request is being built outside a browser; the path is immaterial here.
    const request = new Request('http://localhost/api/v1/notifications/unread-count');
    sendsTheReadingLanguage.onRequest({ request });
    return request.headers.get('Accept-Language');
  }

  it('is the one the reader chose here, not the one their browser was installed in', async () => {
    // Some of what this client shows is written by the server — the lines in the inbox are — and
    // the server reads this header before it reads the language the account last saved. Without
    // it, somebody reading a Romanian site in an English browser gets the page chrome in Romanian
    // and every line the server wrote in English, on the one screen.
    expect(await headerSentWhileReadingIn('ro')).toBe('ro');
    expect(await headerSentWhileReadingIn('en')).toBe('en');
  });
});

/**
 * <b>The version an answer carries, and where it is kept.</b> A write is checked against the
 * version the caller holds, and a client that learned versions only from reads was one behind
 * after every save: the second of two saves a second apart was refused against the version the
 * first had produced, and the page's answer was to call no save finished until it had read the
 * trip back. The server now hands the version over with every answer that moved the row, and
 * this is the one place that decides which resource an answer's tag belongs to.
 */
describe('the version an answer carries', () => {
  const origin = 'http://localhost';

  function answered(method: string, path: string, init: ResponseInit) {
    threadsVersions.onResponse({
      request: new Request(origin + path, { method }),
      response: new Response(null, init),
    });
  }

  /** What the middleware puts on a request about to be sent. */
  function carriedOn(method: string, path: string) {
    const request = new Request(origin + path, { method });
    threadsVersions.onRequest({ request });
    return request.headers.get('If-Match');
  }

  it("is filed under what was read and replayed on that resource's next full update or delete", () => {
    answered('GET', '/api/v1/trip-logs/t-read', { status: 200, headers: { ETag: '"4"' } });
    expect(lastReadETag('/api/v1/trip-logs/t-read')).toBe('"4"');
    expect(carriedOn('PUT', '/api/v1/trip-logs/t-read')).toBe('"4"');
    expect(carriedOn('DELETE', '/api/v1/trip-logs/t-read')).toBe('"4"');
    // A post beside the resource is an action; it asks for the version itself when it wants one,
    // so that an action route which never asked for a precondition is not handed one.
    expect(carriedOn('POST', '/api/v1/trip-logs/t-read/state')).toBeNull();
  });

  it('is handed over by a write, so the next write needs no read in between', () => {
    answered('GET', '/api/v1/trip-logs/t-write', { status: 200, headers: { ETag: '"4"' } });
    answered('PUT', '/api/v1/trip-logs/t-write', { status: 200, headers: { ETag: '"5"' } });
    expect(carriedOn('PUT', '/api/v1/trip-logs/t-write')).toBe('"5"');
  });

  it('is filed under the resource a creation names, not under the collection posted to', () => {
    answered('POST', '/api/v1/trip-logs/', {
      status: 201,
      headers: { ETag: '"1"', Location: '/api/v1/trip-logs/t-new' },
    });
    expect(lastReadETag('/api/v1/trip-logs/t-new')).toBe('"1"');
    expect(lastReadETag('/api/v1/trip-logs/')).toBeUndefined();
  });

  it('is filed where an action says it belongs, and nowhere when the action does not say', () => {
    answered('GET', '/api/v1/trip-logs/t-act', { status: 200, headers: { ETag: '"4"' } });
    answered('POST', '/api/v1/trip-logs/t-act/state', {
      status: 200,
      headers: { ETag: '"6"', 'Content-Location': '/api/v1/trip-logs/t-act' },
    });
    expect(lastReadETag('/api/v1/trip-logs/t-act')).toBe('"6"');
    expect(lastReadETag('/api/v1/trip-logs/t-act/state')).toBeUndefined();

    // A tag an answer does not attribute could be anybody's: it is dropped rather than guessed
    // at, and what was held stays held.
    answered('POST', '/api/v1/trip-logs/t-act/invitations/promote', {
      status: 200,
      headers: { ETag: '"7"' },
    });
    expect(lastReadETag('/api/v1/trip-logs/t-act')).toBe('"6"');
    expect(lastReadETag('/api/v1/trip-logs/t-act/invitations/promote')).toBeUndefined();
  });
});

/** A refusal as it arrives, said only in the one header this rule reads. */
const refusedWith = (retryAfter?: string) =>
  new Response(null, {
    status: 429,
    headers: retryAfter === undefined ? {} : { 'Retry-After': retryAfter },
  });

/**
 * <b>A server that says when to come back is taken at its word.</b> A read refused for being
 * asked too often is retried, and retried on this client's own short pacing it spends three more
 * requests of the allowance it was just told is used up. So the wait the response names is
 * carried on the error and is the wait the retry rule uses — and where the response names none,
 * nothing about the pacing changes.
 */
describe('the wait a refusal names', () => {
  it('is read as whole seconds, the way a rate limiter writes it', () => {
    expect(retryAfterOf(refusedWith('30'))).toBe(30_000);
    expect(retryAfterOf(refusedWith(' 7 '))).toBe(7_000);
    expect(retryAfterOf(refusedWith('0'))).toBe(0);
  });

  it('is read as a date, the way a proxy in front of a server under maintenance writes it', () => {
    const now = Date.parse('2026-09-14T12:00:00Z');
    expect(retryAfterOf(refusedWith('Mon, 14 Sep 2026 12:00:45 GMT'), now)).toBe(45_000);
    // A date already behind the clock means "now", never a negative wait.
    expect(retryAfterOf(refusedWith('Mon, 14 Sep 2026 11:59:00 GMT'), now)).toBe(0);
  });

  it('is no wait at all where the header is absent or cannot be read', () => {
    // The twin of the two above: a value this could not read must fall back to the ordinary
    // pacing exactly as an absent header does, and never become a wait of zero or of NaN.
    expect(retryAfterOf(refusedWith())).toBeUndefined();
    for (const written of ['', 'soon', '-5', '1.5', '30 seconds']) {
      expect(retryAfterOf(refusedWith(written))).toBeUndefined();
    }
    // A reply that did not come from a browser's transport may carry no header list at all.
    expect(retryAfterOf({})).toBeUndefined();
  });

  it('is never longer than a screen can reasonably be left waiting', () => {
    const now = Date.parse('2026-09-14T12:00:00Z');
    expect(retryAfterOf(refusedWith('86400'))).toBe(RETRY_AFTER_CEILING_MS);
    expect(retryAfterOf(refusedWith('Tue, 15 Sep 2026 12:00:00 GMT'), now)).toBe(
      RETRY_AFTER_CEILING_MS,
    );
  });

  it('is the wait before every further attempt, however many have failed', () => {
    const told = new ApiError(429, undefined, undefined, undefined, 30_000);
    expect(retryDelay(0, told)).toBe(30_000);
    expect(retryDelay(1, told)).toBe(30_000);
    expect(retryDelay(2, told)).toBe(30_000);
    // Told to come straight back is still an answer, and not the absence of one.
    expect(retryDelay(0, new ApiError(503, undefined, undefined, undefined, 0))).toBe(0);
  });

  it('leaves the ordinary pacing alone where nothing was named', () => {
    // A second, two, four — the wait every read in the application had before this rule, and
    // still has for a refusal without the header, a server fault and a request that never landed.
    for (const error of [new ApiError(429), new ApiError(503), new TypeError('Failed to fetch')]) {
      expect([0, 1, 2].map((attempt) => retryDelay(attempt, error))).toEqual([1000, 2000, 4000]);
    }
    expect(retryDelay(10, new ApiError(503))).toBe(30_000);
  });
});
