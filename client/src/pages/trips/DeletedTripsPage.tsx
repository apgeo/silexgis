// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { ArrowLeftOutlined, UndoOutlined } from '@ant-design/icons';
import { App, Button, Empty, Flex, Popconfirm, Table, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import { ApiError } from '../../api/client.ts';
import {
  useDeletedTripLogs,
  useRestoreTripLog,
  useTripLogConfig,
  type DeletedTripLog,
} from '../../api/hooks.ts';
import { formatTripDates } from '../../components/trips/tripDates.ts';
import { daysLeft, formatDays } from '../../components/trips/tripRestoreWindow.ts';

/**
 * The deleted trips this reader may put back, most recently deleted first.
 *
 * Which trips those are is the server's decision, made row by row: the right that deletes a trip
 * is the right that restores it, asked of the trip as it stands. So nothing here filters, and an
 * account that may delete nothing sees an empty list rather than a refusal.
 *
 * The one act the page offers is restoring. A deleted trip answers as not found everywhere else,
 * so there is nothing to open and no row is a link; once a trip is back, the page opens it —
 * that is what somebody who restores a trip is about to do, and seeing it whole is the quickest
 * proof that it came back whole.
 */
export default function DeletedTripsPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const { data, isFetching, isError } = useDeletedTripLogs(page);
  const { data: config } = useTripLogConfig();
  const restore = useRestoreTripLog();
  // Read once: the days left are whole days, and a figure that moved under the reader while the
  // page sat open would be a figure about the render rather than about the trip.
  const [now] = useState(() => new Date());
  const language = i18n.resolvedLanguage;

  const onRestore = async (trip: DeletedTripLog) => {
    try {
      await restore.mutateAsync(trip.id);
      message.success(t('trips.deleted.restored'));
      navigate(`/trip-logs/${trip.id}`);
    } catch (e) {
      message.error(t(restoreProblemKey(e)));
    }
  };

  const removedText = (trip: DeletedTripLog): string => {
    const left = daysLeft(trip.restorableUntil, now);
    if (left === null) {
      return t('trips.deleted.kept');
    }
    return left === 0
      ? t('trips.deleted.removedToday')
      : t('trips.deleted.removedIn', { period: formatDays(left, language) });
  };

  // What the installation does with a deleted trip, said once above the table. Nothing is said
  // while the answer is still on its way: a sentence about a window nobody has read yet would be
  // a guess, and this is the page where the number matters.
  const windowText =
    config === undefined
      ? null
      : config.deletedRetentionDays === null
        ? t('trips.deleted.introKept')
        : t('trips.deleted.introWindow', {
            period: formatDays(config.deletedRetentionDays, language),
          });

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 8 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('trips.deleted.title')}
        </Typography.Title>
        <Link to="/trip-logs">
          <Button icon={<ArrowLeftOutlined />}>{t('trips.title')}</Button>
        </Link>
      </Flex>
      <Typography.Paragraph type="secondary" style={{ maxWidth: 760 }}>
        {t('trips.deleted.intro')}
        {windowText && <> {windowText}</>}
      </Typography.Paragraph>
      <Table<DeletedTripLog>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        data-testid="deleted-trips"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={(pagination: TablePaginationConfig) => setPage(pagination.current ?? 1)}
        locale={{
          emptyText: (
            <Empty
              data-testid="deleted-trips-empty"
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={isError ? t('trips.deleted.loadFailed') : t('trips.deleted.empty')}
            />
          ),
        }}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: false,
          hideOnSinglePage: true,
        }}
        columns={[
          { title: t('trips.deleted.columns.title'), dataIndex: 'title' },
          {
            title: t('trips.deleted.columns.dates'),
            key: 'dates',
            width: 200,
            render: (_, trip) => formatTripDates(trip.tripDate, trip.tripDateEnd, language),
          },
          {
            title: t('trips.deleted.columns.deletedAt'),
            dataIndex: 'deletedAt',
            width: 190,
            render: (value: string) => new Date(value).toLocaleString(language),
          },
          {
            title: t('trips.deleted.columns.deletedBy'),
            dataIndex: 'deletedByName',
            width: 180,
          },
          {
            title: t('trips.deleted.columns.removed'),
            key: 'removed',
            width: 170,
            render: (_, trip) => removedText(trip),
          },
          {
            title: '',
            key: 'actions',
            width: 130,
            render: (_, trip) => (
              <Popconfirm
                title={t('trips.deleted.restoreConfirm')}
                description={t('trips.deleted.restoreConfirmDetail')}
                okText={t('trips.deleted.restore')}
                onConfirm={() => void onRestore(trip)}
              >
                <Button
                  size="small"
                  icon={<UndoOutlined />}
                  loading={restore.isPending && restore.variables === trip.id}
                  data-testid="deleted-trip-restore"
                >
                  {t('trips.deleted.restore')}
                </Button>
              </Popconfirm>
            ),
          },
        ]}
      />
    </div>
  );
}

/**
 * The sentence for a restore that was refused. Each refusal the route names means something a
 * reader can act on — the window ran out, somebody else already restored it, it is not theirs —
 * and anything else is the ordinary failed save.
 */
function restoreProblemKey(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return 'common.saveFailed';
  }
  if (error.code === 'trip_log.restore_window_passed') {
    return 'trips.deleted.windowPassed';
  }
  if (error.code === 'trip_log.not_deleted' || error.status === 404) {
    return 'trips.deleted.gone';
  }
  return error.status === 403 ? 'trips.deleted.forbidden' : 'common.saveFailed';
}
