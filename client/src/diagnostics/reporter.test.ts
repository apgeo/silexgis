// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { getUser } = vi.hoisted(() => ({ getUser: vi.fn() }));
vi.mock('../auth/auth.tsx', () => ({ userManager: { getUser } }));

/** The reporter as a built application loads it: the development sink does not exist there. */
async function builtReporter() {
  vi.resetModules();
  vi.stubEnv('DEV', false);
  return import('./reporter.ts');
}

describe('the reporter in a built application', () => {
  let fetchSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    fetchSpy = vi.fn(() => Promise.resolve(new Response(null, { status: 204 })));
    vi.stubGlobal('fetch', fetchSpy);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllEnvs();
    vi.unstubAllGlobals();
    getUser.mockReset();
  });

  it('sends a signed-in browser’s errors to the installation, as its account, within the route’s limits', async () => {
    getUser.mockResolvedValue({ access_token: 'token-1' });
    const { installErrorReporting, reportError } = await builtReporter();
    installErrorReporting();

    reportError({ kind: 'uncaught', name: 'TypeError', message: 'm'.repeat(5_000), stack: 'at x' });
    await vi.advanceTimersByTimeAsync(600);

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const [url, init] = fetchSpy.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/v1/diagnostics/client-errors');
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer token-1');
    const body = JSON.parse(String(init.body)) as { errors: { message: string; kind: string }[] };
    expect(body.errors).toHaveLength(1);
    expect(body.errors[0].kind).toBe('uncaught');
    // Cut to what the route takes rather than having the whole batch refused.
    expect(body.errors[0].message).toHaveLength(2_000);
  });

  it('reports nothing for a browser nobody is signed in on', async () => {
    getUser.mockResolvedValue(null);
    const { installErrorReporting, reportError } = await builtReporter();
    installErrorReporting();

    reportError({ kind: 'uncaught', message: 'boom', stack: 'at y' });
    await vi.advanceTimersByTimeAsync(600);

    expect(fetchSpy).not.toHaveBeenCalled();
  });
});
