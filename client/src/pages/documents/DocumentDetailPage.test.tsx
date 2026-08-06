// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const documentTypes = [
  { id: 1, code: 'survey_report', name: 'Survey report', description: null, sortOrder: 10,
    metadataSchemaVersion: 1, metadataSchema: null },
];

const cabinets = [
  { id: 'cab-1', parentId: null, name: 'Club archive', description: null,
    ancestorIds: ['cab-1'], documentCount: 2 },
  { id: 'cab-2', parentId: 'cab-1', name: '1987', description: null,
    ancestorIds: ['cab-1', 'cab-2'], documentCount: 1 },
  { id: 'cab-3', parentId: null, name: 'Peștera X', description: null,
    ancestorIds: ['cab-3'], documentCount: 0 },
];

let doc = {
  id: 'doc-1',
  title: 'Ridicare topografică',
  documentTypeId: 1 as number | null,
  documentTypeCode: 'survey_report',
  metadata: null,
  metadataSchemaVersion: 1,
  visibility: 'private',
  cavingGroupId: null,
  cabinetIds: ['cab-2'],
  currentFileId: 'file-1',
  currentVersionNumber: 2,
  mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  sizeBytes: 3 * 1024 * 1024,
  kind: 'document',
  pageCount: 42,
  textExtraction: 'extracted',
  language: 'ro' as string | null,
  author: 'A. Speolog',
  producer: null,
  contentCreatedAt: null,
  contentModifiedAt: null,
  durationSeconds: null,
  codec: null,
  createdAt: '2026-01-02T10:00:00Z',
  updatedAt: '2026-02-03T11:00:00Z',
};

let fileInfo = {
  id: 'file-1',
  documentId: 'doc-1',
  originalName: 'ridicare.docx',
  mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  sizeBytes: 3 * 1024 * 1024,
  sha256: 'abc',
  kind: 'document',
  versionNumber: 2,
  documentDate: null,
  createdAt: '2026-01-02T10:00:00Z',
  contentUrl: '/api/v1/files/file-1/content?token=full',
  thumbnailUrl: null as string | null,
  mayDownloadOriginal: true,
  // The server names the file whose pages are drawn and how many there are. An office
  // document has no pages of its own, so this is the portable copy something made of it —
  // which is why the strip below is what draws it.
  pagesUrl: '/api/v1/files/file-2/content?token=full' as string | null,
  pageCount: 42 as number | null,
  conversion: 'converted',
};

let rights = 'read, write';

// One link naming this document and a cave, so the page's own links section has something
// to render. The section itself is exercised by its own tests; here it only has to prove
// that this page carries it.
const links = [
  {
    id: 'l1',
    shortCode: 'Ab3xY9Zq',
    relationType: {
      id: 1, code: 'documents', name: 'Documents', description: null, sortOrder: 40,
      directed: true, inverseName: 'Documented by', seeded: true,
    },
    description: null,
    createdBy: 'someone-else',
    createdAt: '2026-02-03T11:00:00Z',
    updatedAt: '2026-02-03T11:00:00Z',
    members: [
      { id: 'm1', targetType: 'document', targetId: 'doc-1', isMain: true, sortOrder: 0,
        note: null, anchorKind: 'whole', anchor: null, anchorFileId: null, anchorState: 'exact',
        display: { title: 'Ridicare topografică', subtitle: null, route: null, thumbnailUrl: null } },
      { id: 'm2', targetType: 'feature', targetId: 'f1', isMain: false, sortOrder: 1,
        note: null, anchorKind: 'whole', anchor: null, anchorFileId: null, anchorState: 'exact',
        display: { title: 'Peștera Demo Mare', subtitle: null, route: '/caves/f1', thumbnailUrl: null } },
    ],
  },
];

vi.mock('../../api/hooks.ts', () => ({
  useDocument: () => ({ data: doc, isPending: false, isError: false }),
  useFile: () => ({ data: fileInfo }),
  // Refetching the file once its text has been read is the query cache's business, and it has
  // its own test; here the page only has to survive calling it.
  useRefreshFileWhenTextRead: () => undefined,
  useDocumentTypes: () => ({ data: documentTypes }),
  useCabinets: () => ({ data: cabinets }),
  useCan: (_domain: string, action: string) =>
    rights.split(',').map((a) => a.trim()).includes(action),
  useResLinksForTarget: () => ({ data: { items: links, page: 1, pageSize: 200, totalItems: 1 } }),
  useDeleteResLink: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useMe: () => ({ data: { id: 'me' } }),
  useMyPermissionGroups: () => ({ data: [] }),
}));

// The properties panel and the version chain are exercised by their own tests; here they
// only need to say whether the page offered them.
vi.mock('../../components/attachments/DocumentMetadata.tsx', () => ({
  default: () => <div>document-properties</div>,
}));
// The discussion has its own tests; here it is only expected to be on the page.
vi.mock('../../components/documents/DocumentComments.tsx', () => ({
  default: () => <div>document-comments</div>,
}));
vi.mock('../../components/attachments/FileVersions.tsx', () => ({
  default: ({ canEdit }: { canEdit: boolean }) => (
    <div>{canEdit ? 'versions-editable' : 'versions-readonly'}</div>
  ),
}));

let mobile = false;
vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => mobile }));

const { default: DocumentDetailPage } = await import('./DocumentDetailPage.tsx');

afterEach(cleanup);

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/documents/doc-1']}>
      <App>
        <Routes>
          <Route path="/documents/:id" element={<DocumentDetailPage />} />
        </Routes>
      </App>
    </MemoryRouter>,
  );
}

/** Every sentence the page can say about the reading — one of them, and only one, per state. */
const textSentences = [
  'This document has no text layer: it is pictures of pages, so there are no words in it to read.',
  'Reading the text…',
  'The text has been read.',
  'The text could not be read.',
  "Nothing here can read this format's text yet.",
  'This kind of file holds no text to read.',
];

describe('DocumentDetailPage', () => {
  it('gives a document its own page: what it is, where it is filed, and what came of reading it', () => {
    rights = 'read, write';
    renderPage();

    expect(screen.getByRole('heading', { name: 'Ridicare topografică' })).toBeInTheDocument();
    expect(screen.getByText('Survey report')).toBeInTheDocument();
    expect(
      screen.getByText('application/vnd.openxmlformats-officedocument.wordprocessingml.document'),
    ).toBeInTheDocument();
    expect(screen.getByText('3.0 MB')).toBeInTheDocument();

    // The whole path, because a shelf named "1987" sits under many archives.
    expect(screen.getByText('Club archive / 1987')).toBeInTheDocument();

    // Exactly one of the six sentences, and it is the true one.
    expect(screen.getByText('The text has been read.')).toBeInTheDocument();
    for (const sentence of textSentences.filter((s) => s !== 'The text has been read.')) {
      expect(screen.queryByText(sentence)).not.toBeInTheDocument();
    }

    // The document itself is on the page, drawn a page at a time from pictures the server
    // makes — not the stored file, which is a separate question with a separate answer.
    const drawn = screen.getAllByRole('img').map((image) => image.getAttribute('src') ?? '');
    expect(drawn.some((src) => src.includes('/pages/1/render'))).toBe(true);
    expect(drawn.some((src) => src.includes('/content'))).toBe(false);

    // Downloading is still on offer — it has stopped being the only thing on offer.
    for (const download of screen.getAllByRole('link', { name: /Download/ })) {
      expect(download).toHaveAttribute('href', '/api/v1/files/file-1/content?token=full');
    }
  });

  it('offers the properties panel to a writer and withholds it from a reader who cannot write', () => {
    rights = 'read, write';
    renderPage();
    expect(screen.getByText('document-properties')).toBeInTheDocument();
    expect(screen.getByText('versions-editable')).toBeInTheDocument();
    cleanup();

    // The same page, same document, one right fewer: the control the previous assertion
    // used is gone rather than present-and-refused.
    rights = 'read';
    renderPage();
    expect(screen.queryByText('document-properties')).not.toBeInTheDocument();
    expect(screen.getByText('versions-readonly')).toBeInTheDocument();
    // Reading is untouched by not being able to write.
    expect(screen.getByRole('heading', { name: 'Ridicare topografică' })).toBeInTheDocument();
  });

  it('carries the document\'s links as a section of its own, for a reader who cannot write', () => {
    // The properties panel is write-gated and is the other place these links appear; with
    // that right withheld, the section on the page is the only one left — which is exactly
    // the reader this assertion is about.
    rights = 'read';
    renderPage();

    expect(screen.queryByText('document-properties')).not.toBeInTheDocument();
    expect(screen.getByText('Linked items (1)')).toBeInTheDocument();
    // The other end is named and reachable; the document itself is not repeated as a chip
    // on its own page.
    expect(screen.getByRole('link', { name: /Peștera Demo Mare/ })).toHaveAttribute(
      'href',
      '/caves/f1',
    );
    expect(screen.getByText('Documented by')).toBeInTheDocument();
  });

  it('shows a photo from a rendering, and never the original, when the caller may not place it', () => {
    rights = 'read';
    const photo = { ...doc, mimeType: 'image/jpeg', kind: 'image', textExtraction: 'notApplicable' };
    const entitled = {
      ...fileInfo,
      originalName: 'intrare.jpg',
      mimeType: 'image/jpeg',
      kind: 'image',
      thumbnailUrl: '/api/v1/files/file-1/thumbnail?size=480&token=full',
      mayDownloadOriginal: true,
      // A picture is not paged, whatever else it is.
      pagesUrl: null,
      pageCount: 1,
    };

    doc = photo;
    fileInfo = entitled;
    renderPage();
    // Someone who may place what the photo shows sees the stored bytes and may take them.
    expect(screen.getByRole('img', { name: 'intrare.jpg' })).toHaveAttribute(
      'src',
      '/api/v1/files/file-1/content?token=full',
    );
    expect(screen.getByRole('link', { name: /Download/ })).toBeInTheDocument();
    cleanup();

    // The same photo of the same protected cave, to someone holding no right to place it:
    // the token it is given reaches renderings only, so the original is not fetched to
    // display it either, and the control that would fetch it is gone rather than broken.
    fileInfo = {
      ...entitled,
      contentUrl: '/api/v1/files/file-1/content?token=derivatives',
      thumbnailUrl: '/api/v1/files/file-1/thumbnail?size=480&token=derivatives',
      mayDownloadOriginal: false,
    };
    renderPage();
    const shown = screen.getByRole('img', { name: 'intrare.jpg' });
    expect(shown.getAttribute('src')).toContain('size=1200');
    expect(shown.getAttribute('src')).not.toContain('/content');
    expect(screen.queryByRole('link', { name: /Download/ })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Download/ })).toBeDisabled();
    expect(
      screen.getByText(
        'You may not download the original of this photo: it records where it was taken,'
          + ' and you do not have the right to place what it shows.',
      ),
    ).toBeInTheDocument();
  });

  /**
   * A portable document the caller may have the bytes of is laid out in the browser instead of
   * being shown as pictures of its pages, so its words are text: selectable, copyable, findable
   * with the browser's own search and reachable by a screen reader.
   *
   * What is asserted here is the choice, not the rendering — laying a PDF out needs a canvas
   * and a worker, neither of which exists under this test's DOM. The rendering itself is driven
   * in a real browser by the end-to-end suite, which is the only place it can be.
   */
  it('reads a portable document in the browser rather than as pictures of its pages', () => {
    rights = 'read';
    doc = { ...doc, mimeType: 'application/pdf', kind: 'document', textExtraction: 'extracted' };
    fileInfo = {
      ...fileInfo,
      originalName: 'ridicare.pdf',
      mimeType: 'application/pdf',
      kind: 'document',
      mayDownloadOriginal: true,
      pagesUrl: '/api/v1/files/file-1/content?token=full',
      conversion: 'notApplicable',
    };
    renderPage();

    expect(
      screen.getByText('Select text on the page to copy it, or to point a link at the passage.'),
    ).toBeInTheDocument();
    // And nothing asked the server to draw the page, which is the half that would otherwise
    // pass unnoticed: both viewers show a page, only one of them shows words.
    const drawn = screen.getAllByRole('img').map((image) => image.getAttribute('src') ?? '');
    expect(drawn.some((src) => src.includes('/pages/1/render'))).toBe(false);
  });

  it('reads on a phone: the same controls, named rather than revealed, and nothing that needs a hover', () => {
    rights = 'read';
    // Spelled out rather than inherited: these fixtures are module-level and every test before
    // this one may have moved them, and which viewer is chosen is decided from exactly these
    // fields — the strip is what this test is about.
    const office = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
    doc = { ...doc, mimeType: office, kind: 'document', textExtraction: 'extracted' };
    fileInfo = {
      ...fileInfo,
      originalName: 'ridicare.docx',
      mimeType: office,
      kind: 'document',
      contentUrl: '/api/v1/files/file-1/content?token=full',
      thumbnailUrl: null,
      mayDownloadOriginal: true,
      pagesUrl: '/api/v1/files/file-2/content?token=full',
      pageCount: 42,
      conversion: 'converted',
    };

    for (const narrow of [false, true]) {
      mobile = narrow;
      renderPage();

      // Turning pages is a pair of ordinary buttons with names of their own. Nothing has to
      // be hovered to find them, which is the whole point: on a phone there is no hover, so a
      // control that only appears under a pointer is a control that page has lost.
      expect(screen.getByRole('button', { name: 'Next page' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Previous page' })).toBeDisabled();
      expect(screen.getByText('Page 1 of 42')).toBeInTheDocument();
      // The page picture shrinks to whatever room there is rather than pushing the document
      // sideways under a thumb.
      expect(screen.getByRole('img', { name: 'Page 1' })).toHaveStyle({ maxWidth: '100%' });
      cleanup();
    }
    mobile = false;
  });
});
