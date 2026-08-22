// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Badge, Button, Flex, Segmented, Select, Table, Tag, Typography } from 'antd';
import { CheckOutlined } from '@ant-design/icons';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import {
  useMarkAllNotificationsRead,
  useMarkNotificationRead,
  useNotifications,
  type NotificationCategoryName,
  type NotificationItem,
} from '../../api/hooks.ts';

/**
 * Every category the server can send, written out so the filter cannot silently miss one: the
 * record is closed in both directions, so a category added on the server fails to compile here
 * until it is named, and a name that is no longer one fails too.
 */
const everyCategory: Record<NotificationCategoryName, true> = {
  cavingGroupMembership: true,
  permissionGranted: true,
  tripParticipation: true,
  tripPlanning: true,
  jobCompleted: true,
  securityAlerts: true,
};
const filterableCategories = Object.keys(everyCategory) as NotificationCategoryName[];

type Scope = 'all' | 'unread';

/**
 * What happened, newest first.
 *
 * The wording of a line is rendered on the server, in the language the request was made in, so
 * that an operator who rewrites a template sees the new wording here without a client release.
 * This page decides only how a line is shown — and in particular how to show one whose subject the
 * reader may no longer see.
 */
export default function NotificationsPage() {
  const { t, i18n } = useTranslation();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [category, setCategory] = useState<NotificationCategoryName | undefined>(undefined);
  const [scope, setScope] = useState<Scope>('all');

  const { data, isFetching } = useNotifications({
    page,
    pageSize,
    category,
    unreadOnly: scope === 'unread' ? true : undefined,
  });
  const markRead = useMarkNotificationRead();
  const markAllRead = useMarkAllNotificationsRead();

  /**
   * Reading a line is opening it, whether or not there is anywhere to go: a line whose subject
   * has been withheld has no link, and would otherwise stay unread for ever.
   */
  const open = (record: NotificationItem) => {
    if (record.readAt === null) {
      markRead.mutate(record.id);
    }
  };

  function what(record: NotificationItem) {
    if (record.targetWithheld) {
      // Deliberate, not broken: that this happened is not the secret, so the line stays — but it
      // carries neither the name of the thing nor a link to it, and says so in as many words.
      return (
        <Typography.Text type="secondary" italic data-testid="notification-withheld">
          {t('notifications.withheld')}
        </Typography.Text>
      );
    }
    if (record.title === null || record.title === '') {
      // A different failure with the same shape: wording this installation no longer has, which
      // the server records as a dead delivery. Named separately because the reason differs.
      return (
        <Typography.Text type="secondary" italic data-testid="notification-unrenderable">
          {t('notifications.unrenderable')}
        </Typography.Text>
      );
    }
    const text = record.readAt === null ? <strong>{record.title}</strong> : record.title;
    return record.url === null ? <span>{text}</span> : <Link to={record.url}>{text}</Link>;
  }

  return (
    <div style={{ padding: 24, overflow: 'auto', height: '100%' }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
        <Typography.Title level={3} style={{ marginTop: 0, marginBottom: 0 }}>
          {t('notifications.title')}
        </Typography.Title>
        <Button
          icon={<CheckOutlined />}
          onClick={() => markAllRead.mutate()}
          loading={markAllRead.isPending}
        >
          {t('notifications.markAllRead')}
        </Button>
      </Flex>
      <Flex gap={8} align="center" style={{ marginBottom: 12 }} wrap>
        <Segmented<Scope>
          value={scope}
          onChange={(value) => {
            setScope(value);
            setPage(1);
          }}
          options={[
            { value: 'all', label: t('notifications.scopeAll') },
            { value: 'unread', label: t('notifications.scopeUnread') },
          ]}
        />
        <Select<NotificationCategoryName | undefined>
          allowClear
          value={category}
          onChange={(value) => {
            setCategory(value);
            setPage(1);
          }}
          placeholder={t('notifications.allCategories')}
          aria-label={t('notifications.about')}
          style={{ minWidth: 280 }}
          options={filterableCategories.map((name) => ({
            value: name,
            label: t(`settings.notifications.events.${name}`),
          }))}
        />
      </Flex>
      <Table<NotificationItem>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="small"
        loading={isFetching && !data}
        dataSource={data?.items}
        locale={{ emptyText: t('notifications.empty') }}
        onRow={(record) => ({ onClick: () => open(record) })}
        onChange={(pagination) => {
          setPage(pagination.current ?? 1);
          setPageSize(pagination.pageSize ?? 20);
        }}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: true,
        }}
        columns={[
          {
            title: '',
            dataIndex: 'readAt',
            width: 32,
            render: (readAt: NotificationItem['readAt']) =>
              readAt === null ? (
                <Badge status="processing" data-testid="notification-unread" />
              ) : null,
          },
          {
            title: t('notifications.what'),
            dataIndex: 'title',
            render: (_value: unknown, record: NotificationItem) => what(record),
          },
          {
            title: t('notifications.about'),
            dataIndex: 'category',
            width: 280,
            render: (value: NotificationItem['category']) =>
              value === null ? null : <Tag>{t(`settings.notifications.events.${value}`)}</Tag>,
          },
          {
            title: t('notifications.when'),
            dataIndex: 'createdAt',
            width: 180,
            render: (value: string) => new Date(value).toLocaleString(i18n.resolvedLanguage),
          },
        ]}
      />
    </div>
  );
}
