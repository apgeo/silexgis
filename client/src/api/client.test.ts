// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../i18n';
import { ApiError, retryQuery, sendsTheReadingLanguage } from './client.ts';

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
