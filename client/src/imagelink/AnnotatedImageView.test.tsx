// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import type { FileInfo } from '../api/hooks.ts';
import { DOCUMENT_LINK_PAGE_SIZE } from '../textlink/highlights.ts';

const asked: Array<Record<string, unknown> | undefined> = [];
let totalItems = 0;

vi.mock('../api/hooks.ts', () => ({
  useResLinksForTarget: (_type: string, _id: string, params?: Record<string, unknown>) => {
    asked.push(params);
    // A page as the server sends it: as many links as the page holds, or all there are. These
    // links mean nothing on the picture; only how many of them there are is under test.
    const items = Array.from({ length: Math.min(totalItems, DOCUMENT_LINK_PAGE_SIZE) }, (_, i) => ({
      id: `link-${i}`,
      members: [],
    }));
    return { data: { items, page: 1, pageSize: DOCUMENT_LINK_PAGE_SIZE, totalItems } };
  },
}));

// The picture itself is not what is under test, and jsdom does not draw.
vi.mock('./ImageRegionCanvas.tsx', () => ({ default: () => <div data-testid="canvas" /> }));

const { default: AnnotatedImageView } = await import('./AnnotatedImageView.tsx');

const file = {
  id: 'file-1',
  originalName: 'scan.png',
  contentUrl: '/c/file-1',
  thumbnailUrl: null,
  mayDownloadOriginal: true,
} as unknown as FileInfo;

function draw() {
  render(
    <MemoryRouter>
      <AnnotatedImageView documentId="doc-1" file={file} />
    </MemoryRouter>,
  );
}

afterEach(() => {
  cleanup();
  asked.length = 0;
  totalItems = 0;
});

describe('AnnotatedImageView', () => {
  it('reads as many of the picture’s links as the text reader does', () => {
    draw();
    expect(asked.at(-1)).toEqual({ pageSize: DOCUMENT_LINK_PAGE_SIZE });
  });

  it('says when the picture has more links than were read, and is silent when it has not', () => {
    totalItems = DOCUMENT_LINK_PAGE_SIZE + 40;
    draw();
    expect(
      screen.getByText(
        `Only the first ${DOCUMENT_LINK_PAGE_SIZE} of ${DOCUMENT_LINK_PAGE_SIZE + 40} links are drawn on this picture.`,
      ),
    ).toBeInTheDocument();

    cleanup();
    totalItems = 3;
    draw();
    expect(screen.queryByText(/links are drawn on this picture/)).not.toBeInTheDocument();
  });
});
