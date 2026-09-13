// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { CAVEVIEW_HOME, focusNamedNothing, makeCrsLookup } from './loadCaveView.ts';
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

describe('telling a focus that found nothing from one that was abandoned', () => {
  it('recognises the model not holding what was asked for', () => {
    // The two refusals the viewer makes before it moves anything, and the only two states a
    // reader who followed a link has to be told about.
    expect(focusNamedNothing(new Error('No station [p.g.7] in the loaded survey'))).toBe(true);
    expect(focusNamedNothing(new Error('No survey section [p.g] in the loaded survey'))).toBe(true);
    expect(focusNamedNothing(new Error('No survey loaded'))).toBe(true);
  });

  it('says nothing of a move that was merely abandoned', () => {
    // Following two links in quick succession is what supersedes one, and selecting something in
    // the viewer while the camera flies is what cancels one. Both leave the camera where whoever
    // was driving it wanted it.
    expect(focusNamedNothing(new Error('superseded'))).toBe(false);
    expect(focusNamedNothing(new Error('cancelled'))).toBe(false);
  });

  /**
   * Which way the triage fails, and the reason it is written around the failures rather than the
   * abandonments. Every one of these messages is the vendored viewer's own wording, so a later
   * build rewording, prefixing or wrapping one is an ordinary event — and the cost of not
   * recognising an abandonment is a notice nobody sees, while the cost of not recognising a
   * failure would be telling a reader that a link which worked perfectly points at nothing.
   */
  it('falls silent, not into a false alarm, on anything it does not recognise', () => {
    expect(focusNamedNothing(new Error('move superseded by a later focus'))).toBe(false);
    expect(focusNamedNothing(new Error(''))).toBe(false);
    expect(focusNamedNothing('No station [p.g.7] in the loaded survey')).toBe(false);
    expect(focusNamedNothing(undefined)).toBe(false);
  });
});

/**
 * The one check here that is not circular.
 *
 * Everything above feeds the triage the strings the triage looks for, so it would pass whatever
 * the viewer actually says. The messages are the vendored bundle's own, composed inside it, and
 * nothing else in this suite ever touches that file — so an upgrade that reworded one would leave
 * the whole suite green while a link that found nothing had gone quiet. This reads the shipped
 * bundle instead, by way of the same versioned path the browser loads it from, so it follows an
 * upgrade to the next directory and fails on the upgrade that changes the words.
 */
describe('the words the triage is written against', () => {
  it('are the ones in the vendored bundle', () => {
    // Read through the bundler rather than the filesystem, as the other test that reads source
    // text does: the browser type project has no `node:fs`.
    const vendored = import.meta.glob('../../public/caveview/*/js/CaveView2.min.js', {
      query: '?raw',
      import: 'default',
      eager: true,
    }) as Record<string, string>;
    const [path, bundle] = Object.entries(vendored).find(([key]) => key.includes(CAVEVIEW_HOME)) ?? [];

    // The version the application loads is the version this was read from — a leftover directory
    // from a previous vendored build would otherwise be free to answer for the one in use.
    expect(path).toBeDefined();
    expect(bundle).toContain('No station [');
    expect(bundle).toContain('No survey section [');
    expect(bundle).toContain('in the loaded survey');
    expect(bundle).toContain('No survey loaded');
  });
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
