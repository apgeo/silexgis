// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState, type ReactNode } from 'react';
import { DeleteOutlined, UndoOutlined } from '@ant-design/icons';
import { Alert, App, Button, Collapse, Flex, Pagination, Popconfirm, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  TRACKING_REMOVED_PAGE_SIZE,
  useDestroyTrackingEvent,
  useRestoreTrackingEvent,
  useTripTrackingRemovedEvents,
  type TrackingEvent,
} from '../../api/hooks.ts';
import { trackingProblemMessage } from './trackingProblems.ts';

/**
 * The reports taken off a trip's log, under the log, with the two things that can be done to one:
 * put it back, or destroy it.
 *
 * <b>Closed by default, and absent while there is nothing in it.</b> A removed report is in no
 * fold — not on the party table, not on the survey, not in a replay or on a published page — so
 * this is the only place it can be seen at all, and it is somewhere a coordinator goes on purpose.
 * The heading carries the count so that the fact there *is* something to put back is on screen
 * without opening anything.
 *
 * <b>Mounted only for those who may write the log.</b> The server answers this list to nobody
 * else, and asking on behalf of a reader would file a refusal on every visit to the tab. Each
 * report's place arrives through the same per-row withholding as the log, and is drawn by the same
 * function the log draws its own with, handed in by the tab: a withheld place says it was withheld
 * here exactly as it does one table up.
 *
 * <b>Destroying is behind a confirmation of its own, and it is the only one on this surface that
 * says "cannot be undone".</b> Taking a report off the log used to say that and no longer does;
 * the sentence is kept for the one act it is true of.
 */
export default function TrackingRemovedReports({
  tripLogId,
  nameOf,
  placeOf,
  when,
  controlSize,
}: {
  tripLogId: string;
  nameOf: (caverId: string | null) => string;
  /** What one report says about a place, as the log itself draws it — a withholding included. */
  placeOf: (report: TrackingEvent) => ReactNode;
  when: (value: string | null) => string;
  /** How big everything pressed here is drawn: the tab's own size for the pointer in use. */
  controlSize: 'large' | 'small';
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const removed = useTripTrackingRemovedEvents(tripLogId, page);
  const restore = useRestoreTrackingEvent();
  const destroy = useDestroyTrackingEvent();

  const total = removed.data?.totalItems ?? 0;
  const lastPage = Math.max(1, Math.ceil(total / TRACKING_REMOVED_PAGE_SIZE));
  // The last report on a page put back or destroyed empties that page under whoever is on it; step
  // back to the last page that holds any. Only on an answer for the page actually asked for.
  if (removed.data !== undefined && !removed.isPlaceholderData && page > lastPage) {
    setPage(lastPage);
  }

  const onRestore = async (eventId: string) => {
    try {
      await restore.mutateAsync({ tripLogId, eventId });
      message.success(t('trips.tracking.eventRestored'));
    } catch (failure) {
      message.error(trackingProblemMessage(failure, t));
    }
  };

  const onDestroy = async (eventId: string) => {
    try {
      await destroy.mutateAsync({ tripLogId, eventId });
      message.success(t('trips.tracking.removed.destroyed'));
    } catch (failure) {
      message.error(trackingProblemMessage(failure, t));
    }
  };

  // Nothing removed and nothing wrong: no heading, no empty fold. A read that failed while there
  // is nothing held says nothing either — the log above has its own notice for a server that
  // cannot be reached, and a second one about a list most trips never have would only be noise.
  if (total === 0) {
    return null;
  }

  return (
    <Collapse
      size="small"
      style={{ marginTop: 12 }}
      data-testid="trip-tracking-removed"
      // Read again when it is opened: a second coordinator may have taken a report off, or put
      // one back, since this tab last asked.
      onChange={(open) => {
        if (open.length > 0) {
          void removed.refetch();
        }
      }}
      items={[
        {
          key: 'removed',
          label: (
            <span data-testid="trip-tracking-removed-count">
              {t('trips.tracking.removed.title', { count: total })}
            </span>
          ),
          children: (
            <>
              <Typography.Paragraph type="secondary">
                {t('trips.tracking.removed.help')}
              </Typography.Paragraph>
              {removed.error != null && (
                <Alert
                  type="error"
                  showIcon
                  title={t('trips.tracking.removed.unavailable')}
                  style={{ marginBottom: 8 }}
                  data-testid="trip-tracking-removed-unavailable"
                />
              )}
              <Flex vertical gap={12}>
                {(removed.data?.items ?? []).map(({ report, removedAt }) => (
                  <Flex
                    key={report.id}
                    vertical
                    gap={4}
                    data-testid={`trip-tracking-removed-${report.id}`}
                  >
                    <Flex gap={8} align="center" wrap>
                      <Typography.Text strong>{when(report.recordedAt)}</Typography.Text>
                      <span>{nameOf(report.caverId)}</span>
                      <Tag color={report.kind === 'caveNote' ? 'warning' : undefined}>
                        {t(`trips.tracking.kinds.${report.kind}`)}
                      </Tag>
                      <span data-testid={`trip-tracking-removed-place-${report.id}`}>
                        {placeOf(report)}
                      </span>
                    </Flex>
                    {report.note !== null && <div>{report.note}</div>}
                    <Flex gap={8} align="center" justify="space-between" wrap>
                      <Typography.Text type="secondary">
                        {t('trips.tracking.removed.removedAt', { when: when(removedAt) })}
                      </Typography.Text>
                      <Flex gap={8} wrap>
                        <Button
                          size={controlSize}
                          icon={<UndoOutlined />}
                          onClick={() => void onRestore(report.id)}
                          data-testid={`trip-tracking-removed-restore-${report.id}`}
                        >
                          {t('trips.tracking.removed.restore')}
                        </Button>
                        <Popconfirm
                          title={t('trips.tracking.removed.destroyConfirm')}
                          okText={t('trips.tracking.removed.destroy')}
                          okButtonProps={{ size: controlSize, danger: true }}
                          cancelButtonProps={{ size: controlSize }}
                          onConfirm={() => void onDestroy(report.id)}
                        >
                          <Button
                            danger
                            size={controlSize}
                            icon={<DeleteOutlined />}
                            data-testid={`trip-tracking-removed-destroy-${report.id}`}
                          >
                            {t('trips.tracking.removed.destroy')}
                          </Button>
                        </Popconfirm>
                      </Flex>
                    </Flex>
                  </Flex>
                ))}
              </Flex>
              <Pagination
                size="small"
                simple={controlSize === 'large'}
                style={{ marginTop: 12 }}
                current={page}
                pageSize={TRACKING_REMOVED_PAGE_SIZE}
                total={total}
                showSizeChanger={false}
                hideOnSinglePage
                onChange={setPage}
              />
            </>
          ),
        },
      ]}
    />
  );
}
