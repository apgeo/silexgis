// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import type { FileInfo } from '../../api/hooks.ts';
import DocumentContent from './DocumentContent.tsx';

afterEach(cleanup);

const photo: FileInfo = {
  id: 'f1',
  documentId: 'd1',
  originalName: 'entrance.jpg',
  mimeType: 'image/jpeg',
  sizeBytes: 1024,
  sha256: 'x',
  kind: 'image',
  versionNumber: 1,
  documentDate: null,
  createdAt: '2026-01-01T00:00:00Z',
  contentUrl: '/api/v1/files/f1/content?token=t',
  thumbnailUrl: '/api/v1/files/f1/thumbnail?size=480&token=t',
  mayDownloadOriginal: true,
};

const report: FileInfo = {
  ...photo,
  id: 'f2',
  documentId: 'd2',
  originalName: 'report.pdf',
  mimeType: 'application/pdf',
  kind: 'document',
  contentUrl: '/api/v1/files/f2/content?token=t',
  thumbnailUrl: null,
};

const recording: FileInfo = {
  ...photo,
  id: 'f3',
  documentId: 'd3',
  originalName: 'interview.mp3',
  mimeType: 'audio/mpeg',
  kind: 'audio',
  contentUrl: '/api/v1/files/f3/content?token=t',
  thumbnailUrl: null,
};

function sources(): string[] {
  return screen.getAllByRole('img').map((image) => image.getAttribute('src') ?? '');
}

describe('DocumentContent', () => {
  it('shows a photo from the rendering when the original is withheld, and from the file when it is not', () => {
    // The same photo, one right fewer. A caller who may not place what it shows must not be
    // handed the stored bytes by the screen that displays it — those still carry the fix the
    // camera wrote — while a caller who may is shown the file itself.
    render(
      <App>
        <DocumentContent file={{ ...photo, mayDownloadOriginal: false }} />
      </App>,
    );
    expect(sources().some((src) => src.includes('/content'))).toBe(false);
    expect(sources().some((src) => src.includes('size=1200'))).toBe(true);
    expect(screen.getByText(/may not download the original/i)).toBeTruthy();

    cleanup();
    render(
      <App>
        <DocumentContent file={photo} />
      </App>,
    );
    expect(sources().some((src) => src.includes('/content'))).toBe(true);
  });

  it('draws a paged document page by page and never fetches the document itself', () => {
    render(
      <App>
        <DocumentContent file={report} pageCount={12} initialPage={5} />
      </App>,
    );

    expect(sources().some((src) => src.includes('/pages/5/render'))).toBe(true);
    expect(sources().some((src) => src.includes('/content'))).toBe(false);
    expect(screen.getByText('Page 5 of 12')).toBeTruthy();
  });

  it('says nothing about pages when the document has not said how many it has', () => {
    // "Page 1 of 1" over a report nobody has counted would be this interface stating a fact
    // it was never given, so the page is shown and the count is not claimed.
    render(
      <App>
        <DocumentContent file={report} pageCount={null} />
      </App>,
    );

    expect(sources().some((src) => src.includes('/pages/1/render'))).toBe(true);
    expect(screen.queryByText(/Page 1 of/)).toBeNull();
  });

  it('names the format as the reason when it cannot be shown, and offers the download', () => {
    render(
      <App>
        <DocumentContent
          file={{ ...report, mimeType: 'application/vnd.ms-excel', originalName: 'sheet.xls' }}
        />
      </App>,
    );

    expect(screen.getByText(/Nothing here can show this format yet/)).toBeTruthy();
    expect(screen.getByRole('link', { name: /Download/ }).getAttribute('href')).toContain(
      '/content',
    );
  });

  it('plays a recording where the browser can, and says what happened where it cannot', () => {
    const { container } = render(
      <App>
        <DocumentContent file={recording} />
      </App>,
    );

    // The recording is played from the file itself, because there is no rendering of a sound
    // and nothing here converts one.
    const player = container.querySelector('audio');
    expect(player?.getAttribute('src')).toContain('/content');
    // Nothing is claimed about a recording that has not failed yet.
    expect(screen.queryByText(/will not play here/)).toBeNull();
    expect(screen.queryByText(/Nothing here can show this format/)).toBeNull();

    // A codec the browser refuses is a different sentence from a format nothing can show, and
    // the file that is unharmed is still offered.
    fireEvent.error(player!);
    expect(screen.getByText(/will not play here/)).toBeTruthy();
    expect(screen.getByRole('link', { name: /Download/ }).getAttribute('href')).toContain(
      '/content',
    );
  });

  it('plays a video in place rather than only offering it, and never plays bytes it may not have', () => {
    const clip: FileInfo = {
      ...recording,
      originalName: 'passage.mp4',
      mimeType: 'video/mp4',
      kind: 'video',
    };

    const { container, rerender } = render(
      <App>
        <DocumentContent file={clip} />
      </App>,
    );
    expect(container.querySelector('video')?.getAttribute('src')).toContain('/content');

    // The same clip, one right fewer. A player is not an exception to who may have a file: with
    // the original withheld there is nothing to play, and that is said rather than shown empty.
    rerender(
      <App>
        <DocumentContent file={{ ...clip, mayDownloadOriginal: false }} />
      </App>,
    );
    expect(container.querySelector('video')).toBeNull();
    expect(container.querySelector('audio')).toBeNull();
    expect(screen.getByText(/may not download the original/i)).toBeTruthy();
  });

  it('says a page would not draw rather than leaving a broken picture, and keeps the rest reachable', () => {
    render(
      <App>
        <DocumentContent file={report} pageCount={12} initialPage={5} />
      </App>,
    );

    // While the page is drawing there is nothing to say about it, and nothing is said.
    expect(screen.queryByText(/could not be drawn/)).toBeNull();

    fireEvent.error(screen.getAllByRole('img')[0]);

    // The one page that failed is named as the thing that failed — not the document, not the
    // format — and the file that is unharmed is still offered.
    expect(screen.getByText(/This page could not be drawn/)).toBeTruthy();
    expect(screen.getByRole('link', { name: /Download/ }).getAttribute('href')).toContain(
      '/content',
    );
    // The pager survives the failure, because the next page may well draw.
    expect(screen.getByText('Page 5 of 12')).toBeTruthy();
  });

  it('tries a page again once the delivery link is renewed rather than writing it off', () => {
    const { rerender } = render(
      <App>
        <DocumentContent file={report} pageCount={12} initialPage={5} />
      </App>,
    );

    fireEvent.error(screen.getAllByRole('img')[0]);
    expect(screen.getByText(/This page could not be drawn/)).toBeTruthy();

    // The link that fetches a page lapses after some minutes and is re-minted, which is the
    // ordinary course of a long read. A page that would not load under the lapsed one is not a
    // damaged page, and treating it as one strands the reader on it until they navigate away.
    rerender(
      <App>
        <DocumentContent
          file={{ ...report, contentUrl: '/api/v1/files/f2/content?token=renewed' }}
          pageCount={12}
          initialPage={5}
        />
      </App>,
    );

    expect(screen.queryByText(/This page could not be drawn/)).toBeNull();
    expect(sources().some((src) => src.includes('token=renewed'))).toBe(true);
  });
});
