// SPDX-License-Identifier: AGPL-3.0-or-later

// Checking that a terrain pyramid is being served in a form that can actually be drawn, before
// anything tries to draw it.
//
// This exists because of one measured failure that has no symptom. Terrain tiles are a binary
// format, and a web server that declares them compressed when they are not — or serves them
// compressed without saying so — produces a globe with no ground on it, while every tile answers
// 200, the engine reports no error, the network log shows no failure and the console stays empty.
// The engine fetches a handful of tiles, fails to parse them, stops asking for more and says
// nothing at all. An operator meeting that has no thread to pull.
//
// The rule that avoids it is simply that the declared encoding has to describe the bytes, and it
// is checkable from outside the engine in one small request: fetch one tile, and look. That is
// what this module does. It is deliberately engine-free — no 3D library is loaded to run it —
// so it can be exercised as arithmetic over a fake fetch, and so the answer is known before the
// megabyte of engine is asked to do anything with it.

/** What a pyramid's `layer.json` has to say for anything to be fetched from it. */
export interface TerrainLayerDescription {
  /** Path of the tile to look at, relative to the pyramid's root. */
  probeTilePath: string;
  /**
   * True when the pyramid itself said that tile is there.
   *
   * A pyramid may publish which tiles it holds and may equally leave that out — the format makes
   * it optional and a renderer that meets one simply asks for tiles and refines from whatever
   * answers. When it is missing the path above is the format's own origin tile, guessed, and a
   * guess that misses is evidence about nothing at all.
   */
  probeTilePublished: boolean;
  /** Credit the pyramid claims for itself, which may be the pre-baker's placeholder. */
  attribution?: string;
}

/** Why a configured terrain source cannot be drawn. */
export type TerrainSourceProblem =
  /** Nothing answered, or answered with an error status. The pyramid is not where it was said to be. */
  | 'unreachable'
  /** Something answered but it is not a tile pyramid — most often the application's own HTML. */
  | 'malformed'
  /**
   * The tiles and the `Content-Encoding` the server declares for them do not agree. Either the
   * server says gzip over bytes that are not, or the bytes are gzip and the server does not say so.
   * The globe would draw no ground and report nothing.
   */
  | 'encodingMismatch';

/** The two bytes every gzip stream starts with. */
const GZIP_MAGIC = [0x1f, 0x8b];

/**
 * Reads the parts of a `layer.json` this check needs: which tile to look at, and what the pyramid
 * says about its own attribution.
 *
 * The tile is taken from the availability the pyramid publishes when there is any, because a guess
 * that misses answers 404 and reading that as a broken pyramid would refuse a working one. The
 * coarsest level is used: it is the smallest tile, it is always present, and whether a tile parses
 * is a property of how it is served rather than of which one it is.
 *
 * Availability is optional in this format, and a pyramid that omits it is drawn perfectly well —
 * the renderer asks for tiles and refines from whatever answers, rather than consulting a list. So
 * its absence is not a reason to refuse the source; the origin tile is asked for instead, and the
 * caller is told that this address was guessed so that a 404 there is treated as "cannot say"
 * rather than as a failure.
 *
 * The version the pyramid publishes for itself is carried on the end, because that is what the
 * renderer will put on the end of every tile it asks for. Without it this check addresses
 * something the renderer never addresses — and a terrain directory is cached hard and for a long
 * time, so a verdict reached before a re-bake would be handed back out of the browser's own cache
 * for days afterwards, condemning a pyramid that has since been fixed.
 *
 * Exported for its own tests; the check below is what callers use.
 */
export function describeTerrainLayer(layer: unknown): TerrainLayerDescription | undefined {
  if (typeof layer !== 'object' || layer === null) {
    return undefined;
  }
  const bag = layer as Record<string, unknown>;
  // A pyramid of quantized meshes is the only thing this scene can draw. Anything else answering
  // at that URL — a raster tile set, a directory listing, the application's own index page — is a
  // misconfiguration rather than a format to fall back to.
  if (typeof bag.format !== 'string' || !bag.format.startsWith('quantized-mesh')) {
    return undefined;
  }

  const range = coarsestRange(bag.available);
  const version = typeof bag.version === 'string' && bag.version.length > 0 ? bag.version : '';
  // The layout under the root is fixed by the format, so the path is composed rather than read
  // out of the published URL template.
  const tile = `0/${range?.startX ?? 0}/${range?.startY ?? 0}.terrain`;

  return {
    probeTilePath: version ? `${tile}?v=${encodeURIComponent(version)}` : tile,
    probeTilePublished: range !== undefined,
    ...(typeof bag.attribution === 'string' ? { attribution: bag.attribution } : {}),
  };
}

/** The first tile range of the coarsest level a pyramid publishes, when it publishes any. */
function coarsestRange(available: unknown): { startX: number; startY: number } | undefined {
  if (!Array.isArray(available) || available.length === 0) {
    return undefined;
  }
  const coarsest = available[0];
  if (!Array.isArray(coarsest) || coarsest.length === 0) {
    return undefined;
  }
  const range = coarsest[0] as Record<string, unknown> | null;
  if (
    typeof range?.startX !== 'number' ||
    typeof range.startY !== 'number' ||
    !Number.isFinite(range.startX) ||
    !Number.isFinite(range.startY)
  ) {
    return undefined;
  }
  return { startX: range.startX, startY: range.startY };
}

/** True when these bytes are a gzip stream, whatever they claim to be. */
export function looksGzipped(bytes: Uint8Array): boolean {
  return bytes.length >= 2 && bytes[0] === GZIP_MAGIC[0] && bytes[1] === GZIP_MAGIC[1];
}

/**
 * Fetches one tile from a configured pyramid and reports what is wrong with it, or nothing when
 * it can be drawn.
 *
 * Both halves of the encoding mismatch are caught, and they present differently. When the server
 * declares an encoding the bytes do not have, the browser fails the transfer while decoding it and
 * the fetch rejects — there is no response to inspect. When the bytes are compressed and the
 * server says nothing, the transfer succeeds perfectly and what arrives is a gzip stream where a
 * mesh should be, which is visible in its first two bytes.
 *
 * `fetchImpl` is a parameter so this can be tested without a network; callers pass nothing.
 */
export async function checkTerrainSource(
  url: string,
  fetchImpl: typeof fetch = fetch,
): Promise<TerrainSourceProblem | undefined> {
  const root = url.endsWith('/') ? url : `${url}/`;

  const layerBytes = await readFile(`${root}layer.json`, fetchImpl);
  if (typeof layerBytes === 'string') {
    return layerBytes;
  }

  let layer: unknown;
  try {
    layer = JSON.parse(new TextDecoder().decode(layerBytes));
  } catch {
    // Something answered, with a success status, and it is not JSON. Overwhelmingly this is the
    // application's own index page: a web server told to fall back to a single-page application
    // answers 200 with HTML for a path that does not exist, so a pyramid that was never mounted
    // looks exactly like one that is there but broken.
    return 'malformed';
  }

  const description = describeTerrainLayer(layer);
  if (!description) {
    return 'malformed';
  }

  const tileBytes = await readFile(`${root}${description.probeTilePath}`, fetchImpl);
  if (typeof tileBytes !== 'string') {
    return undefined;
  }
  // A tile the pyramid never said was there, and which is not there, says nothing: the renderer
  // asks for tiles it has no list for in exactly the same way and refines from the ones that
  // answer. Refusing here would take a working pyramid off the screen over a guess. Every other
  // verdict still counts — a guessed tile that DOES answer is as good a sample of how this
  // directory is served as any other.
  return !description.probeTilePublished && tileBytes === 'unreachable' ? undefined : tileBytes;
}

/**
 * Fetches one file from the pyramid, returning its bytes or the reason it could not be used.
 *
 * Every file under the pyramid's root gets the same treatment because a web server describes a
 * whole directory at a time: a rule that mislabels the tiles mislabels `layer.json` alongside
 * them, and an operator told "that is not a terrain tile set" when the real answer is "your web
 * server is describing these files wrongly" has been sent to the wrong place entirely.
 *
 * Nothing here is answered out of the browser's store. Elevation tiles are meant to be cached for
 * a week at a time, which is right for drawing them and wrong for judging them: a verdict formed
 * before the operator repaired the source would otherwise be re-served from that cache for the
 * rest of the week, telling an installation its now-correct pyramid is broken — and, in the other
 * direction, a stale copy of a tile that used to be fine would wave through one that no longer is.
 */
async function readFile(
  url: string,
  fetchImpl: typeof fetch,
): Promise<Uint8Array | TerrainSourceProblem> {
  let response: Response;
  try {
    response = await fetchImpl(url, { cache: 'no-store' });
  } catch {
    return 'unreachable';
  }
  if (!response.ok) {
    return 'unreachable';
  }

  let bytes: Uint8Array;
  try {
    bytes = new Uint8Array(await response.arrayBuffer());
  } catch {
    // The request succeeded and the transfer then failed, which for a file that answered with a
    // success status means the browser could not undo the encoding the server declared: it says
    // these bytes are compressed and they are not. Reported as the mismatch it is rather than as
    // an unreachable pyramid, because the two send an operator to entirely different places.
    return 'encodingMismatch';
  }

  if (bytes.length === 0) {
    return 'malformed';
  }
  // The browser has already undone any encoding the server declared, so anything still compressed
  // at this point is compressed without the server having said so — the failure with no symptom.
  return looksGzipped(bytes) ? 'encodingMismatch' : bytes;
}
