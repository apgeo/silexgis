// SPDX-License-Identifier: AGPL-3.0-or-later
import { useRef } from 'react';
import { Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FileInfo } from '../../api/hooks.ts';
import PdfView from '../../pdf/PdfView.tsx';
import DownloadDocument from './DownloadDocument.tsx';

/**
 * A stored PDF, read where it is. This is the join between the document world — a file, its
 * delivery URL, what this caller may do with it — and the viewer, which knows only about bytes
 * at a URL and pages of them.
 *
 * Keeping the two apart is the point of the split: the same viewer shows a survey report here
 * and, whenever something else in the application needs a PDF on screen, shows that too,
 * without either place learning anything about the other.
 */
export default function PdfDocumentView({
  file,
  initialPage,
}: {
  file: FileInfo;
  initialPage?: number;
}) {
  const { t } = useTranslation();

  // The delivery address is renewed on a timer, well inside the life of the token it carries,
  // so that a reader who leaves the page open can still fetch. The viewer must not follow those
  // renewals: it fetched the whole file at the first one and holds it, so a new address would
  // only make it fetch the same bytes again and lose the reader's place. The first address per
  // file is therefore the one it is given, and a new file is a new document to open.
  const opened = useRef<{ id: string; url: string } | null>(null);
  if (opened.current?.id !== file.id) {
    opened.current = { id: file.id, url: file.contentUrl };
  }

  return (
    <Flex vertical gap={8} align="center" style={{ width: '100%', minWidth: 0 }}>
      <PdfView url={opened.current.url} initialPage={initialPage} />
      <Flex gap={12} align="center" wrap justify="center">
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t('documents.viewer.pdfSelectHint')}
        </Typography.Text>
        <DownloadDocument file={file} />
      </Flex>
    </Flex>
  );
}
