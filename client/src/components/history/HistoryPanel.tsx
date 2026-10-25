// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { LockOutlined, UndoOutlined } from '@ant-design/icons';
import { Link } from 'react-router-dom';
import { Alert, App, Card, Empty, Spin, Table, Tag, Timeline, Tooltip, Typography } from 'antd';
import Button from '../Button.tsx';
import dayjs from 'dayjs';
import './HistoryPanel.css';
import { useTranslation } from 'react-i18next';
import {
  useHistory,
  useRestoreFromHistory,
  type HistoryEvent,
  type HistoryRestoreResult,
} from '../../api/hooks.ts';
import {
  changeRows,
  entityLabelKey,
  formatValue,
  groupByChangeSet,
  humanize,
  isGeometrySummary,
  restorableProps,
  restoreRequest,
  rosterTimesAct,
  serverRestorableProps,
  toWriteField,
  type HistoryFieldRow,
  type HistoryGroup,
} from './historyModel.ts';

/**
 * Enables per-field restore for updated rows of the given audit entity type (e.g. "Feature:Cave").
 * The panel puts the fields back through the server unless the host composes the write itself.
 */
export type HistoryRestore = ServerHistoryRestore | ClientHistoryRestore;

/**
 * A feature's row: the server puts the fields back as an ordinary edit and says which it wrote
 * and which it left, so the screen holds no copy of the rule that decides.
 */
export interface ServerHistoryRestore {
  entityType: string;
  /** Called once the server has written something — for a host that keeps its own picture of the feature. */
  onRestored?: (result: HistoryRestoreResult) => void;
}

/** A row the server does not restore (a trip report): the host composes the write from the old values. */
export interface ClientHistoryRestore {
  entityType: string;
  onRestore: (event: HistoryEvent, props: string[]) => Promise<void> | void;
}

function composesItself(handler: HistoryRestore): handler is ClientHistoryRestore {
  return 'onRestore' in handler;
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
  enabled = true,
  variant = 'card',
}: {
  entityType: string;
  entityId: string | undefined;
  restore?: HistoryRestoreProp;
  /** False asks for nothing at all — what a collapsed panel section passes. */
  enabled?: boolean;
  /**
   * 'bare' drops the card chrome for a host that draws its own header. A timeline inside a card
   * inside a section header is two borders around one list.
   */
  variant?: 'card' | 'bare';
}) {
  const { t } = useTranslation();
  const { data, isLoading } = useHistory(entityType, entityId, enabled);
  const events = data?.items ?? [];

  const body = (
    <>
      {isLoading ? (
        <Spin />
      ) : events.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('history.empty')} />
      ) : (
        <Timeline
          items={groupByChangeSet(events).map((group) => ({
            key: group.key,
            color: ACTION_COLOR[group.events[0].action] ?? 'gray',
            content:
              group.events.length > 1 ? (
                <HistoryGroupItem group={group} restore={restore} />
              ) : (
                <HistoryEventItem event={group.events[0]} restore={restore} />
              ),
          }))}
        />
      )}
    </>
  );

  if (variant === 'bare') {
    return body;
  }

  return (
    <Card size="small" title={t('history.title')} style={{ marginTop: 12 }}>
      {body}
    </Card>
  );
}

const SOURCE_COLOR: Record<string, string> = {
  deviceSync: 'purple',
  fileImport: 'geekblue',
  speleolocArchive: 'cyan',
  restore: 'orange',
  undo: 'orange',
  reviewDecision: 'gold',
};

/** Where a change came from, and — when it belongs to an import — a way to the import it is part of. */
function SourceBadge({ event }: { event: HistoryEvent }) {
  const { t } = useTranslation();
  if (!event.source) {
    // History from before the source was recorded: say nothing rather than guess.
    return null;
  }
  return (
    <>
      <Tag color={SOURCE_COLOR[event.source]} data-testid="history-source">
        {t(`history.source.${event.source}`, { defaultValue: event.source })}
      </Tag>
      {event.importBatchId && (
        <Link
          to={`/geodata?tab=batches&batch=${event.importBatchId}`}
          data-testid="history-batch-link"
          style={{ marginInlineEnd: 8 }}
        >
          {t('history.openBatch')}
        </Link>
      )}
    </>
  );
}

const timeOf = (event: HistoryEvent) => dayjs(event.at).format('YYYY-MM-DD HH:mm');

/** Several events one action wrote: who and when once, with each event's own changes under it. */
function HistoryGroupItem({ group, restore }: { group: HistoryGroup<HistoryEvent>; restore?: HistoryRestoreProp }) {
  const { t } = useTranslation();
  const first = group.events[0];
  return (
    <div data-testid="history-change-set">
      <Typography.Text>
        <SourceBadge event={first} />
        {first.userName ?? t('history.systemUser')}
        {' · '}
        <Typography.Text type="secondary">{timeOf(first)}</Typography.Text>
        {' · '}
        <Typography.Text type="secondary">{t('history.changesInSet', { count: group.events.length })}</Typography.Text>
      </Typography.Text>
      <div style={{ marginInlineStart: 8, paddingInlineStart: 12, borderInlineStart: '2px solid rgba(128,128,128,0.3)' }}>
        {group.events.map((event) => (
          <div key={event.id} style={{ marginTop: 8 }}>
            <HistoryEventItem event={event} restore={restore} grouped />
          </div>
        ))}
      </div>
    </div>
  );
}

function HistoryEventItem({
  event,
  restore,
  grouped = false,
}: {
  event: HistoryEvent;
  restore?: HistoryRestoreProp;
  /** Inside a change set, whose header already says who, when and from where. */
  grouped?: boolean;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const restoreOnServer = useRestoreFromHistory();
  const [restoring, setRestoring] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<HistoryRestoreResult | null>(null);

  // An entry that records an act is worded as one; only a real set of changed fields is
  // drawn as a table of before and after.
  const act = rosterTimesAct(event.changes);
  const rows = act === null ? changeRows(event.changes, event.redactedProperties) : [];
  const handlers = restore === undefined ? [] : Array.isArray(restore) ? restore : [restore];
  const handler = event.action === 'updated'
    ? handlers.find((h) => h.entityType === event.entityType)
    : undefined;
  const restorable = !handler ? [] : composesItself(handler) ? restorableProps(rows) : serverRestorableProps(rows);

  const doRestore = async (props: string[]) => {
    if (!handler) {
      return;
    }
    setRestoring(props.join(',') || '*');
    setOutcome(null);
    try {
      if (composesItself(handler)) {
        await handler.onRestore(event, props);
        message.success(t('history.restored'));
      } else {
        const result = await restoreOnServer.mutateAsync(restoreRequest(event, props));
        if (result.applied.length > 0) {
          handler.onRestored?.(result);
        }
        if (result.skipped.length === 0) {
          message.success(t('history.restored'));
        } else {
          // A field left as it is is said where the person is looking, with why — a toast
          // would be gone before the reason could be read.
          setOutcome(result);
        }
      }
    } catch {
      message.error(t('common.saveFailed'));
    } finally {
      setRestoring(null);
    }
  };

  // Audit property names are PascalCase; the i18n field keys are camelCase (like the write
  // DTOs), so map before lookup, falling back to a humanized label.
  const shown = (value: unknown) =>
    isGeometrySummary(value) ? t('history.geometrySummary', { points: value.points }) : formatValue(value);
  const label = (prop: string) => t(`history.field.${toWriteField(prop)}`, { defaultValue: humanize(prop) });

  return (
    <div>
      <Typography.Text>
        <Tag color={ACTION_COLOR[event.action] ?? 'default'}>{t(`history.action.${event.action}`, { defaultValue: event.action })}</Tag>
        <Typography.Text strong>
          {t(`history.entity.${entityLabelKey(event.entityType)}`, { defaultValue: event.entityType })}
        </Typography.Text>
        {!grouped && (
          <>
            {' · '}
            <SourceBadge event={event} />
            {event.userName ?? t('history.systemUser')}
            {' · '}
            <Typography.Text type="secondary">{timeOf(event)}</Typography.Text>
          </>
        )}
      </Typography.Text>

      {act !== null && (
        <Typography.Paragraph
          type="secondary"
          style={{ marginTop: 6, marginBottom: 0 }}
          data-testid="history-roster-times"
        >
          {t('history.rosterTimes.written')}
          {act.people !== null && <> {t('history.rosterTimes.people', { n: act.people })}.</>}
          {act.rows !== null && <> {t('history.rosterTimes.rows', { n: act.rows })}.</>}
          {act.timeZone !== null && <> {t('history.rosterTimes.zone', { zone: act.timeZone })}</>}
        </Typography.Paragraph>
      )}

      {rows.length > 0 && (
        // The changes scroll sideways when a value is long (a geometry's text, a description), and
        // a region that scrolls has to be reachable from the keyboard to be read: the box that
        // scrolls is this one, so it is the one that takes focus — the table inside is as wide as
        // its widest row.
        <div
          className="history-fields-scroll"
          tabIndex={0}
          role="group"
          aria-label={t('history.changesOf', { when: dayjs(event.at).format('YYYY-MM-DD HH:mm') })}
        >
        <Table<HistoryFieldRow>
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
                    <Typography.Text delete type="secondary">{shown(r.old)}</Typography.Text>
                    {' → '}
                    <Typography.Text>{shown(r.new)}</Typography.Text>
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
        </div>
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

      {outcome && (
        <Alert
          type="warning"
          showIcon
          closable
          onClose={() => setOutcome(null)}
          style={{ marginTop: 6 }}
          data-testid="history-restore-outcome"
          title={
            outcome.applied.length > 0
              ? t('history.restoredPartly', { count: outcome.applied.length })
              : t('history.restoredNothing')
          }
          description={
            <ul style={{ margin: 0, paddingInlineStart: 18 }}>
              {outcome.skipped.map((skip) => (
                <li key={skip.field}>
                  <Typography.Text strong>{label(skip.field)}</Typography.Text>
                  {' — '}
                  {t(`history.skipReason.${skip.reason}`, { defaultValue: skip.reason })}
                </li>
              ))}
            </ul>
          }
        />
      )}
    </div>
  );
}
