// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';

vi.mock('../auth/auth.tsx', () => ({ userManager: { getUser: () => Promise.resolve(null) } }));
const { saveBlob } = await import('./download.ts');

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('saveBlob', () => {
  it('saves the file under its name through a link in the document, and lets the file go only later', () => {
    vi.useFakeTimers();
    const created = vi.fn(() => 'blob:movie');
    const revoked = vi.fn();
    URL.createObjectURL = created;
    URL.revokeObjectURL = revoked;
    const clicked: { download: string; href: string; attached: boolean }[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push({ download: this.download, href: this.href, attached: this.isConnected });
    });
    const file = new Blob(['GIF89a'], { type: 'image/gif' });

    saveBlob(file, 'silexgis-alpha-2026-09-29.gif');

    expect(created).toHaveBeenCalledWith(file);
    expect(clicked).toEqual([{ download: 'silexgis-alpha-2026-09-29.gif', href: 'blob:movie', attached: true }]);
    expect(document.querySelector('a[download]')).toBeNull();
    // Still readable while the browser is saving it.
    expect(revoked).not.toHaveBeenCalled();
    vi.advanceTimersByTime(60_000);
    expect(revoked).toHaveBeenCalledWith('blob:movie');
  });
});
