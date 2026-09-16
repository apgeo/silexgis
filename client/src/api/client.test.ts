// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../i18n';
import { ApiError, isSettledRefusal, retryQuery, sendsTheReadingLanguage } from './client.ts';

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
