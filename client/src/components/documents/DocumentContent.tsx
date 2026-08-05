// SPDX-License-Identifier: AGPL-3.0-or-later
import { Empty, Flex, Image, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FileInfo } from '../../api/hooks.ts';
import { displayableImageUrl } from './derivativeUrl.ts';
import DownloadDocument from './DownloadDocument.tsx';
import MediaDocumentView from './MediaDocumentView.tsx';
import PagedDocumentView from './PagedDocumentView.tsx';
import TextDocumentView from './TextDocumentView.tsx';

/** Formats whose pages are drawn on the server, page by page. */
const pagedFormats = ['application/pdf'];

/**
 * Whether a format's bytes are text a person can read directly. A delimited table and a set
 * of notes in markup both qualify: their source is the document.
 */
function isText(mimeType: string): boolean {
  return mimeType.startsWith('text/');
}

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
  pageCount = null,
  initialPage,
}: {
  file: FileInfo;
  pageCount?: number | null;
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

  if (pagedFormats.includes(file.mimeType)) {
    return <PagedDocumentView file={file} pageCount={pageCount} initialPage={initialPage} />;
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

  return (
    <Empty
      image={Empty.PRESENTED_IMAGE_SIMPLE}
      description={
        <Flex vertical gap={8} align="center">
          <Typography.Text type="secondary">
            {file.mayDownloadOriginal
              ? t('documents.viewer.notDisplayable')
              : t('attachments.originalWithheld')}
          </Typography.Text>
          <DownloadDocument file={file} />
        </Flex>
      }
    />
  );
}
