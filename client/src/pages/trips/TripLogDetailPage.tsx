// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EditOutlined } from '@ant-design/icons';
import { App, Button, Card, Descriptions, Flex, Popconfirm, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  parseAccessActions,
  useCan,
  useCavingGroups,
  useCave,
  useDeleteTripLog,
  useEffectiveAccess,
  useTripLog,
  useUpdateTripLog,
  type TripLogWrite,
} from '../../api/hooks.ts';
import AttachmentSection from '../../components/attachments/AttachmentSection.tsx';
import HistoryPanel, { type HistoryRestore } from '../../components/history/HistoryPanel.tsx';
import { applyRestore } from '../../components/history/historyModel.ts';
import LinksSection from '../../components/reslinks/LinksSection.tsx';
import TagChips from '../../components/tags/TagChips.tsx';
import TripFormModal from './TripFormModal.tsx';

function CaveLink({ caveId }: { caveId: string }) {
  const { data: cave } = useCave(caveId);
  return <Link to={`/caves/${caveId}`}>{cave?.name ?? caveId}</Link>;
}

// Server times are "HH:mm:ss"; show "HH:mm" and derive the underground duration (handling a
// crossing of midnight) when both ends are present.
function formatTimeRange(entry: string | null | undefined, exit: string | null | undefined): string | null {
  if (!entry && !exit) return null;
  const range = `${entry?.slice(0, 5) ?? '—'} – ${exit?.slice(0, 5) ?? '—'}`;
  if (!entry || !exit) return range;
  const [eh, em] = entry.split(':').map(Number);
  const [xh, xm] = exit.split(':').map(Number);
  let minutes = xh * 60 + xm - (eh * 60 + em);
  if (minutes < 0) minutes += 24 * 60;
  return `${range} (${Math.floor(minutes / 60)}h ${minutes % 60}m)`;
}

export default function TripLogDetailPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { id } = useParams<{ id: string }>();
  const { data: trip, isPending } = useTripLog(id);
  const { data: cavingGroups } = useCavingGroups();
  const organizingCavingGroup = cavingGroups?.find((g) => g.id === trip?.organizingCavingGroupId);
  const deleteTrip = useDeleteTripLog();
  const updateTrip = useUpdateTripLog();
  // Per-object capabilities once the answer arrives; the coarse domain-level check only
  // bridges the first render (the server enforces regardless).
  const { data: effective } = useEffectiveAccess('tripLog', id);
  const domainFallback = useCan('tripLogs', 'write');
  const held = effective ? parseAccessActions(effective.actions) : null;
  const [editing, setEditing] = useState(false);

  if (isPending || !trip) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  const canEdit = held ? held.has('write') : domainFallback;
  const canDelete = held ? held.has('delete') : domainFallback;
  const dateText = trip.tripDateEnd
    ? `${new Date(trip.tripDate).toLocaleDateString(i18n.resolvedLanguage)} – ${new Date(trip.tripDateEnd).toLocaleDateString(i18n.resolvedLanguage)}`
    : new Date(trip.tripDate).toLocaleDateString(i18n.resolvedLanguage);
  const timeText = formatTimeRange(trip.entryTime, trip.exitTime);

  const onDelete = async () => {
    try {
      await deleteTrip.mutateAsync(trip.id);
      message.success(t('common.deleted'));
      navigate('/trip-logs');
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <div style={{ padding: 24, maxWidth: 900 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {trip.title}
        </Typography.Title>
        {(canEdit || canDelete) && (
          <Flex gap={8}>
            {canEdit && (
              <Button icon={<EditOutlined />} onClick={() => setEditing(true)}>
                {t('trips.edit')}
              </Button>
            )}
            {canDelete && (
              <Popconfirm title={t('trips.deleteConfirm')} onConfirm={() => void onDelete()}>
                <Button danger icon={<DeleteOutlined />}>
                  {t('features.delete')}
                </Button>
              </Popconfirm>
            )}
          </Flex>
        )}
      </Flex>

      <Card size="small">
        <Descriptions column={1} size="small">
          {trip.type && (
            <Descriptions.Item label={t('trips.type')}>
              <Tag>{t(`trips.typeValues.${trip.type}`)}</Tag>
            </Descriptions.Item>
          )}
          <Descriptions.Item label={t('trips.date')}>{dateText}</Descriptions.Item>
          {timeText && <Descriptions.Item label={t('trips.duration')}>{timeText}</Descriptions.Item>}
          {trip.locationText && (
            <Descriptions.Item label={t('trips.location')}>{trip.locationText}</Descriptions.Item>
          )}
          {organizingCavingGroup && (
            <Descriptions.Item label={t('trips.organizingCavingGroup')}>
              {organizingCavingGroup.name}
            </Descriptions.Item>
          )}
          {trip.caveIds.length > 0 && (
            <Descriptions.Item label={t('trips.caves')}>
              <Flex gap={8} wrap>
                {trip.caveIds.map((caveId) => (
                  <CaveLink key={caveId} caveId={caveId} />
                ))}
              </Flex>
            </Descriptions.Item>
          )}
          {trip.participants.length > 0 && (
            <Descriptions.Item label={t('trips.participants')}>
              <Flex gap={4} wrap>
                {trip.participants.map((p) => (
                  <Tag key={p.caverId}>{p.name}</Tag>
                ))}
              </Flex>
            </Descriptions.Item>
          )}
          {trip.proposers.length > 0 && (
            <Descriptions.Item label={t('trips.proposers')}>
              <Flex gap={4} wrap>
                {trip.proposers.map((p) => (
                  <Tag key={p.caverId}>{p.name}</Tag>
                ))}
              </Flex>
            </Descriptions.Item>
          )}
          {trip.weatherConditions && (
            <Descriptions.Item label={t('trips.weather')}>{trip.weatherConditions}</Descriptions.Item>
          )}
          <Descriptions.Item label={t('features.visibility')}>
            <Tag>{t(`caves.visibilityValues.${trip.visibility}`)}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label={t('tags.title')}>
            <TagChips entityType="tripLog" entityId={trip.id} canEdit={canEdit} />
          </Descriptions.Item>
        </Descriptions>
        {trip.results && (
          <>
            <Typography.Text strong>{t('trips.results')}</Typography.Text>
            <Typography.Paragraph style={{ marginTop: 4, whiteSpace: 'pre-wrap' }}>
              {trip.results}
            </Typography.Paragraph>
          </>
        )}
        {trip.description && (
          <>
            <Typography.Text strong>{t('features.description')}</Typography.Text>
            <Typography.Paragraph style={{ marginTop: 4, whiteSpace: 'pre-wrap' }}>
              {trip.description}
            </Typography.Paragraph>
          </>
        )}
      </Card>

      <LinksSection entityType="tripLog" entityId={trip.id} canAdd entityTitle={trip.title} />

      <AttachmentSection entityType="tripLog" entityId={trip.id} canEdit={canEdit} reportSlot />

      <HistoryPanel
        entityType="tripLog"
        entityId={trip.id}
        restore={
          canEdit
            ? ({
                entityType: 'TripLog',
                onRestore: async (event, props) => {
                  await updateTrip.mutateAsync({
                    id: trip.id,
                    body: applyRestore(trip as unknown as TripLogWrite, event.changes, props),
                  });
                },
              } satisfies HistoryRestore)
            : undefined
        }
      />

      <TripFormModal open={editing} trip={trip} onClose={() => setEditing(false)} />
    </div>
  );
}
