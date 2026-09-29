// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * What the sheet importer does with a file before the server sees a character of it.
 *
 * The server reads text, not bytes, so the two questions a spreadsheet's export leaves open —
 * which character encoding it was saved under and which character separates its columns — are
 * answered here, in the browser, where the file is. Both are guessed from the bytes and both can
 * be overruled by the reviewer, because a guess that cannot be corrected is a guess presented as
 * a fact.
 */

/** The encodings a tracking sheet is read under, and "work it out from the bytes". */
export const SHEET_ENCODINGS = ['utf-8', 'windows-1250', 'iso-8859-2'] as const;
export type SheetEncoding = (typeof SHEET_ENCODINGS)[number];
export type SheetEncodingChoice = SheetEncoding | 'auto';

/** The column separators a spreadsheet writes, and "work it out from the header line". */
export const SHEET_DELIMITERS = [',', ';', '\t'] as const;
export type SheetDelimiter = (typeof SHEET_DELIMITERS)[number];
export type SheetDelimiterChoice = SheetDelimiter | 'auto';

/**
 * The file's bytes as text, and which encoding that took.
 *
 * <b>Strict UTF-8 first, then the Central European code page.</b> A file that is valid UTF-8 is
 * almost never anything else by accident — a Windows-1250 sheet with one diacritic in it fails the
 * strict decoder on that byte — so the fall-through is what a plain "CSV" save from a Romanian
 * Excel produces. Read as UTF-8 regardless, that file arrives with a replacement character in
 * every "ș" and "ă": "ieșire" stops being a state word and the row imports as a place at 0 m
 * instead of an exit, and diacritic names miss the roster. A byte-order mark is dropped by the
 * decoder itself.
 */
export function decodeSheet(
  bytes: ArrayBuffer,
  choice: SheetEncodingChoice,
): { text: string; encoding: SheetEncoding } {
  if (choice !== 'auto') {
    return { text: new TextDecoder(choice).decode(bytes), encoding: choice };
  }
  try {
    return { text: new TextDecoder('utf-8', { fatal: true }).decode(bytes), encoding: 'utf-8' };
  } catch {
    return { text: new TextDecoder('windows-1250').decode(bytes), encoding: 'windows-1250' };
  }
}

/**
 * The column separator the header line is written with.
 *
 * Decided on the header only, on purpose: a note column full of semicolons further down the
 * sheet says nothing about how the columns are divided, and the header is the one line that
 * carries no free text. Whichever of the three candidates the header holds most of wins; a header
 * holding none of them is one column wide whatever is chosen, and a comma keeps the reading the
 * server would have made on its own.
 */
export function sniffDelimiter(text: string): SheetDelimiter {
  const header = text.slice(0, firstLineBreak(text));
  let best: SheetDelimiter = ',';
  let bestCount = 0;
  for (const candidate of SHEET_DELIMITERS) {
    const count = header.split(candidate).length - 1;
    if (count > bestCount) {
      best = candidate;
      bestCount = count;
    }
  }
  return best;
}

function firstLineBreak(text: string): number {
  const cr = text.indexOf('\r');
  const lf = text.indexOf('\n');
  if (cr < 0) return lf < 0 ? text.length : lf;
  if (lf < 0) return cr;
  return Math.min(cr, lf);
}
