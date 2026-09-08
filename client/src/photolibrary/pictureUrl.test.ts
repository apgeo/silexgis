// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  libraryPictureFailed,
  libraryPictureUrl,
  markLibraryPictureFailed,
} from './pictureUrl.ts';

/**
 * The address a foreign photograph's picture is fetched from, and the ledger of the ones that must
 * never be asked for again.
 *
 * Both are shared by every surface that shows a photograph from a neighbouring library — the map's
 * markers, its balloons, the browsing grid and the detail panel — and the second is the one with
 * teeth: against one of the two products, a picture request whose original cannot be resolved is
 * itself what marks the file missing over there.
 *
 * Every template, reference and library name below is invented.
 */

const template =
  '/api/v1/photo-libraries/photoprism/thumbnails/{reference}?size={size}&token=invented';

describe('the address one foreign picture is fetched from', () => {
  it('substitutes the reference and the size and nothing else', () => {
    expect(libraryPictureUrl(template, 'aa11bb22cc33', 'small')).toBe(
      '/api/v1/photo-libraries/photoprism/thumbnails/aa11bb22cc33?size=small&token=invented',
    );
    expect(libraryPictureUrl(template, 'aa11bb22cc33', 'large')).toContain('size=large');
  });

  /**
   * A reference is text from a library this installation does not own, and the browser is where it
   * becomes a URL. Escaped here even though the server refuses one it would not put in a path
   * itself: two guards on one value is the right number when one of them is somebody else's.
   */
  it('escapes a reference rather than pasting it into an address', () => {
    expect(libraryPictureUrl(template, 'a b&c', 'small')).toContain('thumbnails/a%20b%26c?');
  });

  /**
   * `$&` and its siblings are substitution syntax in a replacement string. A reference carrying
   * one must not be able to reach into the template around it and repeat the credential, the path
   * or anything else.
   */
  it('does not let a reference reach into the template around it', () => {
    const address = libraryPictureUrl(template, '$&', 'small');

    expect(address).toBe(
      '/api/v1/photo-libraries/photoprism/thumbnails/%24%26?size=small&token=invented',
    );
  });

  /**
   * A null template is this library's pictures being stopped, published by the server. No
   * template, no request — which is the whole of how that gate reaches the browser.
   */
  it('asks for nothing when the library has no template or the photograph has no reference', () => {
    expect(libraryPictureUrl(null, 'aa11bb22cc33', 'small')).toBeUndefined();
    expect(libraryPictureUrl(template, undefined, 'small')).toBeUndefined();
    expect(libraryPictureUrl(template, '', 'small')).toBeUndefined();
  });
});

describe('the pictures that are never asked for again', () => {
  it('remembers a failure per library and per photograph', () => {
    expect(libraryPictureFailed('photoprism', 'dd44ee55ff66')).toBe(false);

    markLibraryPictureFailed('photoprism', 'dd44ee55ff66');

    expect(libraryPictureFailed('photoprism', 'dd44ee55ff66')).toBe(true);

    // Kept apart per library: the two are separate installations with separate storage, and one
    // losing a file says nothing about the other.
    expect(libraryPictureFailed('immich', 'dd44ee55ff66')).toBe(false);

    // And apart per photograph: one derivative can be missing from a library that is otherwise
    // perfectly able to serve every other picture it holds.
    expect(libraryPictureFailed('photoprism', 'ee55ff66aa11')).toBe(false);
  });
});
