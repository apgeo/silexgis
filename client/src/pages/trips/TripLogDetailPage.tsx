// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EditOutlined } from '@ant-design/icons';
import { App, Button, Card, Descriptions, Flex, Popconfirm, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  useCave,
  useDeleteTripLog,
  useMe,
  useTripLog,
  useUpdateTripLog,
  type TripLogWrite,
} from '../../api/hooks.ts';
import AttachmentSection from '../../components/attachments/AttachmentSection.tsx';
import HistoryPanel, { type HistoryRestore } from '../../components/history/HistoryPanel.tsx';
import { applyRestore } from '../../components/history/historyModel.ts';
import TagChips from '../../components/tags/TagChips.tsx';
import TripFormModal from './TripFormModal.tsx';

function CaveLink({ caveId }: { caveId: string }) {
  const { data: cave } = useCave(caveId);
  return <Link to={`/caves/${caveId}`}>{cave?.name ?? caveId}</Link>;
}

export default function TripLogDetailPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { id } = useParams<{ id: string }>();
  const { data: trip, isPending } = useTripLog(id);
  const { data: me } = useMe();
  const deleteTrip = useDeleteTripLog();
  const updateTrip = useUpdateTripLog();
  const [editing, setEditing] = useState(false);

  if (isPending || !trip) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  const canEdit = me?.roles.some((r) => ['Admin', 'Manager', 'Editor'].includes(r)) ?? false;
  const dateText = trip.tripDateEnd
    ? `${new Date(trip.tripDate).toLocaleDateString(i18n.resolvedLanguage)} – ${new Date(trip.tripDateEnd).toLocaleDateString(i18n.resolvedLanguage)}`
    : new Date(trip.tripDate).toLocaleDateString(i18n.resolvedLanguage);

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
        {canEdit && (
          <Flex gap={8}>
            <Button icon={<EditOutlined />} onClick={() => setEditing(true)}>
              {t('trips.edit')}
            </Button>
            <Popconfirm title={t('trips.deleteConfirm')} onConfirm={() => void onDelete()}>
              <Button danger icon={<DeleteOutlined />}>
                {t('features.delete')}
              </Button>
            </Popconfirm>
          </Flex>
        )}
      </Flex>

      <Card size="small">
        <Descriptions column={1} size="small">
          <Descriptions.Item label={t('trips.date')}>{dateText}</Descriptions.Item>
          {trip.locationText && (
            <Descriptions.Item label={t('trips.location')}>{trip.locationText}</Descriptions.Item>
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
                {trip.participants.map((p, index) => (
                  <Tag key={index}>{p.displayName ?? p.nameText}</Tag>
                ))}
              </Flex>
            </Descriptions.Item>
          )}
          <Descriptions.Item label={t('features.visibility')}>
            <Tag>{t(`caves.visibilityValues.${trip.visibility}`)}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label={t('tags.title')}>
            <TagChips entityType="tripLog" entityId={trip.id} canEdit={canEdit} />
          </Descriptions.Item>
        </Descriptions>
        {trip.description && (
          <Typography.Paragraph style={{ marginTop: 12, whiteSpace: 'pre-wrap' }}>
            {trip.description}
          </Typography.Paragraph>
        )}
      </Card>

      <AttachmentSection entityType="tripLog" entityId={trip.id} canEdit={canEdit} />

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
