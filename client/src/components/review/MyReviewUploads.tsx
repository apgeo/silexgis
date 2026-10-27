// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import { useMyReviewBatches, type HeldBatch } from '../../api/hooks.ts';
import { SectionTags } from './ReviewParts.tsx';
import { sourceLabel } from './reviewModel.ts';

/** The colour a state is told in: gold while it waits, green once written, red once turned down. */
function stateColor(state: HeldBatch['reviewState']): string | undefined {
  switch (state) {
    case 'pending':
      return 'gold';
    case 'accepted':
      return 'green';
    case 'partlyAccepted':
      return 'blue';
    case 'rejected':
      return 'red';
    default:
      return undefined;
  }
}

/**
 * The signed-in account's own held uploads, waiting and decided, with the reviewer's note.
 *
 * Shown above the confirmations on the imports tab and nowhere else: an account nobody holds
 * has no held uploads, and the panel then takes no room at all.
 */
export default function MyReviewUploads() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const mine = useMyReviewBatches(page, 10);
  const locale = i18n.resolvedLanguage ?? 'en';
  const when = (value: string | null) => (value ? new Date(value).toLocaleString(locale) : '-');

  if (!mine.data || mine.data.totalItems === 0) {
    return null;
  }

  return (
    <div style={{ marginBottom: 24 }} data-testid="review-mine">
      <Typography.Title level={4} style={{ marginTop: 0 }}>
        {t('review.mine.title')}
      </Typography.Title>
      <Alert type="info" showIcon style={{ marginBottom: 12 }} title={t('review.mine.explain')} />
      <Table<HeldBatch>
        size="small"
        rowKey="id"
        scroll={{ x: 'max-content' }}
        dataSource={mine.data.items}
        pagination={{
          current: mine.data.page,
          pageSize: mine.data.pageSize,
          total: mine.data.totalItems,
          hideOnSinglePage: true,
          onChange: setPage,
        }}
        onRow={(record) => ({
          onClick: () => navigate(`/review/batches/${record.id}`),
          style: { cursor: 'pointer' },
          'data-testid': 'review-mine-row',
        })}
        columns={[
          { title: t('review.mine.sentAt'), render: (_: unknown, b) => when(b.heldAt) },
          { title: t('review.source.label'), render: (_: unknown, b) => sourceLabel(t, b.source) },
          {
            title: t('review.state.label'),
            render: (_: unknown, b) => (
              <Tag color={stateColor(b.reviewState)} data-testid="review-mine-state">
                {t(`review.state.${b.reviewState}`)}
              </Tag>
            ),
          },
          {
            title: t('review.contents'),
            render: (_: unknown, b) => (
              <div>
                <SectionTags sections={b.sections} />
                {b.pendingCount > 0 && (
                  <Typography.Text type="secondary">
                    {t('review.mine.outcome', { waiting: b.pendingCount, total: b.lineCount })}
                  </Typography.Text>
                )}
              </div>
            ),
          },
          {
            title: t('review.mine.decidedAt'),
            render: (_: unknown, b) =>
              b.decidedAt ? (
                <span>
                  {when(b.decidedAt)}
                  {b.decidedByName && (
                    <Typography.Text type="secondary"> {t('review.mine.decidedBy', { name: b.decidedByName })}</Typography.Text>
                  )}
                </span>
              ) : (
                '-'
              ),
          },
          {
            title: t('review.mine.note'),
            render: (_: unknown, b) => (b.reviewNote ? <span data-testid="review-mine-note">{b.reviewNote}</span> : '-'),
          },
          {
            render: (_: unknown, b) => (
              <Link to={`/review/batches/${b.id}`} onClick={(e) => e.stopPropagation()}>
                {t('review.mine.open')}
              </Link>
            ),
          },
        ]}
      />
    </div>
  );
}
