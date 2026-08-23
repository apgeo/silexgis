// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  Alert,
  App,
  Button,
  Descriptions,
  Flex,
  Select,
  Space,
  Statistic,
  Switch,
  Table,
  Tag,
  Typography,
} from 'antd';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import {
  hasAccessAction,
  useCapabilities,
  type NotificationDeliveryRow,
  type NotificationDeliveryStatus,
  type NotificationHealth,
  type NotificationRetryOutcome,
} from '../../api/hooks.ts';

const statusColor: Record<NotificationDeliveryStatus, string> = {
  pending: 'blue',
  deferred: 'gold',
  sent: 'green',
  dead: 'red',
};

/**
 * How long the oldest unsent message may have been waiting before the page raises it rather than
 * merely reporting it. A day, and deliberately the same window the list itself calls overdue: the
 * age is measured from when a message was queued, and two ordinary things hold one back for hours
 * without anything being wrong — the daily summary waits for its window, and a message due in the
 * recipient's night waits for morning. An alarm shorter than the longest healthy wait fires every
 * night, and an alarm that fires every night is one nobody reads.
 */
const STALLED_AFTER_SECONDS = 24 * 60 * 60;

/** Whole hours, then days, because "waiting 754321 seconds" is not a diagnosis. */
function ageLabel(seconds: number, t: TFunction): string {
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return t('notificationHealth.ageMinutes', { count: minutes });
  }
  const hours = Math.floor(minutes / 60);
  if (hours < 48) {
    return t('notificationHealth.ageHours', { count: hours });
  }
  return t('notificationHealth.ageDays', { count: Math.floor(hours / 24) });
}

/**
 * Whether messages are getting out of this installation, and what to do about the ones that are
 * not. Rides the settings domain rather than a domain of its own: the operator who configured the
 * mail server is the operator who wants to know whether mail is going out.
 */
export default function NotificationHealthPage() {
  const { t, i18n } = useTranslation();
  const { message, modal } = App.useApp();
  const queryClient = useQueryClient();
  const { data: capabilities } = useCapabilities();
  const canRetry = hasAccessAction(capabilities?.domains.settings, 'execute');

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [status, setStatus] = useState<NotificationDeliveryStatus | undefined>();
  const [attentionOnly, setAttentionOnly] = useState(true);
  const [retrying, setRetrying] = useState<number | null>(null);

  const health = useQuery({
    queryKey: ['notification-health'],
    queryFn: async () => {
      const { data, error, response } = await api.GET('/api/v1/admin/notifications/health');
      if (error !== undefined || data === undefined) {
        throw new Error(`API error ${response.status}`);
      }
      return data as NotificationHealth;
    },
  });

  const deliveries = useQuery({
    queryKey: ['notification-deliveries', page, pageSize, status, attentionOnly],
    queryFn: async () => {
      const { data, error, response } = await api.GET('/api/v1/admin/notifications/deliveries', {
        params: { query: { page, pageSize, status, attentionOnly } },
      });
      if (error !== undefined || data === undefined) {
        throw new Error(`API error ${response.status}`);
      }
      return data;
    },
    placeholderData: keepPreviousData,
  });

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['notification-health'] });
    await queryClient.invalidateQueries({ queryKey: ['notification-deliveries'] });
  };

  // A deliberate act, not a button that sits under the cursor on every row: this one sends
  // somebody else's message, and the confirmation is where the operator is told that the
  // recipient's own answer is read again first and may still be no.
  const retry = (row: NotificationDeliveryRow) => {
    modal.confirm({
      title: t('notificationHealth.retryTitle'),
      content: t('notificationHealth.retryConfirm', { template: row.templateKey }),
      okText: t('notificationHealth.retryOk'),
      cancelText: t('common.cancel'),
      onOk: async () => {
        setRetrying(row.id);
        try {
          const { data, error } = await api.POST(
            '/api/v1/admin/notifications/deliveries/{id}/retry',
            { params: { path: { id: row.id } } },
          );
          if (error !== undefined || !data) {
            message.error(t('notificationHealth.retryFailed'));
            return;
          }
          const outcome = data.outcome as NotificationRetryOutcome;
          const text = t(`notificationHealth.outcomes.${outcome}`);
          if (outcome === 'queued') {
            message.success(text);
          } else {
            message.info(text);
          }
          await refresh();
        } finally {
          setRetrying(null);
        }
      },
    });
  };

  const counts = health.data?.counts ?? [];
  const totalIn = (wanted: NotificationDeliveryStatus) =>
    counts.filter((c) => c.status === wanted).reduce((sum, c) => sum + c.count, 0);
  const oldestSeconds = health.data?.oldestPendingAgeSeconds ?? null;
  const stalled = oldestSeconds !== null && oldestSeconds > STALLED_AFTER_SECONDS;

  return (
    <div style={{ padding: 24 }}>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        {t('notificationHealth.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary">
        {t('notificationHealth.intro')}
      </Typography.Paragraph>

      {stalled && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('notificationHealth.stalled', {
            age: ageLabel(oldestSeconds, t),
          })}
          description={t('notificationHealth.stalledHint')}
        />
      )}

      <Flex gap={32} wrap style={{ marginBottom: 24 }}>
        <Statistic title={t('notificationHealth.statuses.pending')} value={totalIn('pending')} />
        <Statistic title={t('notificationHealth.statuses.deferred')} value={totalIn('deferred')} />
        <Statistic title={t('notificationHealth.statuses.dead')} value={totalIn('dead')} />
        <Statistic
          title={t('notificationHealth.oldestPending')}
          value={oldestSeconds === null ? t('notificationHealth.nothingWaiting') : ageLabel(oldestSeconds, t)}
        />
      </Flex>

      <Descriptions
        size="small"
        column={1}
        style={{ marginBottom: 24, maxWidth: 720 }}
        items={[
          {
            key: 'channels',
            label: t('notificationHealth.byChannel'),
            children:
              counts.length === 0 ? (
                <Typography.Text type="secondary">{t('notificationHealth.noDeliveries')}</Typography.Text>
              ) : (
                <Space wrap>
                  {counts.map((c) => (
                    <Tag key={`${c.channel}-${c.status}`} color={statusColor[c.status]}>
                      {t(`settings.notifications.channels.${c.channel}`)}
                      {' · '}
                      {t(`notificationHealth.statuses.${c.status}`)}
                      {`: ${c.count}`}
                    </Tag>
                  ))}
                </Space>
              ),
          },
        ]}
      />

      <Flex gap={8} align="center" wrap style={{ marginBottom: 12 }}>
        <Select<NotificationDeliveryStatus | undefined>
          allowClear
          style={{ minWidth: 200 }}
          placeholder={t('notificationHealth.anyStatus')}
          value={status}
          onChange={(value) => {
            setStatus(value);
            setPage(1);
          }}
          options={(['pending', 'deferred', 'sent', 'dead'] as const).map((value) => ({
            value,
            label: t(`notificationHealth.statuses.${value}`),
          }))}
        />
        <Space>
          <Switch
            checked={attentionOnly}
            onChange={(checked) => {
              setAttentionOnly(checked);
              setPage(1);
            }}
          />
          <span>{t('notificationHealth.attentionOnly')}</span>
        </Space>
        <Button onClick={refresh}>{t('notificationHealth.refresh')}</Button>
      </Flex>

      <Table<NotificationDeliveryRow>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="small"
        loading={deliveries.isFetching && !deliveries.data}
        dataSource={deliveries.data?.items}
        onChange={(pagination) => {
          setPage(pagination.current ?? 1);
          setPageSize(pagination.pageSize ?? 25);
        }}
        pagination={{
          current: deliveries.data?.page,
          pageSize: deliveries.data?.pageSize,
          total: deliveries.data?.totalItems,
          showSizeChanger: true,
        }}
        expandable={{
          rowExpandable: (record) => record.error != null,
          expandedRowRender: (record) => (
            <pre style={{ margin: 0, fontSize: 12, whiteSpace: 'pre-wrap' }}>{record.error}</pre>
          ),
        }}
        columns={[
          {
            title: t('notificationHealth.createdAt'),
            dataIndex: 'createdAt',
            width: 180,
            render: (value: string) => new Date(value).toLocaleString(i18n.resolvedLanguage),
          },
          {
            // The name a person may be shown under, never their address. An account with no
            // chosen name has no label at all rather than a fallback that would be one.
            title: t('notificationHealth.recipient'),
            dataIndex: 'recipientLabel',
            width: 180,
            render: (value: string | null) =>
              value ?? <Typography.Text type="secondary">{t('notificationHealth.unknownRecipient')}</Typography.Text>,
          },
          {
            title: t('notificationHealth.category'),
            dataIndex: 'category',
            width: 180,
            render: (value: string) => t(`settings.notifications.events.${value}`),
          },
          { title: t('notificationHealth.templateKey'), dataIndex: 'templateKey', width: 220 },
          {
            title: t('notificationHealth.channel'),
            dataIndex: 'channel',
            width: 100,
            render: (value: string) => t(`settings.notifications.channels.${value}`),
          },
          {
            title: t('notificationHealth.status'),
            dataIndex: 'status',
            width: 120,
            render: (value: NotificationDeliveryStatus) => (
              <Tag color={statusColor[value]}>{t(`notificationHealth.statuses.${value}`)}</Tag>
            ),
          },
          { title: t('notificationHealth.attempts'), dataIndex: 'attempts', width: 100 },
          {
            title: t('notificationHealth.error'),
            dataIndex: 'error',
            ellipsis: true,
          },
          {
            // No heading: the only thing in this column is a button that names itself.
            title: '',
            key: 'action',
            width: 120,
            render: (_: unknown, record) =>
              record.status === 'dead' && canRetry ? (
                <Button size="small" loading={retrying === record.id} onClick={() => retry(record)}>
                  {t('notificationHealth.retry')}
                </Button>
              ) : null,
          },
        ]}
      />
    </div>
  );
}
