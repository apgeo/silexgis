// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, App, Checkbox, Empty, Flex, Progress, Skeleton, Space, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  useSetTripChecklistItem,
  useTripChecklist,
  type TripLogInfo,
} from '../../api/hooks.ts';
import List from '../../components/List.tsx';

/**
 * What this trip settles before it sets off, and how much of it is settled.
 *
 * The figure is advisory in the strongest sense and this surface says so in words. Nothing here
 * refuses anything for being unsettled: a trip with nothing ticked can be published, announced,
 * and read by exactly the people who could read it before. It is a reading for the party, not a
 * state the trip is in and not a second rule about who sees a plan.
 *
 * A trip whose purpose names no list and a trip naming one this reader may not open are one
 * answer here, because they are one answer on the server. Telling them apart would say a list
 * exists to somebody nobody meant to tell.
 */
export default function TripChecklistTab({
  trip,
  canEdit,
}: {
  trip: TripLogInfo;
  canEdit: boolean;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data, isLoading } = useTripChecklist(trip.id);
  const setItem = useSetTripChecklistItem();

  if (isLoading) {
    return <Skeleton active />;
  }

  if (!data?.checklistId) {
    return <Empty description={t('trips.checklistNone')} />;
  }

  const settled = data.total > 0 && data.ticked >= data.total;

  const toggle = (itemId: string, ticked: boolean) => {
    setItem.mutate(
      { tripLogId: trip.id, itemId, ticked },
      {
        onError: (error) => {
          void message.error(
            error instanceof ApiError ? error.detail : t('trips.checklistTickFailed'),
          );
        },
      },
    );
  };

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Typography.Title level={5} style={{ marginBottom: 0 }}>
        {data.title}
      </Typography.Title>
      {data.description ? (
        <Typography.Paragraph type="secondary">{data.description}</Typography.Paragraph>
      ) : null}

      <Flex align="center" gap="middle" wrap>
        <Progress
          percent={data.total > 0 ? Math.round((data.ticked / data.total) * 100) : 100}
          status={settled ? 'success' : 'normal'}
          style={{ maxWidth: 240, marginBottom: 0 }}
        />
        <Typography.Text strong>
          {t('trips.checklistReadiness', { ticked: data.ticked, total: data.total })}
        </Typography.Text>
      </Flex>

      {/* Said in words, because a bar that fills up reads like a gate whatever the code does. */}
      <Alert type="info" showIcon message={t('trips.checklistAdvisory')} />

      <List
        dataSource={data.items}
        renderItem={(item) => (
          <List.Item>
            <Flex vertical gap={2} style={{ width: '100%' }}>
              <Checkbox
                checked={item.ticked}
                disabled={!canEdit || setItem.isPending}
                onChange={(event) => toggle(item.id, event.target.checked)}
              >
                {item.text}
              </Checkbox>
              {item.ticked && item.tickedAt ? (
                <Tooltip title={new Date(item.tickedAt).toLocaleString()}>
                  <Typography.Text type="secondary" style={{ fontSize: 12, marginLeft: 24 }}>
                    {t('trips.checklistTickedAt', {
                      when: new Date(item.tickedAt).toLocaleDateString(),
                    })}
                  </Typography.Text>
                </Tooltip>
              ) : null}
            </Flex>
          </List.Item>
        )}
      />
    </Space>
  );
}
