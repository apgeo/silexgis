// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { LeftOutlined, RightOutlined } from '@ant-design/icons';
import { Button, Empty, Flex, Image, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FileInfo } from '../../api/hooks.ts';
import { pageRenderUrl } from './derivativeUrl.ts';
import DownloadDocument from './DownloadDocument.tsx';

/**
 * A document read one page at a time, from pictures of its pages drawn on the server.
 *
 * Nothing is fetched here that the caller could not already have: a page picture shows what
 * the page shows and is handed out on the same terms a photo's rendering is, so reading a
 * document in the application never becomes a way to obtain the file itself.
 *
 * The pager appears only when the document says how many pages it has. Until its text has
 * been read the count is unknown, and a strip reading "1 of 1" over a two-hundred-page report
 * would be the interface stating a fact the document never gave it — so it says nothing, and
 * shows the first page, which is true.
 *
 * The controls are ordinary buttons rather than anything that has to be hovered or hovered
 * over to be found, because on a phone there is no hover and a control nobody can reveal is a
 * control nobody has. They sit under the page rather than over it for the same reason: a
 * thumb resting on a control that floats above what it is reading covers the reading.
 */
export default function PagedDocumentView({
  file,
  pagesUrl,
  pageCount,
  initialPage = 1,
}: {
  file: FileInfo;
  /**
   * Delivery URL of the file whose pages are drawn — this file when it paginates itself, and
   * the portable copy something made of it when it does not. The server names it; nothing
   * here works it out from a media type.
   */
  pagesUrl: string;
  pageCount: number | null;
  initialPage?: number;
}) {
  const { t } = useTranslation();
  const last = pageCount ?? 1;
  const [page, setPage] = useState(() => Math.min(Math.max(initialPage, 1), last));
  // A page that will not draw is not the same as a page still drawing, and neither is a blank
  // panel. What failed is remembered as the exact address that failed, not as a page number:
  // the delivery URL is renewed while a long read is going on, so a page that would not load
  // under a lapsed one must be tried again under the fresh one rather than written off. Moving
  // on from a genuinely damaged page still works, because that page's address is not this one.
  const [undrawable, setUndrawable] = useState<string | null>(null);

  // A search hit names the page it matched, and following a second hit in the same document
  // must move the reader rather than leave them on the page the first one opened.
  useEffect(() => {
    setPage(Math.min(Math.max(initialPage, 1), pageCount ?? Number.MAX_SAFE_INTEGER));
  }, [initialPage, pageCount]);

  const src = pageRenderUrl(pagesUrl, page, 1200);

  return (
    <Flex vertical gap={12} align="center" style={{ width: '100%', minWidth: 0 }}>
      {undrawable === src ? (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={
            <Flex vertical gap={8} align="center">
              <Typography.Text type="secondary">
                {t('documents.viewer.pageFailed')}
              </Typography.Text>
              <DownloadDocument file={file} />
            </Flex>
          }
        />
      ) : (
        <Image
          // The page is fetched afresh whenever the delivery URL is renewed, which is what
          // keeps a long read from ending on a page that will not load.
          src={src}
          preview={{ src: pageRenderUrl(pagesUrl, page, 2400) }}
          alt={t('documents.viewer.pageAlt', { number: page })}
          onError={() => setUndrawable(src)}
          // The picture is drawn wide enough to read on a desktop; on a narrow screen it
          // shrinks to the panel rather than pushing the page sideways under a thumb.
          style={{ maxWidth: '100%' }}
        />
      )}

      {pageCount !== null && pageCount > 1 && (
        <Flex gap={8} align="center" wrap justify="center">
          <Button
            icon={<LeftOutlined />}
            disabled={page <= 1}
            onClick={() => setPage((current) => Math.max(1, current - 1))}
            aria-label={t('documents.viewer.previousPage')}
          />
          <Typography.Text>
            {t('documents.viewer.pageOf', { number: page, count: pageCount })}
          </Typography.Text>
          <Button
            icon={<RightOutlined />}
            disabled={page >= pageCount}
            onClick={() => setPage((current) => Math.min(pageCount, current + 1))}
            aria-label={t('documents.viewer.nextPage')}
          />
        </Flex>
      )}
    </Flex>
  );
}
