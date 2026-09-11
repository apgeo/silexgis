// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { makeCrsLookup } from './loadCaveView.ts';
import { userManager } from '../auth/auth.tsx';

vi.mock('../auth/auth.tsx', () => ({
  userManager: { getUser: vi.fn(async () => ({ access_token: 'token-123' })) },
}));

let underlying: ReturnType<typeof vi.fn>;

beforeEach(() => {
  underlying = vi.fn(async () => new Response('+proj=sterea +lat_0=45', { status: 200 }));
  vi.stubGlobal('fetch', underlying);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

describe('CaveView CRS lookup', () => {
  it('resolves a code against this installation, with the bearer token', async () => {
    const lookup = makeCrsLookup();

    // Romanian Stereo70 — the case the bundle does not hard-code, and the reason an
    // air-gapped install loses georeferencing without a local registry.
    const definition = await lookup('31700');

    expect(underlying.mock.calls[0][0]).toBe('/api/v1/crs/31700.proj4');
    expect(underlying.mock.calls[0][1]).toEqual({ headers: { Authorization: 'Bearer token-123' } });
    expect(definition).toContain('+proj=');
  });

  it('omits the auth header when nobody is signed in', async () => {
    (userManager.getUser as Mock).mockResolvedValueOnce(null);
    const lookup = makeCrsLookup();

    await lookup('31700');

    expect(underlying.mock.calls[0][0]).toBe('/api/v1/crs/31700.proj4');
    expect(underlying.mock.calls[0][1]).toEqual({ headers: undefined });
  });

  it('answers null rather than rejecting for a code the registry does not know', async () => {
    underlying.mockResolvedValueOnce(new Response(null, { status: 404 }));
    const lookup = makeCrsLookup();

    await expect(lookup('99999')).resolves.toBeNull();
  });

  it('answers null rather than rejecting when the request cannot be made', async () => {
    // The viewer treats null as "no definition" and falls back to its defaultCRS
    // handling — the survey loads unreferenced. A rejection escaping from the lookup
    // would fail the whole parse instead.
    underlying.mockRejectedValueOnce(new Error('offline'));
    const lookup = makeCrsLookup();

    await expect(lookup('31700')).resolves.toBeNull();
  });
});
