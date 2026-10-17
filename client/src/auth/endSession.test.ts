// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import { userManager } from './auth.tsx';
import { endSession } from './endSession.ts';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('endSession', () => {
  it('ends the sign-in cookie and leaves as a new document, without clearing the user in place', async () => {
    const steps: string[] = [];
    vi.spyOn(userManager, 'stopSilentRenew').mockImplementation(() => {
      steps.push('stop renewal');
    });
    // Clearing the user while the page is still open is what let the page's own guard start a
    // sign-in of its own, answered by the cookie, before the sign-out had gone anywhere.
    const removeUser = vi.spyOn(userManager, 'removeUser');
    const signoutRedirect = vi.spyOn(userManager, 'signoutRedirect');
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init: RequestInit) => {
        steps.push(`${init.method} ${url} (${init.credentials})`);
        return Promise.resolve(new Response(null, { status: 204 }));
      }),
    );
    vi.stubGlobal('location', { replace: (to: string) => steps.push(`replace ${to}`) });

    await endSession(userManager, '/');

    expect(steps).toEqual(['stop renewal', 'POST /api/v1/auth/logout (same-origin)', 'replace /']);
    expect(removeUser).not.toHaveBeenCalled();
    expect(signoutRedirect).not.toHaveBeenCalled();
  });

  it('still leaves when the server cannot be reached', async () => {
    vi.spyOn(userManager, 'stopSilentRenew').mockImplementation(() => undefined);
    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new TypeError('offline'))));
    const replace = vi.fn();
    vi.stubGlobal('location', { replace });

    await endSession(userManager, '/login?passwordChanged=1');

    expect(replace).toHaveBeenCalledWith('/login?passwordChanged=1');
  });
});
