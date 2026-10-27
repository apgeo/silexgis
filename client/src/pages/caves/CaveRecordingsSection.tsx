// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Card, Table, Tag, Tooltip, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  useCaveRecordings,
  type TripRecordingListItem,
  type TripRecordingStanding,
} from '../../api/hooks.ts';
import { RowLink } from '../../components/RowOpeners.tsx';

/** A cave page shows a page of recordings at a time. */
const PAGE_SIZE = 10;

const STANDING_COLOUR: Record<TripRecordingStanding, string> = {
  waiting: 'default',
  converted: 'green',
  needsAttention: 'orange',
};

/**
 * The trips phones recorded in this cave, with where each stands and the way to make a waiting one
 * into a trip report by hand.
 *
 * What the server answers is already what this reader may be told: a cave they may not be told
 * about or may not place lists nothing, for the reason the cave's trips list nothing, and a
 * converted recording names its trip only to somebody who may read the trip. So an empty answer
 * draws nothing at all — not an empty table that would announce there is a list to be denied.
 */
export default function CaveRecordingsSection({ caveId }: { caveId: string }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const { data, isFetching } = useCaveRecordings(caveId, page, PAGE_SIZE);

  const onTableChange = (pagination: TablePaginationConfig) => setPage(pagination.current ?? 1);

  if (!data || data.totalItems === 0) {
    return null;
  }

  const when = (value: string) => new Date(value).toLocaleString(i18n.resolvedLanguage);

  return (
    <Card
      title={t('recordings.title')}
      style={{ marginTop: 16 }}
      data-testid="cave-recordings"
    >
      <Typography.Paragraph type="secondary">{t('recordings.intro')}</Typography.Paragraph>
      <Table<TripRecordingListItem>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="small"
        loading={isFetching && !data}
        dataSource={data.items}
        onChange={onTableChange}
        pagination={{
          current: data.page,
          pageSize: data.pageSize,
          total: data.totalItems,
          hideOnSinglePage: true,
        }}
        columns={[
          {
            title: t('recordings.columns.recording'),
            dataIndex: 'title',
            render: (_, recording) => (
              <>
                {recording.title}
                {recording.endedAt === null && (
                  <Tag style={{ marginInlineStart: 8 }}>{t('recordings.stillOpen')}</Tag>
                )}
              </>
            ),
          },
          {
            title: t('recordings.columns.started'),
            dataIndex: 'startedAt',
            width: 190,
            render: (value: string) => when(value),
          },
          {
            title: t('recordings.columns.scans'),
            dataIndex: 'pointCount',
            width: 90,
            align: 'right',
          },
          {
            title: t('recordings.columns.standing'),
            dataIndex: 'standing',
            width: 190,
            render: (standing: TripRecordingStanding, recording) => (
              <Tooltip title={t(`recordings.standingHint.${standing}`)}>
                <Tag
                  color={STANDING_COLOUR[standing]}
                  data-testid={`recording-standing-${standing}`}
                >
                  {t(`recordings.standing.${standing}`)}
                </Tag>
                {recording.tripLogId && (
                  <RowLink to={`/trip-logs/${recording.tripLogId}`}>
                    {t('recordings.openTrip')}
                  </RowLink>
                )}
              </Tooltip>
            ),
          },
          {
            key: 'action',
            width: 190,
            render: (_, recording) =>
              recording.canConvert ? (
                <Tooltip title={t('recordings.convertHint')}>
                  <Button
                    size="small"
                    data-testid={`recording-convert-${recording.id}`}
                    onClick={() =>
                      navigate(`/trip-logs/speleoloc-import/recording/${recording.id}`)
                    }
                  >
                    {t('recordings.convert')}
                  </Button>
                </Tooltip>
              ) : null,
          },
        ]}
      />
    </Card>
  );
}
