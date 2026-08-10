// SPDX-License-Identifier: AGPL-3.0-or-later
import { CURRENT_FILTER_VERSION, type FilterDocument } from './types.ts';

/**
 * A filter as a link.
 *
 * A filtered workspace someone can paste into a message is the whole point: "look at these" is a
 * sentence people say, and it is worth more than any amount of explaining which boxes to tick.
 */

/** The query parameter a filtered workspace carries. */
export const FILTER_PARAM = 'filter';

/**
 * The document as one URL-safe token.
 *
 * Base64url over the JSON rather than a readable query string. Readable would be nicer to debug
 * and worse at everything else: a filter has nested groups, so a flat query string needs an
 * encoding of its own, and every link already sent would break the day that encoding gained a
 * case. One opaque token has one version number and one place to read it.
 */
export function encodeFilter(document: FilterDocument): string {
  const json = JSON.stringify(document);
  const bytes = new TextEncoder().encode(json);
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '');
}

/**
 * The document a token holds, or null when it holds something this version cannot read.
 *
 * Null rather than a thrown error, and null for every reason: truncated by a chat client, written
 * by a newer version, or simply not a filter. A link somebody pasted badly must open the page it
 * points at with nothing filtered — never an error screen, because the person who opened it did
 * nothing wrong and has no way to fix the link.
 */
export function decodeFilter(token: string | null | undefined): FilterDocument | null {
  if (!token) {
    return null;
  }

  try {
    const padded = token.replaceAll('-', '+').replaceAll('_', '/');
    const binary = atob(padded + '='.repeat((4 - (padded.length % 4)) % 4));
    const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
    const parsed: unknown = JSON.parse(new TextDecoder().decode(bytes));

    return isReadable(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

/**
 * Whether a parsed object is a filter this version understands.
 *
 * Shape only, and deliberately shallow: whether the fields and operators inside are ones this
 * installation admits is the server's answer, given against the caller's own vocabulary. Checking
 * it here as well would be a second opinion that is wrong for anybody whose vocabulary differs.
 */
function isReadable(value: unknown): value is FilterDocument {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const document = value as Partial<FilterDocument>;
  return (
    typeof document.version === 'number'
    && document.version <= CURRENT_FILTER_VERSION
    && Array.isArray(document.scope)
    && document.scope.every((s) => typeof s?.world === 'string')
    && typeof document.sort === 'string'
    && typeof document.descending === 'boolean'
  );
}

/** The same parameters with the filter written in, or removed when there is nothing to say. */
export function withFilterParam(
  params: URLSearchParams,
  document: FilterDocument | null,
): URLSearchParams {
  const next = new URLSearchParams(params);
  if (document === null || document.scope.length === 0) {
    next.delete(FILTER_PARAM);
  } else {
    next.set(FILTER_PARAM, encodeFilter(document));
  }

  return next;
}
