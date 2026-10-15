// SPDX-License-Identifier: AGPL-3.0-or-later
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { LOCHVIEWER_PAGE, lochViewerUrl } from './lochViewer.ts';

const publicDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'public');

describe('lochViewerUrl', () => {
  const signed = '/api/v1/files/abc/content?token=s3cr3t';

  it('keeps the signed file address out of the query, where logs and Referers would carry it', () => {
    const url = lochViewerUrl(signed, 'Pestera.lox', 'https://gis.example');

    const [beforeHash, hash] = url.split('#');
    expect(beforeHash).toBe(LOCHVIEWER_PAGE);
    expect(beforeHash).not.toContain('token');
    expect(new URLSearchParams(hash).get('model')).toBe(
      'https://gis.example/api/v1/files/abc/content?token=s3cr3t&name=Pestera.lox',
    );
  });

  it('names the file by the extension the viewer chooses its parser by', () => {
    const url = lochViewerUrl(signed, 'Survey v2.3d', 'https://gis.example');
    const model = new URL(new URLSearchParams(url.split('#')[1]).get('model')!);

    expect(model.searchParams.get('name')).toBe('Survey v2.3d');
    // The token is passed through untouched — the viewer fetches exactly what it was given.
    expect(model.searchParams.get('token')).toBe('s3cr3t');
  });

  it('points at a page that is actually vendored', () => {
    expect(existsSync(join(publicDir, LOCHVIEWER_PAGE)), `${LOCHVIEWER_PAGE} is vendored`).toBe(true);
  });
});
