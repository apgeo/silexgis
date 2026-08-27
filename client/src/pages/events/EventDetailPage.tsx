// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EditOutlined, LockOutlined } from '@ant-design/icons';
import {
  App,
  Button,
  Card,
  Descriptions,
  Flex,
  Popconfirm,
  Result,
  Spin,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import {
  parseAccessActions,
  useCan,
  useDeleteEvent,
  useEffectiveAccess,
  useEvent,
  type EventKind,
} from '../../api/hooks.ts';
import PermissionsModal from '../../components/permissions/PermissionsModal.tsx';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import { formatTripDates } from '../../components/trips/tripDates.ts';
import EventFormModal from './EventFormModal.tsx';
import EventStateControl from './EventStateControl.tsx';

/**
 * One event: what it is, when it is, where it is in words, who it is for, and where it has got to.
 *
 * The dates and times are printed from their own parts. They are calendar days and wall-clock
 * times carrying no zone, so a 19:00 club meeting reads as 19:00 to every reader wherever they
 * are — the same reading a trip's entry and exit times already carry, and the reason nothing here
 * hands either to a date constructor.
 */
export default function EventDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { data: event, isPending, isError } = useEvent(id);
  const { data: effective } = useEffectiveAccess('event', id);
  const domainFallback = useCan('events', 'write');
  const held = effective ? parseAccessActions(effective.actions) : null;
  const remove = useDeleteEvent();
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
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

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
          {canDelete && (
            <Popconfirm title={t('events.deleteConfirm')} onConfirm={() => void deleteEvent()}>
              <Button danger icon={<DeleteOutlined />} data-testid="event-delete">
                {t('events.delete')}
              </Button>
            </Popconfirm>
          )}
        </Flex>
      </Flex>

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
