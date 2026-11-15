// SPDX-License-Identifier: AGPL-3.0-or-later
import { normalizeFlybyDocument, type FlybyDocument } from './flybyDocument.ts';

// The flybys a person has kept, and the file one travels in.
//
// Kept by name: saving under a name already used replaces that flyby, which is what "save" means
// to somebody refining one. The list is bounded, because it lives in the browser's own storage,
// which is small and shared with everything else this application remembers there.

export const FLYBY_LIBRARY_LIMIT = 50;
export const FLYBY_FILE_EXTENSION = 'flyby.json';
/** A file larger than this is not a flyby; reading it would only be reading somebody's mistake. */
export const FLYBY_FILE_MAX_BYTES = 256 * 1024;

/** The flybys kept, newest first, with `document` added or put in place of the one of its name. */
export function withFlybySaved(library: readonly FlybyDocument[], document: FlybyDocument): FlybyDocument[] {
  const others = library.filter((kept) => kept.name !== document.name);
  return [document, ...others].slice(0, FLYBY_LIBRARY_LIMIT);
}

export function withoutFlyby(library: readonly FlybyDocument[], name: string): FlybyDocument[] {
  return library.filter((kept) => kept.name !== name);
}

/** What was stored, read as a list of flybys; anything in it that is not one is left out. */
export function normalizeFlybyLibrary(stored: unknown): FlybyDocument[] {
  if (!Array.isArray(stored)) {
    return [];
  }
  const seen = new Set<string>();
  const library: FlybyDocument[] = [];
  for (const entry of stored) {
    const document = normalizeFlybyDocument(entry);
    if (document && document.name.length > 0 && !seen.has(document.name)) {
      seen.add(document.name);
      library.push(document);
    }
  }
  return library.slice(0, FLYBY_LIBRARY_LIMIT);
}

/** A flyby as the text of its file. */
export function flybyFileText(document: FlybyDocument): string {
  return `${JSON.stringify(document, null, 2)}\n`;
}

/** A flyby read from the text of a file, or null when the text is not one. */
export function parseFlybyFile(text: string): FlybyDocument | null {
  if (text.length > FLYBY_FILE_MAX_BYTES) {
    return null;
  }
  try {
    return normalizeFlybyDocument(JSON.parse(text));
  } catch {
    return null;
  }
}

/** What a flyby's file is called: its name made safe for a file system, or `flyby` when nothing is left. */
export function flybyFileName(document: Pick<FlybyDocument, 'name'>): string {
  const slug = document.name
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 60)
    .replace(/-+$/g, '');
  return `${slug.length > 0 ? slug : 'flyby'}.${FLYBY_FILE_EXTENSION}`;
}
