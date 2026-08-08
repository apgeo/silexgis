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
  pagesUrl: null,
  pageCount: null,
  conversion: 'notApplicable',
  contentCreatedAt: null,
  photo: null,
  position: null,
};

/**
 * An office document that something has converted, which is what the server-drawn page strip
 * exists for: the file has no pages of its own, so the pages — and their numbers — belong to
 * the portable copy, and its delivery URL is what the strip draws from.
 *
 * A portable document the caller may have the bytes of takes the other branch and is laid out
 * in the browser; that choice has a test of its own below.
 */
const report: FileInfo = {
  ...photo,
  id: 'f2',
  documentId: 'd2',
  originalName: 'report.docx',
  mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  kind: 'document',
  contentUrl: '/api/v1/files/f2/content?token=t',
  thumbnailUrl: null,
  pagesUrl: '/api/v1/files/f2/content?token=t',
  pageCount: 3,
  conversion: 'converted',
  contentCreatedAt: null,
  photo: null,
  position: null,
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

/** A portable document, whose two branches turn on how far this caller may reach. */
const portable: FileInfo = {
  ...report,
  id: 'f4',
  documentId: 'd4',
  originalName: 'report.pdf',
  mimeType: 'application/pdf',
  contentUrl: '/api/v1/files/f4/content?token=t',
  pagesUrl: '/api/v1/files/f4/content?token=t',
  conversion: 'notApplicable',
  contentCreatedAt: null,
  photo: null,
  position: null,
};

describe('DocumentContent', () => {
  it('lays a portable document out in the browser when the caller may have its bytes', () => {
    render(
      <App>
        <DocumentContent file={portable} />
      </App>,
    );

    // Nothing asked the server to draw a page: the words on screen are text rather than part
    // of a picture, which is the whole difference between the two viewers.
    expect(sources().some((src) => src.includes('/pages/'))).toBe(false);
    expect(screen.getByText(/Select text on the page/i)).toBeTruthy();
  });

  it('falls back to pictures of the pages for a caller the bytes are withheld from', () => {
    // The same document, one right fewer. Laying it out in the browser needs the file itself,
    // so a caller who may not have it is shown the pages the server drew — which show what the
    // pages show and nothing more. Displaying is never a way around who may have a file.
    render(
      <App>
        <DocumentContent file={{ ...portable, mayDownloadOriginal: false }} />
      </App>,
    );

    expect(sources().some((src) => src.includes('/pages/1/render'))).toBe(true);
    expect(screen.queryByText(/Select text on the page/i)).toBeNull();
  });

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
        <DocumentContent file={{ ...report, pageCount: 12 }} initialPage={5} />
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
        <DocumentContent file={{ ...report, pageCount: null }} />
      </App>,
    );

    expect(sources().some((src) => src.includes('/pages/1/render'))).toBe(true);
    expect(screen.queryByText(/Page 1 of/)).toBeNull();
  });

  it('names the format as the reason when it cannot be shown, and offers the download', () => {
    render(
      <App>
        <DocumentContent
          file={{
            ...report,
            mimeType: 'application/vnd.ms-excel',
            originalName: 'sheet.xls',
            pagesUrl: null,
            pageCount: null,
          }}
        />
      </App>,
    );

    expect(screen.getByText(/Nothing here can show this format yet/)).toBeTruthy();
    expect(screen.getByRole('link', { name: /Download/ }).getAttribute('href')).toContain(
      '/content',
    );
  });

  it('blames the installation, not the document, when no converter is deployed here', () => {
    // The same office document twice, once where nothing can lay it out and once where
    // something has. The first must not read as a damaged file: it is a perfectly good
    // document and the gap is on this side, which is a different sentence and has to be
    // shown as one. The second proves the branch is reachable at all.
    const spreadsheet: FileInfo = {
      ...report,
      mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      originalName: 'inventory.xlsx',
      pagesUrl: null,
      pageCount: null,
      conversion: 'unavailable',
      contentCreatedAt: null,
      photo: null,
      position: null,
    };

    render(
      <App>
        <DocumentContent file={spreadsheet} />
      </App>,
    );
    expect(screen.getByText(/cannot lay out office documents/)).toBeTruthy();
    expect(screen.queryByText(/Nothing here can show this format yet/)).toBeNull();
    expect(screen.getByRole('link', { name: /Download/ }).getAttribute('href')).toContain(
      '/content',
    );

    cleanup();
    render(
      <App>
        <DocumentContent
          file={{
            ...spreadsheet,
            conversion: 'converted',
            contentCreatedAt: null,
            photo: null,
            position: null,
            // The pages belong to the copy something made of it, not to the upload.
            pagesUrl: '/api/v1/files/converted/content?token=t',
            pageCount: 4,
          }}
        />
      </App>,
    );
    expect(sources().some((src) => src.includes('/api/v1/files/converted/pages/1/render'))).toBe(
      true,
    );
    expect(screen.getByText('Page 1 of 4')).toBeTruthy();
    expect(screen.queryByText(/cannot lay out office documents/)).toBeNull();
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
        <DocumentContent file={{ ...report, pageCount: 12 }} initialPage={5} />
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
        <DocumentContent file={{ ...report, pageCount: 12 }} initialPage={5} />
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
          file={{
            ...report,
            pageCount: 12,
            contentUrl: '/api/v1/files/f2/content?token=renewed',
            pagesUrl: '/api/v1/files/f2/content?token=renewed',
          }}
          initialPage={5}
        />
      </App>,
    );

    expect(screen.queryByText(/This page could not be drawn/)).toBeNull();
    expect(sources().some((src) => src.includes('token=renewed'))).toBe(true);
  });
});
