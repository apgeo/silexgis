// SPDX-License-Identifier: AGPL-3.0-or-later
import { Empty, Flex, Image, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FileInfo } from '../../api/hooks.ts';
import { displayableImageUrl } from './derivativeUrl.ts';
import DownloadDocument from './DownloadDocument.tsx';
import MediaDocumentView from './MediaDocumentView.tsx';
import PagedDocumentView from './PagedDocumentView.tsx';
import PdfDocumentView from './PdfDocumentView.tsx';
import TextDocumentView from './TextDocumentView.tsx';

/**
 * What to say about a document nothing can draw pages of, when the reason is worth saying.
 *
 * Only one of these is about the document. "Nothing here can lay this format out" is a fact
 * about this installation — laying out a word-processor document needs an office suite, which
 * is an optional service a small site is not expected to run — and telling someone their file
 * is broken when the truth is that nobody deployed the converter is the mistake this exists to
 * prevent. "The attempt did not finish" is narrower still: it is about this one document on
 * this one occasion, and saying it as either of the other two would be wrong in both
 * directions. Null means there is nothing useful to add beyond the format not being
 * displayable.
 */
function conversionMessage(conversion: FileInfo['conversion']): string | null {
  switch (conversion) {
    case 'pending':
      return 'documents.viewer.conversionPending';
    case 'unavailable':
      return 'documents.viewer.conversionUnavailable';
    case 'deferred':
      return 'documents.viewer.conversionDeferred';
    case 'failed':
      return 'documents.viewer.conversionFailed';
    default:
      return null;
  }
}

/**
 * Whether a format's bytes are text a person can read directly. A delimited table and a set
 * of notes in markup both qualify: their source is the document.
 */
function isText(mimeType: string): boolean {
  return mimeType.startsWith('text/');
}

/** Whether the bytes are a portable document the browser can be made to lay out itself. */
function isPdf(mimeType: string): boolean {
  return mimeType.split(';')[0].trim().toLowerCase() === 'application/pdf';
}

/**
 * Above this, a portable document is read as pictures of its pages rather than laid out here.
 *
 * Laying one out in the browser means holding the whole file in memory, because the alternative
 * — letting the renderer fetch its own byte ranges as the reader moves — asks for them under a
 * delivery link that expires part-way through a long read, and a document that stops arriving in
 * the middle is worse than one that was never laid out. So the trade is made on size, at a bound
 * a phone can hold: a survey report is a few megabytes and reads with selectable text, while a
 * scanned volume is drawn a page at a time by the server, which is what that path is for and
 * costs the reader nothing but the selection.
 */
export const maxBrowserPdfBytes = 64 * 1024 * 1024;

/**
 * The document itself, shown where it can be shown and named honestly where it cannot.
 *
 * Two silences are kept apart deliberately. "There is nothing in this file to display" is a
 * fact about the document; "this application cannot draw this format" is a fact about this
 * application. A blank panel says neither, and leaves someone waiting for something that is
 * never going to appear — so every branch that cannot render ends in a sentence and the
 * download that does work.
 *
 * Nothing here reaches for the stored bytes in order to display something the caller may not
 * have. A photo records where it was taken and a page picture does not, so a caller who may
 * not be told the first is shown the largest *rendering* they are entitled to; a document
 * whose pages are drawn is shown those drawings. Where there is no rendering to fall back on
 * — the words of a text file, the sound of a recording — the branch is taken only for a caller
 * who may have the file anyway, and everyone else is told so. Displaying is not an exception
 * to the rule about who may have a file.
 */
export default function DocumentContent({
  file,
  initialPage,
}: {
  file: FileInfo;
  initialPage?: number;
}) {
  const { t } = useTranslation();

  if (file.kind === 'image') {
    const src = displayableImageUrl(file);
    if (src !== null) {
      return (
        <Flex vertical gap={8} align="start">
          <Image src={src} alt={file.originalName} style={{ maxWidth: '100%' }} />
          {!file.mayDownloadOriginal && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('attachments.originalWithheld')}
            </Typography.Text>
          )}
        </Flex>
      );
    }
  }

  // A portable document is read in the browser when the caller may have its bytes anyway: the
  // words are then real text rather than part of a picture, so they can be selected, copied,
  // found with the browser's own search and read aloud. A caller the bytes are withheld from
  // falls through to the page pictures below, which show the same page and disclose nothing
  // more than it does — displaying is never a way around who may have a file.
  if (isPdf(file.mimeType) && file.mayDownloadOriginal && file.sizeBytes <= maxBrowserPdfBytes) {
    return <PdfDocumentView file={file} initialPage={initialPage} />;
  }

  // Which file's pages can be drawn is the server's statement, not a guess made here from a
  // media type: an office document has no pages of its own, and where something has converted
  // one into a portable copy the pages — and the page numbers — belong to that copy.
  if (file.pagesUrl !== null && file.pagesUrl !== undefined) {
    return (
      <PagedDocumentView
        file={file}
        pagesUrl={file.pagesUrl}
        pageCount={file.pageCount ?? null}
        initialPage={initialPage}
      />
    );
  }

  // A recording is played by the browser from the stored bytes, since there is no rendering of
  // a sound to fall back on and nothing here converts one. A caller who may not have the bytes
  // is told that instead, by the branch below.
  if ((file.kind === 'audio' || file.kind === 'video') && file.mayDownloadOriginal) {
    return <MediaDocumentView file={file} />;
  }

  // Text needs the bytes themselves, and there is no rendering of a text file to fall back
  // on. A caller who may not have them is told so rather than shown an empty panel.
  if (isText(file.mimeType) && file.mayDownloadOriginal) {
    return <TextDocumentView file={file} />;
  }

  const conversion = conversionMessage(file.conversion);
  return (
    <Empty
      image={Empty.PRESENTED_IMAGE_SIMPLE}
      description={
        <Flex vertical gap={8} align="center">
          <Typography.Text type="secondary">
            {!file.mayDownloadOriginal
              ? t('attachments.originalWithheld')
              : conversion !== null
                ? t(conversion)
                : t('documents.viewer.notDisplayable')}
          </Typography.Text>
          <DownloadDocument file={file} />
        </Flex>
      }
    />
  );
}
