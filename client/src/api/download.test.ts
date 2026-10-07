// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';

vi.mock('../auth/auth.tsx', () => ({ userManager: { getUser: () => Promise.resolve(null) } }));
const { downloadFileForm, saveBlob } = await import('./download.ts');

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
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

describe('downloadFileForm', () => {
  /**
   * A form's content type carries the boundary between its parts, and only the browser knows
   * what boundary it wrote. Setting the header by hand would announce a form whose parts the
   * server cannot find — so the form goes exactly as it is and the header is left alone.
   */
  it('posts the form as it is, leaves its content type to the browser, and saves what comes back', async () => {
    const fetched = vi.fn(
      async (_url: string, _init: RequestInit) =>
        new Response(new Blob(['a document']), {
          status: 200,
          headers: { 'content-disposition': 'attachment; filename="trip-report-1a2b3c4d-20261006.docx"' },
        }),
    );
    vi.stubGlobal('fetch', fetched);
    URL.createObjectURL = vi.fn(() => 'blob:document');
    URL.revokeObjectURL = vi.fn();
    const saved: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      saved.push(this.download);
    });
    const form = new FormData();
    form.append('map', new Blob(['a picture'], { type: 'image/png' }), 'map.png');

    await downloadFileForm('/api/v1/trip-logs/t/report/download?', form);

    const [url, init] = fetched.mock.calls[0];
    expect(url).toBe('/api/v1/trip-logs/t/report/download?');
    expect(init.method).toBe('POST');
    expect(init.body).toBe(form);
    expect(init.headers).not.toHaveProperty('Content-Type');
    expect(saved).toEqual(['trip-report-1a2b3c4d-20261006.docx']);
  });

  it('hands a refusal back with the server’s own code, so a caller can tell what was refused', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(JSON.stringify({ status: 400, code: 'trip_report.map_too_large' }), {
            status: 400,
            headers: { 'content-type': 'application/problem+json' },
          }),
      ),
    );

    await expect(downloadFileForm('/api/v1/trip-logs/t/report/download?', new FormData())).rejects.toMatchObject({
      name: 'DownloadError',
      status: 400,
      code: 'trip_report.map_too_large',
    });
  });
});
