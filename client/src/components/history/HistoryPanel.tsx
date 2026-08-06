// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { LockOutlined, UndoOutlined } from '@ant-design/icons';
import { App, Button, Card, Empty, Spin, Table, Tag, Timeline, Tooltip, Typography } from 'antd';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useHistory, type HistoryEvent } from '../../api/hooks.ts';
import { changeRows, formatValue, restorableProps, toWriteField, type HistoryFieldRow } from './historyModel.ts';

/** Enables per-field restore for updated rows of the given CLR entity type (e.g. "Cave"). */
export interface HistoryRestore {
  entityType: string;
  onRestore: (event: HistoryEvent, props: string[]) => Promise<void> | void;
}

/**
 * A timeline mixes a root entity's events with its children's (entrances, taggings…),
 * so restore is keyed by CLR entity type: pass one handler, or several to make more than
 * one row type restorable (e.g. Cave + CaveEntrance on the cave page).
 */
export type HistoryRestoreProp = HistoryRestore | HistoryRestore[];

const ACTION_COLOR: Record<string, string> = { created: 'green', updated: 'blue', deleted: 'red' };

export default function HistoryPanel({
  entityType,
  entityId,
  restore,
}: {
  entityType: string;
  entityId: string | undefined;
  restore?: HistoryRestoreProp;
}) {
  const { t } = useTranslation();
  const { data, isLoading } = useHistory(entityType, entityId);
  const events = data?.items ?? [];

  return (
    <Card size="small" title={t('history.title')} style={{ marginTop: 12 }}>
      {isLoading ? (
        <Spin />
      ) : events.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('history.empty')} />
      ) : (
        <Timeline
          items={events.map((event) => ({
            key: event.id,
            color: ACTION_COLOR[event.action] ?? 'gray',
            content: <HistoryEventItem event={event} restore={restore} />,
          }))}
        />
      )}
    </Card>
  );
}

function HistoryEventItem({ event, restore }: { event: HistoryEvent; restore?: HistoryRestoreProp }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [restoring, setRestoring] = useState<string | null>(null);

  const rows = changeRows(event.changes, event.redactedProperties);
  const handlers = restore === undefined ? [] : Array.isArray(restore) ? restore : [restore];
  const handler = event.action === 'updated'
    ? handlers.find((h) => h.entityType === event.entityType)
    : undefined;
  const restorable = handler ? restorableProps(rows) : [];

  const doRestore = async (props: string[]) => {
    if (!handler) {
      return;
    }
    setRestoring(props.join(',') || '*');
    try {
      await handler.onRestore(event, props);
      message.success(t('history.restored'));
    } catch {
      message.error(t('common.saveFailed'));
    } finally {
      setRestoring(null);
    }
  };

  // Audit property names are PascalCase; the i18n field keys are camelCase (like the write
  // DTOs), so map before lookup, falling back to a humanized label.
  const label = (prop: string) => t(`history.field.${toWriteField(prop)}`, { defaultValue: humanize(prop) });

  return (
    <div>
      <Typography.Text>
        <Tag color={ACTION_COLOR[event.action] ?? 'default'}>{t(`history.action.${event.action}`, { defaultValue: event.action })}</Tag>
        <Typography.Text strong>{t(`history.entity.${event.entityType}`, { defaultValue: event.entityType })}</Typography.Text>
        {' · '}
        {event.userName ?? t('history.systemUser')}
        {' · '}
        <Typography.Text type="secondary">{dayjs(event.at).format('YYYY-MM-DD HH:mm')}</Typography.Text>
      </Typography.Text>

      {rows.length > 0 && (
        <Table<HistoryFieldRow>
          scroll={{ x: 'max-content' }}
          className="history-fields"
          size="small"
          pagination={false}
          showHeader={false}
          rowKey="prop"
          style={{ marginTop: 6 }}
          dataSource={rows}
          columns={[
            { dataIndex: 'prop', width: '30%', render: (_, r) => <Typography.Text type="secondary">{label(r.prop)}</Typography.Text> },
            {
              render: (_, r) =>
                r.redacted ? (
                  <Typography.Text type="secondary">
                    <LockOutlined /> {t('history.hidden')}
                  </Typography.Text>
                ) : (
                  <span>
                    <Typography.Text delete type="secondary">{formatValue(r.old)}</Typography.Text>
                    {' → '}
                    <Typography.Text>{formatValue(r.new)}</Typography.Text>
                  </span>
                ),
            },
            {
              width: 40,
              render: (_, r) =>
                restorable.includes(r.prop) ? (
                  <Tooltip title={t('history.restoreField')}>
                    <Button
                      size="small"
                      type="text"
                      icon={<UndoOutlined />}
                      loading={restoring === r.prop}
                      aria-label={t('history.restoreField')}
                      onClick={() => void doRestore([r.prop])}
                    />
                  </Tooltip>
                ) : null,
            },
          ]}
        />
      )}

      {restorable.length > 1 && (
        <Button
          size="small"
          type="link"
          icon={<UndoOutlined />}
          loading={restoring === restorable.join(',')}
          onClick={() => void doRestore(restorable)}
        >
          {t('history.restoreAll')}
        </Button>
      )}
    </div>
  );
}

/** "ClosestAddress" → "Closest address" — a readable fallback when no i18n key exists. */
function humanize(prop: string): string {
  const spaced = prop.replace(/([a-z0-9])([A-Z])/g, '$1 $2');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}
