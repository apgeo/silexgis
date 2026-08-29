// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The code printed on a label at a feature, read out of whatever the record actually holds.
 *
 * The codes arrive from the caving app and live in the typed-property document rather than in a
 * column, so this is a read of untyped data: anything that is not a non-empty string is no code
 * at all. The derived form is preferred over the raw place code because that is the one a label
 * carries when both exist.
 */
export function printedCode(properties: unknown): string | null {
  if (typeof properties !== 'object' || properties === null) {
    return null;
  }
  const bag = properties as Record<string, unknown>;
  for (const key of ['speleolocQcri', 'speleolocPci']) {
    const value = bag[key];
    if (typeof value === 'string' && value.trim().length > 0) {
      return value.trim();
    }
  }
  return null;
}

/**
 * Whether a code survives being written into the address a label carries.
 *
 * The scanner on a phone takes the text after the last `/` or `=` of the address **as written**,
 * and nothing unescapes it — so a code that has to be escaped to sit in a path reaches the lookup
 * as the escaped characters and resolves to nothing, and one containing `/` or `=` is silently
 * truncated. A browser opens such an address perfectly well, which is exactly why this has to be
 * said on screen rather than left to be discovered on a label already bolted to a wall.
 */
export function isPrintableCode(code: string): boolean {
  return encodeURIComponent(code) === code && !code.includes('=');
}
