// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { PhotoInfo } from '../../api/hooks.ts';
import { viewPhoto } from './photoView.ts';

const listed = {
  documentId: 'doc-1',
  fileId: 'file-1',
  title: 'DSC_0042.jpg',
  originalName: 'DSC_0042.jpg',
  sizeBytes: 1024,
  width: 4000,
  height: 3000,
  orientationQuarterTurns: 0,
  visibility: 'internal',
  cavingGroupId: null,
  takenAt: null,
  createdAt: '2026-08-10T09:00:00Z',
  thumbnailUrl: '/api/v1/files/file-1/thumbnail?size=480',
  previewUrl: '/api/v1/files/file-1/thumbnail?size=2400',
  contentUrl: '/api/v1/files/file-1/content',
  mayDownloadOriginal: true,
  credit: {
    photographerCaverId: 'caver-1',
    photographerName: 'Ana Pop',
    caption: 'The entrance in winter',
    licenceCode: 'cc-by-sa',
    placeName: 'Padiș',
    inPublicGallery: false,
  },
  photo: { cameraModel: 'X-T5' },
  position: null,
} as unknown as PhotoInfo;

describe('viewPhoto', () => {
  it('lifts everything the credit holds up to where the viewer reads it', () => {
    // The defect this exists to stop: the listing nests these under a credit and both the grid
    // and the viewer read them flat, so a forgotten field shows as a photograph nobody has
    // captioned rather than as an error.
    const view = viewPhoto(listed);

    expect(view.caption).toBe('The entrance in winter');
    expect(view.photographerName).toBe('Ana Pop');
    expect(view.licenceCode).toBe('cc-by-sa');
    expect(view.placeName).toBe('Padiș');
  });

  it('keeps what the grid lays a tile out from', () => {
    const view = viewPhoto(listed);

    // Both are needed before the bytes arrive, or the grid reflows as pictures load — which is
    // the thing lazy loading is supposed to make bearable.
    expect(view.width).toBe(4000);
    expect(view.height).toBe(3000);
    expect(view.thumbnailUrl).toContain('size=480');
    expect(view.previewUrl).toContain('size=2400');
  });

  it('carries the server’s answer about the stored bytes rather than deciding one', () => {
    expect(viewPhoto(listed).mayDownloadOriginal).toBe(true);
    expect(viewPhoto({ ...listed, mayDownloadOriginal: false }).mayDownloadOriginal).toBe(false);
  });
});
