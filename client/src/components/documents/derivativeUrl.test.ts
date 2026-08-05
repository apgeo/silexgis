// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { displayableImageUrl, pageRenderUrl, thumbnailAtSize } from './derivativeUrl.ts';

describe('thumbnailAtSize', () => {
  it('keeps the token and every other parameter, changing only the width', () => {
    const url = thumbnailAtSize('/api/v1/files/abc/thumbnail?size=480&token=sig.ned', 1200);
    const query = new URLSearchParams(url.split('?')[1]);
    expect(url.startsWith('/api/v1/files/abc/thumbnail?')).toBe(true);
    expect(query.get('size')).toBe('1200');
    expect(query.get('token')).toBe('sig.ned');
  });
});

describe('pageRenderUrl', () => {
  it('asks for a picture of the page rather than for the document', () => {
    const url = pageRenderUrl('/api/v1/files/abc/content?token=sig.ned', 7, 1200);
    const query = new URLSearchParams(url.split('?')[1]);
    expect(url.split('?')[0]).toBe('/api/v1/files/abc/pages/7/render');
    // The stored file is not what a viewer fetches, at any size and under any spelling.
    expect(url).not.toContain('/content');
    expect(query.get('size')).toBe('1200');
    expect(query.get('token')).toBe('sig.ned');
  });
});

describe('displayableImageUrl', () => {
  it('shows the stored bytes to a caller entitled to them, and a rendering to one who is not', () => {
    const entitled = {
      contentUrl: '/api/v1/files/abc/content?token=full',
      thumbnailUrl: '/api/v1/files/abc/thumbnail?size=480&token=full',
      mayDownloadOriginal: true,
    };
    // The same file, one right fewer: the photo records where it was taken and this caller
    // may not place what it shows, so the original must not be reachable from the screen
    // that displays it either.
    const withheld = { ...entitled, mayDownloadOriginal: false };

    expect(displayableImageUrl(entitled)).toBe(entitled.contentUrl);

    const shown = displayableImageUrl(withheld);
    expect(shown).not.toBeNull();
    expect(shown).not.toContain('/content');
    expect(shown).toContain('size=1200');
  });

  it('shows nothing rather than a broken image when there is no rendering to show', () => {
    expect(
      displayableImageUrl({
        contentUrl: '/api/v1/files/abc/content?token=derivatives',
        thumbnailUrl: null,
        mayDownloadOriginal: false,
      }),
    ).toBeNull();
  });
});
