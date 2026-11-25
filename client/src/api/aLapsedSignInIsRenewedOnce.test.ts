// SPDX-License-Identifier: AGPL-3.0-or-later
import type { User } from 'oidc-client-ts';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// Every request leaves through this one stand-in for the network. It is put in place before the
// client module loads, because the generated client keeps the `fetch` it found when it was created.
const network = vi.hoisted(() => {
  const state = { answer: (_request: Request): Promise<Response> => Promise.reject(new Error('no answer set')) };
  globalThis.fetch = ((request: Request) => state.answer(request)) as typeof fetch;
  return state;
});

vi.mock('../auth/renewSignIn.ts', () => ({
  renewSignIn: vi.fn(),
  signInAgain: vi.fn(() => Promise.resolve()),
  onSignInRenewed: vi.fn(() => () => {}),
}));

import { userManager } from '../auth/auth.tsx';
import { renewSignIn, signInAgain } from '../auth/renewSignIn.ts';
import { useSignInLapsed } from '../auth/signInLapsed.ts';
import { api, readJson } from './client.ts';

/**
 * <b>A sign-in that ran out while the tab was open is renewed by the transport, not by a reload.</b>
 * The token lives minutes and is renewed once before it runs out; a renewal that found no
 * connection is never tried again. So a tab that slept, or lost its signal, across that moment
 * holds a token the server refuses — and every read and save after it would be refused as coming
 * from nobody until the person reloads, losing whatever form they had open. These pin the one
 * place every request passes through: a refused bearer is renewed once and the request asked
 * again; a sign-in that cannot be renewed is announced, and signing in again is left to the person.
 */
describe('a request refused because the sign-in ran out', () => {
  let token: string | null;
  const sent: Request[] = [];
  let answers: Response[];

  beforeEach(() => {
    token = 'expired-token';
    sent.length = 0;
    answers = [];
    vi.spyOn(userManager, 'getUser').mockImplementation(
      async () => (token ? ({ access_token: token } as User) : null),
    );
    network.answer = async (request: Request) => {
      sent.push(request);
      return answers.shift() ?? new Response(null, { status: 500 });
    };
    window.history.pushState({}, '', '/caves/7?tab=documents#latest');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.mocked(renewSignIn).mockReset();
    vi.mocked(signInAgain).mockClear();
    useSignInLapsed.setState({ lapsed: false });
    window.history.pushState({}, '', '/');
  });

  const refused = () =>
    new Response(JSON.stringify({ status: 401 }), {
      status: 401,
      headers: { 'Content-Type': 'application/problem+json' },
    });
  const answered = (body: unknown) =>
    new Response(JSON.stringify(body), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    });

  it('renews the sign-in once and asks again with the new bearer', async () => {
    vi.mocked(renewSignIn).mockImplementation(async () => {
      token = 'renewed-token';
      return 'renewed';
    });
    answers = [refused(), answered({ displayName: 'x' })];

    const { data, response } = await api.GET('/api/v1/me', { baseUrl: window.location.origin });

    expect(response.status).toBe(200);
    expect(data).toEqual({ displayName: 'x' });
    expect(renewSignIn).toHaveBeenCalledTimes(1);
    expect(sent).toHaveLength(2);
    expect(sent[0].headers.get('Authorization')).toBe('Bearer expired-token');
    expect(sent[1].headers.get('Authorization')).toBe('Bearer renewed-token');
    expect(signInAgain).not.toHaveBeenCalled();
  });

  it('asks a save again with the body it was first sent with', async () => {
    vi.mocked(renewSignIn).mockImplementation(async () => {
      token = 'renewed-token';
      return 'renewed';
    });
    answers = [refused(), answered({})];

    const { response } = await api.PUT('/api/v1/me', {
      baseUrl: window.location.origin,
      body: { displayName: 'Ion A.' } as never,
      headers: { 'If-Match': '"v3"' },
    });

    expect(response.status).toBe(200);
    expect(sent).toHaveLength(2);
    expect(sent[1].method).toBe('PUT');
    expect(sent[1].headers.get('If-Match')).toBe('"v3"');
    expect(await sent[1].text()).toBe(JSON.stringify({ displayName: 'Ion A.' }));
  });

  it('asks again only once, when the renewed bearer is refused too', async () => {
    vi.mocked(renewSignIn).mockImplementation(async () => {
      token = 'renewed-token';
      return 'renewed';
    });
    answers = [refused(), refused(), answered({})];

    const { response } = await api.GET('/api/v1/me', { baseUrl: window.location.origin });

    expect(response.status).toBe(401);
    expect(sent).toHaveLength(2);
    expect(renewSignIn).toHaveBeenCalledTimes(1);
  });

  it('says the sign-in has ended, and leaves the page where it is, when it cannot be renewed', async () => {
    // Signing in again is a full redirect. A refused refetch or save must not start it by itself:
    // the form on screen may hold the only copy of what was typed. The person starts it, from the
    // notice the shell shows for this.
    vi.mocked(renewSignIn).mockResolvedValue('lapsed');
    answers = [refused()];

    const { response } = await api.GET('/api/v1/me', { baseUrl: window.location.origin });

    expect(response.status).toBe(401);
    expect(sent).toHaveLength(1);
    expect(signInAgain).not.toHaveBeenCalled();
    expect(useSignInLapsed.getState().lapsed).toBe(true);
  });

  it('hands a refused save back to its caller without leaving the page', async () => {
    vi.mocked(renewSignIn).mockResolvedValue('lapsed');
    answers = [refused()];

    const { response } = await api.PUT('/api/v1/me', {
      baseUrl: window.location.origin,
      body: { displayName: 'Ion A.' } as never,
    });

    expect(response.status).toBe(401);
    expect(signInAgain).not.toHaveBeenCalled();
    expect(useSignInLapsed.getState().lapsed).toBe(true);
  });

  it('leaves the refusal to the caller when nobody answered the renewal', async () => {
    vi.mocked(renewSignIn).mockResolvedValue('noAnswer');
    answers = [refused()];

    const { response } = await api.GET('/api/v1/me', { baseUrl: window.location.origin });

    expect(response.status).toBe(401);
    expect(sent).toHaveLength(1);
    expect(signInAgain).not.toHaveBeenCalled();
    expect(useSignInLapsed.getState().lapsed).toBe(false);
  });

  it('leaves a refusal of a request that carried no sign-in to the page that made it', async () => {
    // A visitor without an account opening a link that needs one is not a sign-in that ran out:
    // the page says so and offers signing in; the transport must not redirect them by itself.
    token = null;
    answers = [refused()];

    const { response } = await api.GET('/api/v1/me', { baseUrl: window.location.origin });

    expect(response.status).toBe(401);
    expect(sent).toHaveLength(1);
    expect(sent[0].headers.has('Authorization')).toBe(false);
    expect(renewSignIn).not.toHaveBeenCalled();
    expect(signInAgain).not.toHaveBeenCalled();
  });

  it('renews for a read made outside the typed client too', async () => {
    vi.mocked(renewSignIn).mockImplementation(async () => {
      token = 'renewed-token';
      return 'renewed';
    });
    answers = [refused(), answered({ items: [] })];

    await expect(readJson('/api/v1/not-yet-in-the-contract')).resolves.toEqual({ items: [] });
    expect(sent).toHaveLength(2);
    expect(sent[1].headers.get('Authorization')).toBe('Bearer renewed-token');
  });
});
