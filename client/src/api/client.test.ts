// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError, retryQuery } from './client.ts';

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
