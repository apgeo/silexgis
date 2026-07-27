// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EditOutlined, EnvironmentOutlined, LockOutlined, PlusOutlined } from '@ant-design/icons';
import { Alert, App, Button, Card, Descriptions, Flex, Popconfirm, Spin, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import {
  useCave,
  useCaveTypes,
  useDeleteCave,
  useDeleteEntrance,
  useEntranceTypes,
  useEntrances,
  useMe,
  useRockTypes,
  useUpdateCave,
  useUpdateEntrance,
  type CaveWrite,
  type Entrance,
  type EntranceWrite,
} from '../../api/hooks.ts';
import { formatLonLat } from '../../geo/coords.ts';
import AttachmentSection from '../../components/attachments/AttachmentSection.tsx';
import HistoryPanel, { type HistoryRestore } from '../../components/history/HistoryPanel.tsx';
import { applyRestore } from '../../components/history/historyModel.ts';
import PermissionsModal from '../../components/permissions/PermissionsModal.tsx';
import TagChips from '../../components/tags/TagChips.tsx';
import CenterlineSection from './CenterlineSection.tsx';
import EntranceEditorModal from '../../components/caves/EntranceEditorModal.tsx';
import SurveyModelSection from './SurveyModelSection.tsx';

export default function CaveDetailPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { id } = useParams<{ id: string }>();

  const { data: cave, isPending } = useCave(id);
  const { data: entrances } = useEntrances(id);
  const { data: caveTypes } = useCaveTypes();
  const { data: rockTypes } = useRockTypes();
  const { data: entranceTypes } = useEntranceTypes();
  const deleteCave = useDeleteCave();
  const deleteEntrance = useDeleteEntrance(id ?? '');
  const updateCave = useUpdateCave(id ?? '');
  const updateEntrance = useUpdateEntrance(id ?? '');
  const { data: me } = useMe();
  const canEdit = me?.roles.some((r) => ['Admin', 'Manager', 'Editor'].includes(r)) ?? false;

  const [editorOpen, setEditorOpen] = useState(false);
  const [editingEntrance, setEditingEntrance] = useState<Entrance | null>(null);
  const [permissionsOpen, setPermissionsOpen] = useState(false);

  if (isPending || !cave) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  const caveTypeName = caveTypes?.find((x) => x.id === cave.caveTypeId)?.name;
  const rockTypeName = rockTypes?.find((x) => x.id === cave.rockTypeId)?.name;
  const entranceTypeName = (typeId: number) => entranceTypes?.find((x) => x.id === typeId)?.name ?? '';

  const detailItem = (label: string, value: unknown) =>
    value == null || value === '' ? null : (
      <Descriptions.Item key={label} label={label}>
        {String(value)}
      </Descriptions.Item>
    );

  const onDeleteCave = async () => {
    try {
      await deleteCave.mutateAsync(cave.id);
      message.success(t('common.deleted'));
      navigate('/caves');
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <div style={{ padding: 24, maxWidth: 1100 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {cave.name}
        </Typography.Title>
        <Flex gap={8}>
          {canEdit && (
            <Button icon={<LockOutlined />} onClick={() => setPermissionsOpen(true)}>
              {t('permissions.button')}
            </Button>
          )}
          <Button icon={<EditOutlined />} onClick={() => navigate(`/caves/${cave.id}/edit`)}>
            {t('caves.edit')}
          </Button>
          <Popconfirm title={t('caves.deleteConfirm')} onConfirm={() => void onDeleteCave()}>
            <Button danger icon={<DeleteOutlined />}>
              {t('caves.delete')}
            </Button>
          </Popconfirm>
        </Flex>
      </Flex>

      {cave.approximateLocation && (
        <Alert type="warning" showIcon message={t('map.approximate')} style={{ marginBottom: 12 }} />
      )}

      <Card style={{ marginBottom: 16 }}>
        {/* Three columns of label+value do not fit a phone: antd would keep them and let the
            values wrap to unreadable slivers. One column below sm, two through md. */}
        <Descriptions column={{ xs: 1, sm: 2, md: 3 }} size="small">
          {detailItem(t('caves.type'), caveTypeName)}
          {detailItem(t('caves.identificationCode'), cave.identificationCode)}
          {detailItem(t('caves.region'), cave.region)}
          {detailItem(t('caves.fields.otherToponyms'), cave.otherToponyms)}
          {detailItem(t('caves.fields.hydrographicBasin'), cave.hydrographicBasin)}
          {detailItem(t('caves.fields.valley'), cave.valley)}
          {detailItem(t('caves.fields.rockType'), rockTypeName)}
          {detailItem(t('caves.fields.surveyedLength'), cave.surveyedLength)}
          {detailItem(t('caves.depth'), cave.depth)}
          {detailItem(t('caves.fields.altitude'), cave.altitude)}
          {detailItem(t('caves.fields.discoveryDate'), cave.discoveryDate)}
          {detailItem(t('caves.fields.discoverer'), cave.discoverer)}
          <Descriptions.Item label={t('caves.visibility')}>
            <Tag>{t(`caves.visibilityValues.${cave.visibility}`)}</Tag>
          </Descriptions.Item>
        </Descriptions>
        {cave.description && (
          <Typography.Paragraph style={{ marginTop: 12, marginBottom: 0 }}>
            {cave.description}
          </Typography.Paragraph>
        )}
      </Card>

      <Card
        title={t('caves.entrances')}
        extra={
          <Button
            size="small"
            icon={<PlusOutlined />}
            onClick={() => {
              setEditingEntrance(null);
              setEditorOpen(true);
            }}
          >
            {t('entrances.add')}
          </Button>
        }
      >
        <Table<Entrance>
          scroll={{ x: 'max-content' }}
          rowKey="id"
          size="small"
          dataSource={entrances}
          pagination={false}
          columns={[
            { title: t('caves.name'), dataIndex: 'name' },
            { title: t('caves.type'), dataIndex: 'entranceTypeId', render: entranceTypeName, width: 160 },
            {
              title: t('entrances.coordinates'),
              key: 'coords',
              render: (_, e) => (
                <span>
                  <EnvironmentOutlined /> {formatLonLat(e.geom.coordinates[0], e.geom.coordinates[1])}
                </span>
              ),
            },
            { title: t('caves.fields.altitude'), dataIndex: 'altitude', width: 100, align: 'right' },
            {
              title: t('entrances.main'),
              dataIndex: 'isMain',
              width: 80,
              render: (isMain: boolean) => (isMain ? <Tag color="green">✓</Tag> : null),
            },
            {
              title: t('entrances.positionQuality'),
              dataIndex: 'positionQuality',
              width: 130,
              render: (value: string) => t(`entrances.qualityValues.${value}`),
            },
            {
              key: 'actions',
              width: 110,
              render: (_, e) => (
                <Flex gap={4}>
                  <Button
                    size="small"
                    type="text"
                    icon={<EditOutlined />}
                    onClick={() => {
                      setEditingEntrance(e);
                      setEditorOpen(true);
                    }}
                  />
                  <Popconfirm
                    title={t('entrances.deleteConfirm')}
                    onConfirm={() => void deleteEntrance.mutateAsync(e.id)}
                  >
                    <Button size="small" type="text" danger icon={<DeleteOutlined />} />
                  </Popconfirm>
                </Flex>
              ),
            },
          ]}
        />
      </Card>

      {id && (
        <div style={{ marginTop: 12 }}>
          <TagChips entityType="cave" entityId={id} canEdit={canEdit} />
        </div>
      )}

      {id && <SurveyModelSection caveId={id} canEdit={canEdit} />}

      {id && <CenterlineSection caveId={id} canEdit={canEdit} />}

      {id && <AttachmentSection entityType="cave" entityId={id} canEdit={canEdit} />}

      {id && (
        <HistoryPanel
          entityType="cave"
          entityId={id}
          restore={
            canEdit
              ? ([
                  {
                    entityType: 'Cave',
                    onRestore: async (event, props) => {
                      await updateCave.mutateAsync(applyRestore(cave as unknown as CaveWrite, event.changes, props));
                    },
                  },
                  {
                    // Entrance events surface in the cave timeline (audit root = cave);
                    // restore composes a PUT on the entrance the row belongs to.
                    entityType: 'CaveEntrance',
                    onRestore: async (event, props) => {
                      const entrance = entrances?.find((e) => e.id === event.entityId);
                      if (!entrance) {
                        // The row's entrance was deleted since; surface it as a failed restore
                        // rather than a silent no-op (HistoryPanel shows the error toast).
                        throw new Error('entrance no longer exists');
                      }
                      await updateEntrance.mutateAsync({
                        id: event.entityId,
                        body: applyRestore(entrance as unknown as EntranceWrite, event.changes, props),
                      });
                    },
                  },
                ] satisfies HistoryRestore[])
              : undefined
          }
        />
      )}

      {id && (
        <PermissionsModal
          entityType="cave"
          entityId={id}
          open={permissionsOpen}
          onClose={() => setPermissionsOpen(false)}
        />
      )}

      {id && (
        <EntranceEditorModal
          caveId={id}
          entrance={editingEntrance}
          defaultCenter={
            cave.mainGeom ? [cave.mainGeom.coordinates[0], cave.mainGeom.coordinates[1]] : [25.3, 45.7]
          }
          open={editorOpen}
          onClose={() => setEditorOpen(false)}
        />
      )}
    </div>
  );
}
