// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EditOutlined, LockOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Flex,
  Popconfirm,
  Radio,
  Result,
  Space,
  Spin,
  Tabs,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  parseAccessActions,
  useCan,
  useDeleteEvent,
  useDeleteEventSeriesFollowing,
  useEffectiveAccess,
  useEvent,
  type EventKind,
} from '../../api/hooks.ts';
import HistoryPanel from '../../components/history/HistoryPanel.tsx';
import PermissionsModal from '../../components/permissions/PermissionsModal.tsx';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { formatTripDates } from '../../components/trips/tripDates.ts';
import EventFormModal from './EventFormModal.tsx';
import EventResponsesTab from './EventResponsesTab.tsx';
import EventStateControl from './EventStateControl.tsx';
import { eventRefusalKey } from './eventRefusals.ts';
import { eventKindTakesResponses } from './eventKinds.ts';

/**
 * One event: what it is, when it is, where it is in words, who it is for, and where it has got to.
 *
 * The dates and times are printed from their own parts. They are calendar days and wall-clock
 * times carrying no zone, so a 19:00 club meeting reads as 19:00 to every reader wherever they
 * are — the same reading a trip's entry and exit times already carry, and the reason nothing here
 * hands either to a date constructor.
 *
 * What the event is stays above the tab strip rather than becoming a tab of its own: it is what a
 * reader came for, and it is what everything below is about. Which tabs there are depends on the
 * kind — nobody comes to a deadline, so a deadline is not asked who is coming.
 */
export default function EventDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message, modal } = App.useApp();
  const [searchParams, setSearchParams] = useSearchParams();
  const { data: event, isPending, isError } = useEvent(id);
  const { data: effective } = useEffectiveAccess('event', id);
  const domainFallback = useCan('events', 'write');
  const held = effective ? parseAccessActions(effective.actions) : null;
  const remove = useDeleteEvent();
  const removeFollowing = useDeleteEventSeriesFollowing();
  const [editOpen, setEditOpen] = useState(false);
  const [permissionsOpen, setPermissionsOpen] = useState(false);

  // An event this reader may not open and one that does not exist answer identically, and the
  // page must not try to tell them apart. The read is not retried, so a refusal settles at once —
  // without this branch the page would hold a spinner that never resolves, which is what somebody
  // following a link to an event whose grant has since been withdrawn would be left looking at.
  if (isError) {
    return (
      <Result
        status="404"
        title={t('events.notFound')}
        subTitle={t('events.notFoundDetail')}
        extra={
          <Button type="primary" onClick={() => void navigate('/events')}>
            {t('common.back')}
          </Button>
        }
      />
    );
  }

  if (isPending || !event) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  const canEdit = held ? held.has('write') : domainFallback;
  const canDelete = held ? held.has('delete') : domainFallback;
  // Naming who may read an event is its own right, held by whoever made it and by anybody they
  // hand it to — not implied by being able to edit the text.
  const canManagePermissions = held ? held.has('managePermissions') : domainFallback;

  const deleteEvent = async () => {
    try {
      await remove.mutateAsync(event.id);
      message.success(t('common.deleted'));
      navigate('/events');
    } catch (error) {
      message.error(t(eventRefusalKey(error, 'common.deleteFailed')));
    }
  };

  const deleteFollowing = async () => {
    try {
      const result = await removeFollowing.mutateAsync(event.id);
      // Both halves are reported, and the kept half is the one that matters: somebody told only
      // how many went would believe the whole run is gone while the evenings that already happened
      // are still on the calendar, correctly.
      message.success(t('events.seriesDeleted', { deleted: result.deleted, kept: result.kept }));
      navigate('/events');
    } catch (error) {
      // Read from the code rather than shown as a general failure. This is the act where an
      // all-or-nothing refusal is likeliest — rights over a run are often held over part of it —
      // and "save failed" is both the wrong verb and no hint at all about what happened.
      message.error(t(eventRefusalKey(error, 'common.deleteFailed')));
    }
  };

  /**
   * Calling off an occurrence of a repeating event is a choice with two outcomes, so it is asked
   * in a dialog with something to choose in it rather than in the yes/no popover a single event
   * gets. The narrow answer is the default: calling off two years of evenings must be something
   * somebody picked, never something they got by confirming.
   */
  const confirmSeriesDelete = () => {
    let scope: 'occurrence' | 'following' = 'occurrence';
    modal.confirm({
      title: t('events.seriesDeleteTitle'),
      okText: t('events.delete'),
      okButtonProps: { danger: true, 'data-testid': 'event-delete-confirm' },
      cancelText: t('common.cancel'),
      content: (
        <Space orientation="vertical" size="middle" style={{ marginTop: 8 }}>
          <Radio.Group
            defaultValue={scope}
            onChange={(e) => {
              scope = e.target.value as 'occurrence' | 'following';
            }}
            data-testid="event-delete-scope"
          >
            <Space orientation="vertical">
              <Radio value="occurrence" data-testid="event-delete-scope-occurrence">
                {t('events.scopeOccurrence')}
              </Radio>
              <Radio value="following" data-testid="event-delete-scope-following">
                {t('events.scopeFollowing')}
              </Radio>
            </Space>
          </Radio.Group>
          <Typography.Text type="secondary">{t('events.seriesDeleteDetail')}</Typography.Text>
        </Space>
      ),
      onOk: () => (scope === 'following' ? deleteFollowing() : deleteEvent()),
    });
  };

  // Only the kinds people are asked about get a list of who is coming. The server holds the same
  // rule and is the one that enforces it, refusing the whole group for a kind that takes no
  // answers; this decides only what a reader is offered.
  const tabs = [
    ...(eventKindTakesResponses(event.kind)
      ? [
          {
            key: 'responses',
            label: t('events.tabResponses'),
            children: <EventResponsesTab event={event} canEdit={canEdit} />,
          },
        ]
      : []),
    {
      key: 'history',
      // Being asked, and answering, are facts about the event and surface on its own trail rather
      // than in a history of their own that nobody would think to open.
      label: t('events.tabHistory'),
      children: <HistoryPanel entityType="event" entityId={event.id} variant="bare" />,
    },
  ];
  const requested = searchParams.get('tab');
  const activeTab = tabs.some((tab) => tab.key === requested) ? requested! : tabs[0].key;

  const times = [event.startTime, event.endTime]
    .filter((value): value is string => !!value)
    .map((value) => value.slice(0, 5))
    .join(' – ');

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="start" wrap gap={12} style={{ marginBottom: 16 }}>
        <div>
          <Typography.Title level={3} style={{ margin: 0 }} data-testid="event-title">
            {event.title}
          </Typography.Title>
          <Flex gap={8} style={{ marginTop: 8 }}>
            <Tag>{t(`events.kindValues.${event.kind satisfies EventKind}`)}</Tag>
            <TripStateTag state={event.state} />
          </Flex>
        </div>
        <Flex gap={8} wrap>
          {canEdit && (
            <Button
              icon={<EditOutlined />}
              data-testid="event-edit"
              onClick={() => setEditOpen(true)}
            >
              {t('common.edit')}
            </Button>
          )}
          {/* Who may read this event, narrowed person by person. Its own right rather than a
              consequence of being able to edit the text: handing somebody the notice and handing
              them the reader list are two different decisions. An event contains nothing, so the
              dialog offers reach over this event alone. */}
          {canManagePermissions && (
            <Button
              icon={<LockOutlined />}
              data-testid="event-permissions"
              onClick={() => setPermissionsOpen(true)}
            >
              {t('permissions.button')}
            </Button>
          )}
          {/* An event standing on its own keeps the yes/no popover it has always had: there is
              nothing to choose, and a dialog to say so would be ceremony. One occurrence of a run
              has two outcomes, and a popover has no body to put the choice in. */}
          {canDelete &&
            (event.seriesId ? (
              <Button
                danger
                icon={<DeleteOutlined />}
                data-testid="event-delete"
                onClick={confirmSeriesDelete}
              >
                {t('events.delete')}
              </Button>
            ) : (
              <Popconfirm title={t('events.deleteConfirm')} onConfirm={() => void deleteEvent()}>
                <Button danger icon={<DeleteOutlined />} data-testid="event-delete">
                  {t('events.delete')}
                </Button>
              </Popconfirm>
            ))}
        </Flex>
      </Flex>

      {/* That this evening is one of a run, said once and in the words its author used. There is
          no series to open: the run is the other events carrying the same grouping key, each a
          whole event of its own. Nothing here is worked out from the sentence — it is shown
          exactly as written, and the days were settled when the run was created. */}
      {event.seriesId && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="event-series-banner"
          title={t('events.seriesBanner')}
          description={
            <Space orientation="vertical" size={4}>
              {event.seriesRule && (
                <span>{t('events.seriesRule', { rule: event.seriesRule })}</span>
              )}
              {/* The way to the rest of the run. The occurrences are ordinary events, so the run
                  is the event list narrowed to the grouping key — narrowed on the server and
                  through the same visibility walk as any other listing, so a reader is shown the
                  occurrences they may open and learns nothing about the ones they may not. */}
              <Typography.Link
                data-testid="event-series-occurrences"
                onClick={() => void navigate(`/events?seriesId=${event.seriesId}`)}
              >
                {t('events.seriesOccurrences')}
              </Typography.Link>
            </Space>
          }
        />
      )}

      <Card style={{ marginBottom: 16 }}>
        <Descriptions column={1} size="small">
          <Descriptions.Item label={t('events.dates')}>
            {formatTripDates(event.startDate, event.endDate, i18n.resolvedLanguage)}
          </Descriptions.Item>
          {times && (
            <Descriptions.Item label={t('events.time')} data-testid="event-times">
              {times}
            </Descriptions.Item>
          )}
          {event.place && (
            <Descriptions.Item label={t('events.place')}>{event.place}</Descriptions.Item>
          )}
          <Descriptions.Item label={t('features.visibility')}>
            <Tag>{t(`caves.visibilityValues.${event.visibility}`)}</Tag>
          </Descriptions.Item>
          {event.description && (
            <Descriptions.Item label={t('events.description')}>
              <Typography.Paragraph style={{ marginBottom: 0, whiteSpace: 'pre-wrap' }}>
                {event.description}
              </Typography.Paragraph>
            </Descriptions.Item>
          )}
        </Descriptions>
      </Card>

      <EventStateControl eventId={event.id} state={event.state} canEdit={canEdit} />

      <Tabs
        // The tab is in the address, so a section of an event is a place somebody can link to and
        // one that survives a reload. Switching replaces rather than pushes, matching the trip's
        // strip: the back button leaves the event instead of walking back through the tabs the
        // reader opened on the way. An address naming a tab this event has not got falls back to
        // its first one rather than leaving the strip with nothing under it.
        activeKey={activeTab}
        onChange={(key) =>
          setSearchParams(key === tabs[0].key ? {} : { tab: key }, { replace: true })
        }
        items={tabs}
      />

      <EventFormModal open={editOpen} event={event} onClose={() => setEditOpen(false)} />
      <PermissionsModal
        entityType="event"
        entityId={event.id}
        open={permissionsOpen}
        onClose={() => setPermissionsOpen(false)}
      />
    </div>
  );
}
