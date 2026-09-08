// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Which rendering of a photograph is wanted. Named for what it is for rather than for either
 * product's vocabulary, exactly as the server names it: the two libraries name their renderings
 * differently and neither name would mean anything at the call site.
 */
export type LibraryPictureSize = 'small' | 'large';

/**
 * The address one photograph's rendering is fetched from.
 *
 * One template per library rather than a finished address per photograph: the address carries a
 * short-lived credential, and repeating that across every point in a viewport — or every tile in a
 * grid — would add megabytes to a response for a picture nobody has clicked on yet.
 *
 * A null template means this library's pictures are stopped. It answered a picture request with
 * something that was not a picture, which can mean it has lost the disk its originals live on. No
 * template, no request — and against one of the two products that is not merely tidiness, because
 * asking such an instance for a picture is what marks the file missing over there.
 *
 * Written once and used by every surface that shows a foreign photograph, because it is one rule
 * rather than one line: two copies would part company the first time either moved, and the copy
 * that had not moved would go on minting addresses for a path that no longer exists.
 */
export function libraryPictureUrl(
  template: string | null | undefined,
  reference: string | undefined,
  size: LibraryPictureSize,
): string | undefined {
  if (!template || !reference) {
    return undefined;
  }

  // Escaped even though the server refuses a reference it would not put in a path itself. This is
  // a string from a library this installation does not own, and the browser is where it becomes a
  // URL; two guards on one value is the right number when one of them is somebody else's.
  //
  // Replaced through a function rather than with a string, because `$&` and its siblings are
  // substitution syntax in a replacement — a reference is foreign text and must not be able to
  // reach into the template around it.
  const encoded = encodeURIComponent(reference);
  return template.replace('{reference}', () => encoded).replace('{size}', () => size);
}

/**
 * The photographs whose picture did not arrive, per library, for as long as the page is open.
 *
 * <p>
 * A failed picture falls back to whatever stands in for it and is <b>never asked for again</b>.
 * That is not a performance choice. Against one of the two products, a picture request whose
 * original cannot be resolved is itself what marks the file missing and drops the photograph from
 * that library's own index — so a component that retries on every render, or a map style asked for
 * again on every frame, is a deletion loop rather than a slow page.
 * </p>
 * <p>
 * Held outside React and outside any one component on purpose: a ledger that lived in component
 * state would be forgotten on every unmount, and the first re-render after a failure would ask
 * again. The way back is a page reload, which is a person deciding, and on the server there is a
 * separate deliberate act that reopens the byte path for everybody.
 * </p>
 */
const failedPictures = new Map<string, Set<string>>();

export function markLibraryPictureFailed(source: string, reference: string): void {
  let failed = failedPictures.get(source);
  if (!failed) {
    failed = new Set<string>();
    failedPictures.set(source, failed);
  }
  failed.add(reference);
}

export function libraryPictureFailed(source: string, reference: string): boolean {
  return failedPictures.get(source)?.has(reference) ?? false;
}
