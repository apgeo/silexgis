// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from './client.ts';
import { unwrap, unwrapVoid } from './hooks/shared.ts';

/** A 429 as the server writes it: a coded problem and the wait it names. */
function tooMany() {
  return Promise.resolve({
    error: { code: 'rate_limited', detail: 'Slow down.' },
    response: new Response(null, { status: 429, headers: { 'Retry-After': '7' } }),
  });
}

describe('a rate-limited write with no answer body', () => {
  it('keeps the wait the server named, as a write with a body does', async () => {
    const withBody = await unwrap(tooMany()).catch((e: unknown) => e);
    const withoutBody = await unwrapVoid(tooMany()).catch((e: unknown) => e);

    expect(withBody).toBeInstanceOf(ApiError);
    expect((withBody as ApiError).retryAfterMs).toBe(7000);
    expect(withoutBody).toBeInstanceOf(ApiError);
    expect((withoutBody as ApiError).status).toBe(429);
    expect((withoutBody as ApiError).retryAfterMs).toBe(7000);
  });
});
