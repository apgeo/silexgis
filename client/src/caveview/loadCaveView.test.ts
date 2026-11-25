// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { CAVEVIEW_HOME, caveViewerOptions, focusNamedNothing, makeCrsLookup } from './loadCaveView.ts';
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

/** The request the lookup sent, the way the application's own requests are sent: as a whole request. */
const asked = (index: number) => underlying.mock.calls[index][0] as unknown as Request;
const askedPath = (index: number) => new URL(asked(index).url).pathname;

describe('what a viewer is built with', () => {
  it('is the versioned home, the language it was given and a lookup against this installation', async () => {
    const options = caveViewerOptions('ro');

    expect(options.home).toBe(CAVEVIEW_HOME);
    expect(options.language).toBe('ro');
    await options.crsLookup('31700');
    expect(askedPath(0)).toBe('/api/v1/crs/31700.proj4');
  });

  it('names the language by its two letters, which is all the viewer keys a catalogue on', () => {
    // The viewer asks for `lib/lang-<code>.json` with whatever it is given, so a regional form
    // would be a request for a file that does not exist rather than for the language's catalogue.
    expect(caveViewerOptions('en-GB').language).toBe('en');
    expect(caveViewerOptions('ro-RO').language).toBe('ro');
  });

  it('takes the lookup of a caller that cannot use this installation’s own', () => {
    // A page read without an account cannot reach the registry and brings a lookup that can.
    const own = async () => null;

    expect(caveViewerOptions('en', own).crsLookup).toBe(own);
    // Absent is absent however it is spelled: an optional prop passed straight through.
    expect(typeof caveViewerOptions('en', undefined).crsLookup).toBe('function');
  });
});

describe('CaveView CRS lookup', () => {
  it('resolves a code against this installation, with the bearer token', async () => {
    const lookup = makeCrsLookup();

    // Romanian Stereo70 — the case the bundle does not hard-code, and the reason an
    // air-gapped install loses georeferencing without a local registry.
    const definition = await lookup('31700');

    expect(askedPath(0)).toBe('/api/v1/crs/31700.proj4');
    expect(asked(0).headers.get('Authorization')).toBe('Bearer token-123');
    expect(definition).toContain('+proj=');
  });

  it('omits the auth header when nobody is signed in', async () => {
    (userManager.getUser as Mock).mockResolvedValueOnce(null);
    const lookup = makeCrsLookup();

    await lookup('31700');

    expect(askedPath(0)).toBe('/api/v1/crs/31700.proj4');
    expect(asked(0).headers.has('Authorization')).toBe(false);
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

/**
 * Every unit test of a host drives a hand-written fake of the viewer, so nothing there would notice
 * a vendored upgrade that renamed a method or an event the application relies on: the type check
 * reads the hand-written interface, not the bundle. This reads the names out of that interface and
 * looks each one up in the shipped bundle, so the interface cannot promise what the bundle no
 * longer has. A minified bundle keeps public property and event names as written, which is what
 * makes a text search meaningful.
 */
describe('the viewer surface the application calls', () => {
  const vendored = import.meta.glob('../../public/caveview/*/js/CaveView2.min.js', {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>;
  const bundle = Object.entries(vendored).find(([key]) => key.includes(CAVEVIEW_HOME))?.[1] ?? '';
  const source = Object.values(
    import.meta.glob('./loadCaveView.ts', { query: '?raw', import: 'default', eager: true }) as Record<string, string>,
  )[0];

  /** The member names of one exported interface, as its two-space-indented lines declare them. */
  function membersOf(name: string): string[] {
    const start = source.search(new RegExp(`^export interface ${name}\\b[^\\n]*\\{$`, 'm'));
    expect(start, `interface ${name}`).toBeGreaterThanOrEqual(0);
    const body = source.slice(start, source.indexOf('\n}', start));
    return [...body.matchAll(/^ {2}(?:readonly )?([A-Za-z_]\w*)\??\s*[(:<]/gm)].map((match) => match[1]);
  }

  /** The string members of one exported union type. */
  function unionOf(name: string): string[] {
    const start = source.indexOf(`export type ${name} =`);
    expect(start, `type ${name}`).toBeGreaterThanOrEqual(0);
    const body = source.slice(start, source.indexOf(';', start));
    return [...body.matchAll(/\|\s*'([^']+)'/g)].map((match) => match[1]);
  }

  it('is read from the version the application loads', () => {
    expect(bundle.length).toBeGreaterThan(0);
  });

  it.each(['CaveViewer', 'CaveViewUi', 'CaveViewToolbar', 'Cv2Namespace'])(
    'has every member of %s in the bundle',
    (name) => {
      const members = membersOf(name);
      // A parse that found nothing would pass the check below for any bundle at all.
      expect(members.length).toBeGreaterThan(0);
      expect(members.filter((member) => !bundle.includes(member))).toEqual([]);
    },
  );

  it('has every event the application listens for, dispatched under that name', () => {
    const events = unionOf('CaveViewerEvent');
    expect(events).toContain('newCave');
    expect(events.filter((event) => !new RegExp(`type:\\s*["']${event}["']`).test(bundle))).toEqual([]);
  });

  it('has every model layer the application switches', () => {
    const layers = unionOf('CaveViewModelLayer');
    expect(layers.length).toBeGreaterThan(0);
    expect(layers.filter((layer) => !bundle.includes(layer))).toEqual([]);
  });
});

/**
 * The bundle is loaded once per page by a script element; these drive that element by hand, since
 * the test document fetches nothing. Each test takes a fresh copy of the module, because the
 * promise of the load is held for the life of the page.
 */
describe('loading the viewer’s bundle', () => {
  const freshLoader = async () => {
    vi.resetModules();
    return (await import('./loadCaveView.ts')).loadCaveView;
  };
  const scripts = () => Array.from(document.head.querySelectorAll<HTMLScriptElement>('script'))
    .filter((script) => script.src.includes(`${CAVEVIEW_HOME}js/`));
  const links = () => Array.from(document.head.querySelectorAll('link'))
    .filter((link) => link.href.includes(`${CAVEVIEW_HOME}css/`));
  const fakeCv2 = {} as NonNullable<Window['CV2']>;

  beforeEach(() => {
    for (const element of [...scripts(), ...links()]) element.remove();
    window.CV2 = undefined;
  });

  afterEach(() => {
    window.CV2 = undefined;
  });

  it('answers the global the bundle defines, from one script and one stylesheet however often it is asked', async () => {
    const loadCaveView = await freshLoader();
    const first = loadCaveView();
    const second = loadCaveView();
    expect(scripts()).toHaveLength(1);
    expect(links()).toHaveLength(1);

    window.CV2 = fakeCv2;
    scripts()[0].onload?.(new Event('load'));
    await expect(first).resolves.toBe(fakeCv2);
    await expect(second).resolves.toBe(fakeCv2);
  });

  it('tries again after a script that failed to load, without a second stylesheet', async () => {
    const loadCaveView = await freshLoader();
    const failed = loadCaveView();
    scripts()[0].onerror?.(new Event('error'));
    await expect(failed).rejects.toThrow('failed to load the CaveView bundle');
    expect(scripts()).toHaveLength(0);

    const retried = loadCaveView();
    expect(scripts()).toHaveLength(1);
    expect(links()).toHaveLength(1);
    window.CV2 = fakeCv2;
    scripts()[0].onload?.(new Event('load'));
    await expect(retried).resolves.toBe(fakeCv2);
  });

  it('tries again after a script that loaded but defined no viewer, rather than failing for the life of the page', async () => {
    // What a deploy answering the bundle's path with an error page and a 200 looks like.
    const loadCaveView = await freshLoader();
    const failed = loadCaveView();
    scripts()[0].onload?.(new Event('load'));
    await expect(failed).rejects.toThrow('CV2 global is missing');
    expect(scripts()).toHaveLength(0);

    const retried = loadCaveView();
    expect(scripts()).toHaveLength(1);
    window.CV2 = fakeCv2;
    scripts()[0].onload?.(new Event('load'));
    await expect(retried).resolves.toBe(fakeCv2);
  });

  it('adds nothing to the page when the viewer is already there', async () => {
    window.CV2 = fakeCv2;
    const loadCaveView = await freshLoader();
    await expect(loadCaveView()).resolves.toBe(fakeCv2);
    expect(scripts()).toHaveLength(0);
    expect(links()).toHaveLength(0);
  });
});
