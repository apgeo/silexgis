// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Result, Table, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import { ApiError } from '../../api/client.ts';
import { useCan, useCapabilities, useReviewQueue, type HeldBatch } from '../../api/hooks.ts';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import { SectionTags } from '../../components/review/ReviewParts.tsx';
import { sourceLabel } from '../../components/review/reviewModel.ts';

/** How long ago, in the reader's language, so the oldest waiting upload is the one that stands out. */
function age(from: string | null, locale: string): string {
  if (!from) {
    return '-';
  }
  const days = Math.floor((Date.now() - new Date(from).getTime()) / 86_400_000);
  if (days < 1) {
    return new Intl.RelativeTimeFormat(locale, { numeric: 'auto' }).format(0, 'day');
  }
  return new Intl.RelativeTimeFormat(locale, { numeric: 'auto' }).format(-days, 'day');
}

/** Uploads waiting for a decision, oldest first: who sent them, when, from where, and how much. */
export default function ReviewQueuePage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const mayRead = useCan('uploadReview', 'read');
  const rightsKnown = useCapabilities().data !== undefined;
  const queue = useReviewQueue(page, pageSize, mayRead);
  const locale = i18n.resolvedLanguage ?? 'en';

  if ((rightsKnown && !mayRead) || (queue.error instanceof ApiError && queue.error.status === 403)) {
    return <Result status="403" title={t('review.forbidden')} />;
  }

  return (
    <div style={{ padding: 24 }} data-testid="review-queue">
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        {t('review.queueTitle')}
      </Typography.Title>
      <Typography.Paragraph type="secondary">{t('review.queueExplain')}</Typography.Paragraph>
      <Typography.Paragraph type="warning" data-testid="review-whole-view">
        {t('review.wholeView')}
      </Typography.Paragraph>
      <Table<HeldBatch>
        size="small"
        rowKey="id"
        scroll={{ x: 'max-content' }}
        loading={queue.isFetching && !queue.data}
        dataSource={queue.data?.items}
        locale={{ emptyText: t('review.queueEmpty') }}
        onChange={(pagination) => {
          setPage(pagination.current ?? 1);
          setPageSize(pagination.pageSize ?? 25);
        }}
        pagination={{ current: queue.data?.page, pageSize: queue.data?.pageSize, total: queue.data?.totalItems }}
        onRow={(record) => ({
          onClick: () => navigate(`/review/batches/${record.id}`),
          style: { cursor: 'pointer' },
          'data-testid': 'review-queue-row',
        })}
        columns={[
          {
            title: t('review.uploader'),
            render: (_: unknown, b) => (
              <Link to={`/review/batches/${b.id}`} onClick={(e) => e.stopPropagation()}>
                {b.uploaderName ?? t('review.unknownUploader')}
              </Link>
            ),
          },
          {
            title: t('review.heldAt'),
            render: (_: unknown, b) => (
              <span title={b.heldAt ? new Date(b.heldAt).toLocaleString(locale) : undefined}>
                {b.heldAt ? new Date(b.heldAt).toLocaleString(locale) : '-'}
                <Typography.Text type="secondary"> ({age(b.heldAt, locale)})</Typography.Text>
              </span>
            ),
          },
          { title: t('review.source.label'), render: (_: unknown, b) => sourceLabel(t, b.source) },
          { title: t('review.contents'), render: (_: unknown, b) => <SectionTags sections={b.sections} /> },
          { title: t('review.waiting'), dataIndex: 'pendingCount', align: 'right' as const },
          {
            title: t('review.heldBytes'),
            align: 'right' as const,
            render: (_: unknown, b) => (b.heldBytes > 0 ? formatSize(b.heldBytes) : '-'),
          },
        ]}
      />
    </div>
  );
}
