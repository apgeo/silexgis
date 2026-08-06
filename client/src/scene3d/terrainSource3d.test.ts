// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  checkTerrainSource,
  describeTerrainLayer,
  looksGzipped,
} from './terrainSource3d.ts';

// The four ways a terrain pyramid and the web server serving it can be paired, and what each one
// does. Two of them work and two of them draw a globe with no ground on it while every request
// answers 200 and nothing anywhere reports an error — which is why this check exists at all, and
// why its tests are written as that matrix rather than as "does it parse".

/** A `layer.json` the way a pre-baker writes one, trimmed to the fields this check reads. */
const LAYER_JSON = {
  tilejson: '2.1.0',
  format: 'quantized-mesh-1.0',
  attribution: 'insert attribution here',
  scheme: 'tms',
  tiles: ['{z}/{x}/{y}.terrain?v={version}'],
  extensions: ['octvertexnormals'],
  available: [
    [{ startX: 0, endX: 2, startY: 0, endY: 1 }],
    [{ startX: 1, endX: 3, startY: 0, endY: 2 }],
  ],
};

/** The first bytes of a real quantized mesh: a centre position, which is never `1f 8b`. */
const RAW_TILE = new Uint8Array([0x8d, 0x97, 0x6e, 0x3f, 0x00, 0x11, 0x22, 0x33]);
/** The same tile, compressed. Any gzip stream begins with these two bytes. */
const GZIPPED_TILE = new Uint8Array([0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00]);

/** A body that a browser could not decode, because the declared encoding was not the bytes. */
const DECODING_FAILED = 'decodingFailed';

interface StubOptions {
  /** What `layer.json` answers with. An object is served as its JSON text. */
  layer?: unknown;
  layerStatus?: number;
  tile?: Uint8Array | typeof DECODING_FAILED;
  tileStatus?: number;
}

function body(value: unknown): Uint8Array | typeof DECODING_FAILED {
  if (value === DECODING_FAILED) {
    return DECODING_FAILED;
  }
  if (value instanceof Uint8Array) {
    return value;
  }
  return new TextEncoder().encode(typeof value === 'string' ? value : JSON.stringify(value));
}

function respond(status: number, content: Uint8Array | typeof DECODING_FAILED): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    arrayBuffer: async () => {
      if (content === DECODING_FAILED) {
        // What a browser does when it is told the body is gzip and it is not: the response
        // itself is perfectly fine and only the transfer dies, while it is being decoded.
        throw new TypeError('Failed to fetch');
      }
      return content.buffer as ArrayBuffer;
    },
  } as Response;
}

/**
 * A web server, as a function.
 *
 * Written by hand rather than mocked so both failure shapes can be reproduced exactly as a browser
 * produces them: a server declaring an encoding the bytes do not have fails while the body is
 * read, while a server serving compressed bytes and saying nothing succeeds all the way through
 * and hands over a gzip stream where a mesh should be.
 */
function server(options: StubOptions): typeof fetch {
  return (async (input: RequestInfo | URL) => {
    const url = String(input);
    if (url.endsWith('layer.json')) {
      return respond(options.layerStatus ?? 200, body(options.layer ?? '<!doctype html><html>'));
    }
    if (url.includes('.terrain')) {
      return respond(options.tileStatus ?? 200, body(options.tile ?? new Uint8Array()));
    }
    throw new Error(`unexpected request: ${url}`);
  }) as typeof fetch;
}

describe('reading what a pyramid says about itself', () => {
  it('picks a tile that the pyramid says exists, rather than guessing one', () => {
    // Guessing would 404 on any pyramid whose coarsest level does not start at the origin, and a
    // 404 reads as "the pyramid is not there" — the wrong answer, loudly.
    expect(describeTerrainLayer(LAYER_JSON)?.probeTilePath).toBe('0/0/0.terrain');
    expect(
      describeTerrainLayer({ ...LAYER_JSON, available: [[{ startX: 3, startY: 5 }]] })
        ?.probeTilePath,
    ).toBe('0/3/5.terrain');
  });

  it('refuses anything that is not a quantized-mesh pyramid', () => {
    expect(describeTerrainLayer({ ...LAYER_JSON, format: 'heightmap-1.0' })).toBeUndefined();
    expect(describeTerrainLayer({ tilejson: '2.1.0' })).toBeUndefined();
    expect(describeTerrainLayer('<!doctype html>')).toBeUndefined();
    expect(describeTerrainLayer(null)).toBeUndefined();
  });

  it('falls back to the origin tile when a pyramid publishes no availability', () => {
    // Availability is optional in this format and a pyramid without it draws perfectly well — a
    // renderer that meets one simply asks for tiles and refines from whatever answers. Refusing
    // the source over its absence would take a working elevation model off the screen and tell the
    // operator their address holds no tile set.
    for (const available of [undefined, [], [[]], [[{ endX: 2 }]]]) {
      const description = describeTerrainLayer({ ...LAYER_JSON, available });
      expect(description?.probeTilePath).toBe('0/0/0.terrain');
      expect(description?.probeTilePublished).toBe(false);
    }
    expect(describeTerrainLayer(LAYER_JSON)?.probeTilePublished).toBe(true);
  });

  it('asks for the tile at the address the renderer will ask for it at', () => {
    // A terrain directory is cached hard and for a week, and the version the pyramid publishes is
    // what every tile URL a renderer builds ends with. Leaving it off addresses a file the
    // renderer never addresses — so a verdict formed before a re-bake comes back out of the
    // browser's own cache for days afterwards, condemning a pyramid that has since been repaired.
    expect(
      describeTerrainLayer({ ...LAYER_JSON, version: '1.1.0-9f2c04ab77e1' })?.probeTilePath,
    ).toBe('0/0/0.terrain?v=1.1.0-9f2c04ab77e1');
  });
});

describe('recognising a gzip stream by its first two bytes', () => {
  it('knows one from a mesh', () => {
    expect(looksGzipped(GZIPPED_TILE)).toBe(true);
    expect(looksGzipped(RAW_TILE)).toBe(false);
    expect(looksGzipped(new Uint8Array([0x1f]))).toBe(false);
    expect(looksGzipped(new Uint8Array())).toBe(false);
  });
});

describe('the four pairings of tile bytes and declared encoding', () => {
  it('accepts a tile that arrives as a mesh, whether or not it travelled compressed', async () => {
    // The two working pairings — raw bytes with nothing declared, and gzipped bytes declared as
    // gzip — are indistinguishable here, because the browser has already undone any encoding the
    // server declared before anything can look at the body. That is exactly why the rule is
    // checked as "what arrived is a mesh" rather than as anything about the header.
    expect(
      await checkTerrainSource('/terrain/', server({ layer: LAYER_JSON, tile: RAW_TILE })),
    ).toBeUndefined();
  });

  it('catches raw tiles served as gzip, where the transfer dies while being decoded', async () => {
    expect(
      await checkTerrainSource('/terrain/', server({ layer: LAYER_JSON, tile: DECODING_FAILED })),
    ).toBe('encodingMismatch');
  });

  it('names the encoding when a bad rule catches layer.json too, as a real one does', async () => {
    // A web server describes a directory, not a file, so a wrong Content-Encoding on the tiles is
    // a wrong Content-Encoding on the description beside them — which is where a browser meets it
    // first. Calling that "this is not a terrain tile set" would send an operator to check a
    // directory that is perfectly fine.
    expect(
      await checkTerrainSource('/terrain/', server({ layer: DECODING_FAILED })),
    ).toBe('encodingMismatch');
    expect(
      await checkTerrainSource('/terrain/', server({ layer: GZIPPED_TILE })),
    ).toBe('encodingMismatch');
  });

  it('catches gzipped tiles served with nothing declared — the failure with no symptom at all', async () => {
    // This is the one that matters. Every request succeeds, the status is 200, no error is raised
    // anywhere and the globe simply has no ground on it. The only thing that gives it away is that
    // what arrived is still compressed.
    expect(
      await checkTerrainSource('/terrain/', server({ layer: LAYER_JSON, tile: GZIPPED_TILE })),
    ).toBe('encodingMismatch');
  });
});

describe('a pyramid that is not there at all', () => {
  it('reports an unreachable source when the description is missing', async () => {
    expect(await checkTerrainSource('/terrain/', server({ layerStatus: 404 }))).toBe('unreachable');
  });

  it('reports an unreachable source when the tiles are missing under a good description', async () => {
    expect(
      await checkTerrainSource(
        '/terrain/',
        server({ layer: LAYER_JSON, tile: RAW_TILE, tileStatus: 404 }),
      ),
    ).toBe('unreachable');
  });

  it("reports a malformed source when the application's own page answers instead", async () => {
    // A web server told to fall back to a single-page application answers 200 with HTML for any
    // path that does not exist, so a terrain directory that was never mounted looks — to every
    // status code involved — exactly like one that is there.
    expect(await checkTerrainSource('/terrain/', server({}))).toBe('malformed');
  });

  it('reports a malformed source when a tile is empty', async () => {
    expect(
      await checkTerrainSource('/terrain/', server({ layer: LAYER_JSON, tile: new Uint8Array() })),
    ).toBe('malformed');
  });
});

describe('a pyramid that does not publish which tiles it holds', () => {
  const noAvailability = { ...LAYER_JSON, available: undefined };

  it('accepts it when the guessed tile is not there, because a guess proves nothing', async () => {
    // The origin tile is a guess when the pyramid says nothing about what it holds, and a guess
    // that misses is not evidence about the pyramid. A renderer asks for tiles it has no list for
    // in exactly the same way and refines from the ones that answer.
    expect(
      await checkTerrainSource(
        '/terrain/',
        server({ layer: noAvailability, tile: RAW_TILE, tileStatus: 404 }),
      ),
    ).toBeUndefined();
  });

  it('still catches the encoding mismatch when the guessed tile does answer', async () => {
    // A tile that IS there is as good a sample of how this directory is served as any other, and
    // the mismatch it would otherwise hide is the failure with no symptom at all.
    expect(
      await checkTerrainSource('/terrain/', server({ layer: noAvailability, tile: GZIPPED_TILE })),
    ).toBe('encodingMismatch');
  });

  it('keeps refusing a missing tile the pyramid said was there', async () => {
    expect(
      await checkTerrainSource(
        '/terrain/',
        server({ layer: LAYER_JSON, tile: RAW_TILE, tileStatus: 404 }),
      ),
    ).toBe('unreachable');
  });
});

describe('addressing the pyramid', () => {
  it('looks under the configured directory even when it was given without a trailing slash', async () => {
    const asked: string[] = [];
    const record: typeof fetch = (async (input: RequestInfo | URL) => {
      asked.push(String(input));
      return server({ layer: LAYER_JSON, tile: RAW_TILE })(input);
    }) as typeof fetch;

    await checkTerrainSource('/terrain', record);

    // Without this a pyramid at /terrain would be looked for at /layer.json, which the
    // single-page fallback answers with the application's own HTML under a 200.
    expect(asked).toEqual(['/terrain/layer.json', '/terrain/0/0/0.terrain']);
  });

  it('never answers out of the browser\'s own store', async () => {
    // Elevation tiles are meant to be cached for a week, which is right for drawing them and wrong
    // for judging them: a verdict formed before the operator repaired the source would be served
    // back from that cache for the rest of the week, telling the installation its now-correct
    // pyramid is broken — and, the other way about, a stale copy of a tile that used to be fine
    // would wave through one that no longer is.
    const modes: unknown[] = [];
    const record: typeof fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      modes.push(init?.cache);
      return server({ layer: LAYER_JSON, tile: RAW_TILE })(input);
    }) as typeof fetch;

    await checkTerrainSource('/terrain/', record);

    expect(modes).toEqual(['no-store', 'no-store']);
  });
});
