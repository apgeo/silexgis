// SPDX-License-Identifier: AGPL-3.0-or-later
import { beforeEach, describe, expect, it, vi } from 'vitest';

const downloadFilePost = vi.fn<(url: string, body: unknown) => Promise<void>>();

vi.mock('../../api/download.ts', () => ({
  downloadFilePost: (url: string, body: unknown) => downloadFilePost(url, body),
}));

const { exportKarstLink, exportKarstLinkWithStoredAnswer } = await import('./karstLinkExport.ts');

/**
 * Which field the answer travels in, because the two are not interchangeable and only one of
 * them is what "I already decided this" means.
 *
 * An answer given in the dialog is the answer for this export and overrides anything stored;
 * an answer carried over from a previous export is the fallback the server consults last, so a
 * treatment named for one particular cave still beats it. Sending the stored one as "apply to
 * all" would silently promote a months-old preference over a decision made about a single
 * cave, and nothing in the file would show that it had happened.
 */
describe('the interchange export request', () => {
  beforeEach(() => {
    downloadFilePost.mockReset();
    downloadFilePost.mockResolvedValue();
  });

  it('carries an answer just given as the answer for every protected cave', async () => {
    await exportKarstLink({ search: 'ursilor' }, 'omit');

    expect(downloadFilePost).toHaveBeenCalledTimes(1);
    const [url, body] = downloadFilePost.mock.calls[0];
    expect(url).toBe('/api/v1/export/caves/karstlink');
    expect(body).toEqual({ search: 'ursilor', treatmentForAll: 'omit' });
  });

  it('carries a remembered answer as the default the server falls back to', async () => {
    await exportKarstLinkWithStoredAnswer({ tag: 'karst' }, 'grid_position');

    const [, body] = downloadFilePost.mock.calls[0];
    expect(body).toEqual({ tag: 'karst', defaultTreatment: 'grid_position' });

    // And never as the answer for all, which would outrank a per-cave decision.
    expect(body).not.toHaveProperty('treatmentForAll');
  });
});
