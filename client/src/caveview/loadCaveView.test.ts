// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { acquireCrsRewrite } from './loadCaveView.ts';

vi.mock('../auth/auth.tsx', () => ({
  userManager: { getUser: async () => ({ access_token: 'token-123' }) },
}));

let underlying: ReturnType<typeof vi.fn>;

beforeEach(() => {
  underlying = vi.fn(async () => new Response('+proj=sterea +lat_0=45', { status: 200 }));
  vi.stubGlobal('fetch', underlying);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

/** The URL the wrapper actually forwarded, whatever shape the caller used. */
function forwardedUrl(call: number): string {
  const input = underlying.mock.calls[call][0] as RequestInfo | URL;
  return input instanceof Request ? input.url : String(input);
}

describe('CaveView CRS lookup rewrite', () => {
  it('sends an epsg.io PROJ.4 lookup to this installation, with the bearer token', async () => {
    const release = acquireCrsRewrite();

    // Romanian Stereo70 — the case the vendored bundle does not hard-code, and the reason an
    // air-gapped install loses georeferencing without this.
    const response = await fetch('https://epsg.io/31700.proj4');

    expect(forwardedUrl(0)).toBe('/api/v1/crs/31700.proj4');
    expect(underlying.mock.calls[0][1]).toEqual({ headers: { Authorization: 'Bearer token-123' } });
    expect(await response.text()).toContain('+proj=');
    release();
  });

  it('leaves every other request byte-identical', async () => {
    const release = acquireCrsRewrite();

    await fetch('https://files.local/survey.3d');
    await fetch('/caveview/js/workers/gltfWorker.js', { method: 'GET' });
    // Same host, different resource: not a PROJ.4 lookup, so not ours to rewrite.
    await fetch('https://epsg.io/31700.wkt');
    await fetch('https://epsg.io/about');

    expect(forwardedUrl(0)).toBe('https://files.local/survey.3d');
    expect(forwardedUrl(1)).toBe('/caveview/js/workers/gltfWorker.js');
    expect(underlying.mock.calls[1][1]).toEqual({ method: 'GET' });
    expect(forwardedUrl(2)).toBe('https://epsg.io/31700.wkt');
    expect(forwardedUrl(3)).toBe('https://epsg.io/about');
    release();
  });

  it('restores the original fetch only once the last holder releases', async () => {
    const first = acquireCrsRewrite();
    const wrapper = globalThis.fetch;
    expect(wrapper).not.toBe(underlying);

    const second = acquireCrsRewrite();
    first();
    expect(globalThis.fetch).toBe(wrapper);

    second();
    expect(globalThis.fetch).toBe(underlying);

    // A double release must not tear down a rewrite a later panel installed.
    const third = acquireCrsRewrite();
    second();
    expect(globalThis.fetch).not.toBe(underlying);
    third();
    expect(globalThis.fetch).toBe(underlying);
  });

  it('answers with a response rather than rejecting when the lookup cannot be made', async () => {
    // The bundle crashes the whole parse on a rejected lookup but degrades cleanly on a non-ok
    // response, so a failure here must never escape as a rejection.
    underlying.mockRejectedValueOnce(new Error('offline'));
    const release = acquireCrsRewrite();

    const response = await fetch('https://epsg.io/31700.proj4');

    expect(response.ok).toBe(false);
    expect(response.status).toBe(503);
    release();
  });

  it('accepts a Request object, not just a string URL', async () => {
    const release = acquireCrsRewrite();

    await fetch(new Request('https://epsg.io/27700.proj4'));

    expect(forwardedUrl(0)).toBe('/api/v1/crs/27700.proj4');
    release();
  });
});
