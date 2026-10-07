// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Which file a delivery address delivers, ignoring the signature that expires.
 *
 * A survey is handed to the browser as a signed address: the stored file's own path, and a token
 * good for a few minutes. Whatever lists the survey is re-read before that token lapses and every
 * read signs the address afresh, so the whole string changes every few minutes while the path —
 * the file being handed over — stays exactly what it was. A stored file is never rewritten: a
 * survey that is replaced or converted again is another file at another path.
 *
 * So the path is the identity of what a viewer has drawn, in the only sense a viewer needs one:
 * it draws those bytes, and two addresses naming the same file name the same model.
 */
export function modelDeliveryIdentity(url: string): string {
  const query = url.indexOf('?');
  return query === -1 ? url : url.slice(0, query);
}
