// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { photoPopupNodes } from './photoPopup.ts';

describe('photoPopupNodes', () => {
  it('renders a thumbnail linking to the full image, with the file name', () => {
    const [link, caption] = photoPopupNodes({
      id: 'f1',
      name: 'entrance.jpg',
      thumbnailUrl: '/api/v1/files/f1/thumbnail?size=160&token=abc',
      contentUrl: '/api/v1/files/f1/content?token=abc',
    }) as [HTMLAnchorElement, HTMLElement];

    expect(link.getAttribute('href')).toBe('/api/v1/files/f1/content?token=abc');
    expect(link.rel).toBe('noopener');
    const img = link.querySelector('img')!;
    expect(img.getAttribute('src')).toBe('/api/v1/files/f1/thumbnail?size=160&token=abc');
    expect(caption.textContent).toBe('entrance.jpg');
  });

  it('escapes the file name (no markup injection via textContent)', () => {
    const [, caption] = photoPopupNodes({ name: '<img src=x onerror=alert(1)>' }) as [Node, HTMLElement];
    expect(caption.querySelector('img')).toBeNull(); // rendered as text, not parsed as HTML
    expect(caption.textContent).toBe('<img src=x onerror=alert(1)>');
  });
});
